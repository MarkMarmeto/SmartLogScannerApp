# PL0018: Scanner Heartbeat Service

> **Status:** Draft
> **Story:** [US0120: Implement Heartbeat Service](../stories/US0120-heartbeat-service.md)
> **Epic:** EP0005: Scanner Integration (cross-project — epic tracked in WebApp)
> **Created:** 2026-04-28
> **Language:** C# 12 / .NET 8 MAUI (Windows + macOS)
> **Drafted by:** Claude (Opus 4.7)

---

## Overview

Implement a singleton `IHeartbeatService` that POSTs `/api/v1/devices/heartbeat` every 60 s, with exponential backoff up to 5 min on failure, configurable via `appsettings.json`, and best-effort semantics (no queue, no replay). Lifecycle and HttpClient/TLS patterns mirror the existing `HealthCheckService`. The service runs in parallel with `HealthCheckService` — health check *pulls* (drives the local online indicator), heartbeat *pushes* (reports vitals to the admin).

No new tables, no Setup UI changes, no offline-queue interaction beyond reading `GetQueueCountAsync()` for the payload, no scanner-side UI at all (Tony sees health on the WebApp).

---

## Acceptance Criteria Mapping

| AC (US0120) | Phase |
|-------------|-------|
| AC1: Singleton, starts after setup | Phase 3 — DI + MainViewModel hook |
| AC2: POST every 60 s base | Phase 1 — variable-interval loop |
| AC3: 204 resets interval | Phase 1 — success path |
| AC4: Exponential backoff to 5 min | Phase 1 — failure path |
| AC5: Payload from MAUI / scan / queue | Phase 2 — `BuildPayloadAsync` |
| AC6: TLS / self-signed | Phase 1 — HttpClient construction (HealthCheckService:158-175 mirror) + Phase 3 named-client registration |
| AC7: Polling starts only after setup | Phase 3 — MainViewModel.cs:194 pattern |
| AC8: Clean shutdown | Phase 1 — `CancellationTokenSource` + `IAsyncDisposable` |

---

## Technical Context (Verified)

### Existing service patterns to mirror
- `HealthCheckService` (`SmartLog.Scanner.Core/Services/HealthCheckService.cs`) — singleton, registered in `MauiProgram.cs:326`, started via `_healthCheck.StartAsync()` from `MainViewModel.cs:194`. **Heartbeat follows the same lifecycle exactly.**
- HttpClient construction at `HealthCheckService.cs:158-175` — reads `IPreferencesService.GetAcceptSelfSignedCerts()`, builds an inline `HttpClientHandler` with `ServerCertificateCustomValidationCallback` when needed; otherwise uses `IHttpClientFactory.CreateClient("HealthCheck")`. **Heartbeat reuses this exact pattern but with a new dedicated named client `"Heartbeat"` registered in `MauiProgram.cs` alongside `"HealthCheck"` (line 191) — must NOT reuse `"SmartLogApi"` (line 210), which has Polly retry/circuit-breaker that would double-up our loop's backoff.**

### Verified service interfaces
- `ISecureConfigService.GetApiKeyAsync()` — exists, returns `Task<string?>` (line 14 of `ISecureConfigService.cs`)
- `IPreferencesService.GetServerBaseUrl()` — line 15
- `IPreferencesService.GetAcceptSelfSignedCerts()` — line 118
- `IOfflineQueueService.GetQueueCountAsync()` — line 24 (**not** `GetQueuedCountAsync` as US0120 originally stated)
- `IScanHistoryService.GetRecentLogsAsync(1)` returns `List<ScanLogEntry>` (most recent first, **any status**). `ScanLogEntry.Timestamp` is `DateTimeOffset` — convert to UTC via `.UtcDateTime` for the payload. **Per A1 decision: take whatever's first regardless of status — server ignores `lastScanAt` anyway (informational only per WebApp PL0039 Phase 2).**
- `ScanStatistics` (`IScanHistoryService.GetTodayStatisticsAsync()`) — counts only, **no `LastScanAt` field**. Not used here.

### appsettings.json existing config
`SmartLog.Scanner/Resources/Raw/appsettings.json:11-12` has `OfflineQueue.HealthCheckIntervalSeconds: 15`. New `Heartbeat` section is added as a sibling, not nested under OfflineQueue.

### MAUI device APIs
- `AppInfo.VersionString` — app version
- `DeviceInfo.VersionString` — OS version
- `Battery.Default.ChargeLevel` (0.0–1.0) and `Battery.Default.State` — wrap in try/catch; null on desktop where unsupported
- `Connectivity.NetworkAccess` and `Connectivity.ConnectionProfiles` — for `networkType` derivation

---

## Implementation Phases

### Phase 1 — `IHeartbeatService` interface + `HeartbeatService` core loop

**New file:** `SmartLog.Scanner.Core/Services/IHeartbeatService.cs`

```csharp
namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0120: Periodic heartbeat sender. POSTs scanner vitals to the admin server.
/// Distinct from IHealthCheckService — health check pulls (GET) for local online indicator;
/// heartbeat pushes (POST) for admin-side health monitoring.
/// </summary>
public interface IHeartbeatService
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
```

**New file:** `SmartLog.Scanner.Core/Services/HeartbeatService.cs`

Loop using `Task.Delay` (not `PeriodicTimer` — needed for variable backoff):

```csharp
public class HeartbeatService : IHeartbeatService, IAsyncDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly IPreferencesService _preferences;       // server URL + AcceptSelfSignedCerts
    private readonly ISecureConfigService _secureConfig;     // API key
    private readonly IOfflineQueueService _offlineQueue;     // queued scan count
    private readonly IScanHistoryService _scanHistory;       // last scan timestamp
    private readonly ILogger<HeartbeatService> _logger;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private int _baseIntervalSeconds;
    private int _maxBackoffSeconds;
    private int _requestTimeoutSeconds;
    private int _currentIntervalSeconds;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_cts != null)
        {
            _logger.LogWarning("HeartbeatService already started — ignoring duplicate StartAsync");
            return Task.CompletedTask;
        }

        // Read config from appsettings.json with silent clamp on out-of-range values (A2 decision)
        _baseIntervalSeconds = Math.Clamp(
            _config.GetValue<int>("Heartbeat:BaseIntervalSeconds", 60), 30, 600);
        _maxBackoffSeconds = Math.Clamp(
            _config.GetValue<int>("Heartbeat:MaxBackoffSeconds", 300), 60, 3600);
        _requestTimeoutSeconds = Math.Clamp(
            _config.GetValue<int>("Heartbeat:RequestTimeoutSeconds", 10), 5, 60);
        _currentIntervalSeconds = _baseIntervalSeconds;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
        _logger.LogInformation("HeartbeatService started (base={Base}s, max={Max}s)",
            _baseIntervalSeconds, _maxBackoffSeconds);
        return Task.CompletedTask;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var success = await SendHeartbeatAsync(ct);
                _currentIntervalSeconds = success
                    ? _baseIntervalSeconds
                    : Math.Min(_currentIntervalSeconds * 2, _maxBackoffSeconds);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in heartbeat loop");
                _currentIntervalSeconds = Math.Min(_currentIntervalSeconds * 2, _maxBackoffSeconds);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(_currentIntervalSeconds), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task StopAsync() { /* cancel cts, await _loopTask, dispose */ }
    public async ValueTask DisposeAsync() => await StopAsync();
}
```

**`SendHeartbeatAsync` highlights:**
- Resolve API key via `_secureConfig.GetApiKeyAsync()`. If null/empty, skip silently (setup incomplete) and treat as "success" so backoff doesn't activate during fresh installs.
- Resolve server URL via `_preferences.GetServerBaseUrl()`. Skip silently if empty (treat as success — same rationale as empty key).
- Build URL: `$"{baseUrl.TrimEnd('/')}/api/v1/devices/heartbeat"`
- Construct HttpClient identically to `HealthCheckService.cs:158-175`:
  - If `AcceptSelfSignedCerts == true`: build inline `HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = certificateValidator }) { Timeout = TimeSpan.FromSeconds(_requestTimeoutSeconds) }`
  - Else: `_httpClientFactory.CreateClient("Heartbeat")` — **dedicated named client; not `"SmartLogApi"` (which has Polly retries that would conflict with our loop's backoff). Registered in Phase 3.**
- Use `HttpRequestMessage` with method POST, `RequestUri = healthUrl`, and add `X-API-Key` header on `request.Headers` (single-shot, not on `httpClient.DefaultRequestHeaders` which is shared across calls when using factory clients).
- Set `Content = JsonContent.Create(payload)`; send via `httpClient.SendAsync(request, ct)`; expect `204 NoContent`. Treat any 2xx as success; non-2xx (including 401 revoked, 5xx) as failure.
- Catch `HttpRequestException`, `OperationCanceledException` (distinguish timeout from caller cancellation by checking `ct.IsCancellationRequested`), and generic `Exception` — return `false` for backoff in all failure cases.
- Dispose the ad-hoc HttpClient/handler in `finally` only when constructed inline (factory clients are managed by `IHttpClientFactory` and must not be disposed).

### Phase 2 — Payload composition

**Private record inside `HeartbeatService.cs`:**

```csharp
private sealed record HeartbeatPayload(
    string? AppVersion,
    string? OsVersion,
    int? BatteryPercent,
    bool? IsCharging,
    string? NetworkType,
    DateTime? LastScanAt,
    int? QueuedScansCount,
    DateTime ClientTimestamp);
```

**`BuildPayloadAsync(CancellationToken)` composes from:**
- `AppVersion` — `SafeGet(() => AppInfo.VersionString)` (try/catch → null)
- `OsVersion` — `SafeGet(() => DeviceInfo.VersionString)`
- Battery — single try/catch around `Battery.Default.ChargeLevel` and `.State`. On any exception (desktop platforms where MAUI Battery is unimplemented), both `BatteryPercent` and `IsCharging` are null. **No platform-specific branching** — exception-based fallback keeps the code path uniform.
- `NetworkType` — `MapNetworkType(Connectivity.NetworkAccess, Connectivity.ConnectionProfiles)` returning `"WIFI"` / `"ETHERNET"` / `"CELLULAR"` / `"OFFLINE"`. Wrap in try/catch returning `"OFFLINE"` on failure.
- `LastScanAt` — `(await _scanHistory.GetRecentLogsAsync(1)).FirstOrDefault()?.Timestamp.UtcDateTime` (per A1: most recent of any status; `Timestamp` is `DateTimeOffset`, `.UtcDateTime` returns `DateTime` in UTC). Defensive: try/catch returning null.
- `QueuedScansCount` — `await _offlineQueue.GetQueueCountAsync()`. try/catch returning null.
- `ClientTimestamp` — `DateTime.UtcNow`

**Decisions:**
- All sub-failures are swallowed and rendered as `null` — heartbeat itself must never fail because of a missing peripheral. Logged at Debug (not Warning) to avoid log spam on desktop where Battery is permanently unavailable.
- Fields are passed by name in the record so JSON serialization uses camelCase via `JsonSerializerOptions` already configured app-wide (verify default; if PascalCase, set `[JsonPropertyName]` attributes on each field).

### Phase 3 — DI registration + named HttpClient + lifecycle wiring

**File:** `SmartLog.Scanner/MauiProgram.cs`

Two additions:

1. **Register the dedicated named HttpClient `"Heartbeat"`** alongside `"HealthCheck"` (after the existing block at line 191–207, before the `"SmartLogApi"` block at line 210). Mirror the `"HealthCheck"` config — TLS handler with the same `certificateValidator` already in scope, no Polly:
```csharp
// Register dedicated HttpClient for heartbeat (no Polly — backoff is managed by HeartbeatService loop)
builder.Services.AddHttpClient("Heartbeat")
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new HttpClientHandler();
        if (acceptSelfSigned)
        {
            handler.ServerCertificateCustomValidationCallback = certificateValidator;
        }
        return handler;
    })
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
    });
// NOTE: No Polly — HeartbeatService implements its own exponential backoff (60s → 300s cap).
```

2. **Register the service** immediately after the `HealthCheckService` registration at line 326:
```csharp
builder.Services.AddSingleton<IHeartbeatService, HeartbeatService>();
```

**File:** `SmartLog.Scanner/ViewModels/MainViewModel.cs`

- Inject `IHeartbeatService _heartbeat` in the ctor (next to `_healthCheck`).
- After `await _healthCheck.StartAsync();` at line 194, add:
```csharp
await _heartbeat.StartAsync();
```
- **No explicit `StopAsync` call** — the service implements `IAsyncDisposable`, so the DI container fires `DisposeAsync()` → `StopAsync()` automatically on app shutdown. Process termination covers abrupt close. Keeps the call site symmetric to whatever HealthCheckService does without verification overhead.

### Phase 4 — Tests

**New file:** `SmartLog.Scanner.Tests/Services/HeartbeatServiceTests.cs`

Tests are trickier than HealthCheckService because the service has more dependencies. Use Moq for everything; no MAUI APIs are exercised in tests (the `Battery`/`Connectivity` calls are inside `BuildPayloadAsync`, which we'll only test the success path of via integration; backoff logic is testable in isolation).

**Strategy:** Extract the loop-driving logic into a testable seam. Either:
- **(A)** Make `SendHeartbeatAsync` `internal` + `[InternalsVisibleTo]` and unit-test it directly with a mocked `HttpMessageHandler`. Drive backoff state via direct method calls, not the loop.
- **(B)** Make the backoff calculation a pure static method `ComputeNextInterval(currentSeconds, success, baseSeconds, maxSeconds)` and unit-test that, plus light integration tests of `SendHeartbeatAsync` with mocked HttpClient.

**Recommended: (B)** — keeps the service surface clean and the testable bit doesn't depend on async/cancellation.

**Test cases:**
- `ComputeNextInterval` — table test: success → base; failure → 2× capped at max; failure at cap stays at cap
- `SendHeartbeatAsync` happy path → returns true, mocked HttpClient receives POST with X-API-Key header and `204` response handled correctly
- `SendHeartbeatAsync` with empty API key → returns true (no-op), no POST sent
- `SendHeartbeatAsync` with empty server URL → returns true (no-op), no POST sent
- `SendHeartbeatAsync` with HttpRequestException → returns false
- `SendHeartbeatAsync` with timeout → returns false
- `SendHeartbeatAsync` with 401 response → returns false (so backoff applies on revoked devices)
- `BuildPayloadAsync` — verify `QueuedScansCount` reflects `IOfflineQueueService.GetQueueCountAsync()` mock; verify `LastScanAt` from `IScanHistoryService.GetRecentLogsAsync(1)` mock
- `StartAsync` twice → second call is a no-op (logged warning, not exception)
- `StopAsync` → cancels in-progress delay; `_loopTask` completes without throwing

**Mocking HttpMessageHandler:** standard pattern — `new Mock<HttpMessageHandler>(MockBehavior.Strict)` with `.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ...)`. Project already uses this in `ConnectionTestServiceTests` — mirror that.

### Phase 5 — appsettings.json + cross-platform smoke

**File:** `SmartLog.Scanner/Resources/Raw/appsettings.json`

Add a sibling section to `OfflineQueue`:
```json
"Heartbeat": {
    "BaseIntervalSeconds": 60,
    "MaxBackoffSeconds": 300,
    "RequestTimeoutSeconds": 10
}
```

**Manual verification (after build):**

1. **macOS dev build** — `dotnet run --project SmartLog.Scanner -f net8.0-maccatalyst`
   - Setup → MainPage → wait 60 s → check Serilog console output for "heartbeat sent (204)"
   - Open WebApp `/Admin/Devices` → device shows green Online badge with "Last seen Xs ago"
2. **Backoff** — kill the WebApp briefly, watch scanner log: failures every 60 s → 120 s → 240 s → 300 s. Restart WebApp → next successful heartbeat resets to 60 s. WebApp UI flips to Online within ≤60 s.
3. **Config override via appsettings.json** — change `Heartbeat:BaseIntervalSeconds` to `30`, restart app, observe heartbeats every 30 s. Try `5` → clamped to `30` (silent). Try `9999` → clamped to `600` (silent).
4. **Revoked device** — revoke device in WebApp; scanner heartbeat returns 401; logs Warning; backoff applies; loop continues without crash.
5. **Cross-build Windows TFM** — `dotnet build SmartLog.Scanner -f net8.0-windows10.0.19041.0`. (Cannot launch from macOS; just verify clean compile.)
6. **`dotnet test SmartLog.Scanner.Tests`** — green.

---

## Risks & Considerations

- **Battery API on desktop** — MAUI's `Battery.Default.ChargeLevel` may throw `FeatureNotSupportedException` on Windows desktops. Caught silently → null. Acceptable per US0120 AC5 + your decision.
- **Network type detection** — when a Windows machine has both Ethernet and Wi-Fi connected, `ConnectionProfiles` returns both. Plan picks the first match in priority order (Ethernet → Wi-Fi → Cellular). Document; revisit if Tony reports misclassification.
- **First-run race** — if `StartAsync` fires before `SecureConfigService` has migrated keys (App.xaml.cs runs `SecurityMigrationService` on startup before navigating to MainPage), the API key may be missing on first heartbeat attempt. Mitigation: in `SendHeartbeatAsync`, an empty key is treated as "success/no-op" (no failure, no backoff), so the next 60 s tick will succeed. No crash, no spam.
- **Clock skew** — server uses its own UTC for `LastSeenAt` / `LastHeartbeatAt`. `clientTimestamp` is informational only and not persisted server-side (per WebApp PL0039 Phase 2 decision).
- **Heartbeat flooding the admin DB** — at ~10 scanners × 1 heartbeat/min = 14,400 row updates/day on the `Devices` table by PK. Negligible. Already acknowledged in WebApp PL0039 risks.
- **Wrong named-client reuse** — must register a new `"Heartbeat"` HttpClient, **not** reuse `"SmartLogApi"`. The latter has Polly retry/circuit-breaker which would compound with our loop's exponential backoff (Polly retries inline first, then loop retries on top). Plan calls this out in Phase 3.
- **Interval misconfiguration in appsettings** — clamp on `Math.Clamp(value, 30, 600)` prevents pathological values (e.g. `1` → flooding, `0` → divide-by-zero in delay math). Silent clamp per A2; admin sees the actual interval in Serilog startup line.

---

## Out of Scope

- Heartbeat retry queue / replay on reconnect — best-effort per AC4.
- Status indicator in scanner UI — Tony sees health on the WebApp; scanners don't need to know their own health state for operational use.
- Per-platform native API for battery (e.g. WinRT `Battery` directly) — try/catch on MAUI's API is sufficient.
- Refactoring `HealthCheckService` to share HttpClient construction logic with `HeartbeatService` — deferred (separate cleanup story).
- Telemetry / metrics on heartbeat success rate — Serilog logs are the source of truth.
- Setup-UI surface for the heartbeat interval — explicitly out per A3 decision. Configuration is appsettings.json only; restart-required.
- Hot-reload of interval after appsettings change — restart-required.

---

## Estimated Effort

| Phase | Time |
|-------|------|
| 1 — IHeartbeatService + core loop | ~45 min |
| 2 — Payload composition | ~30 min |
| 3 — DI + named HttpClient + MainViewModel hook | ~20 min |
| 4 — Tests | ~45 min |
| 5 — appsettings + manual verify | ~30 min |
| **Total** | **~2.5 hours** |

Aligns with the 3-pt estimate in US0120.

---

## Rollout Plan

1. Phase 1 + 2 — implement service, run unit tests for `ComputeNextInterval` and `SendHeartbeatAsync` in isolation.
2. Phase 3 — register named HttpClient + service in `MauiProgram.cs`; wire `_heartbeat.StartAsync()` into `MainViewModel`. Full app smoke on macOS.
3. Phase 4 — full test suite green (`dotnet test`).
4. Phase 5 — appsettings update; macOS + Windows TFM cross-build; live verification against running WebApp (Online badge, backoff, revoke flow).
5. Confirm with user before commit.
6. Commit on `dev` branch; PR to `main` per project workflow.

---

## Open Questions

> All resolved. Plan is ready for execution.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-28 | Claude (Opus 4.7) | Initial plan drafted; verified service interfaces; corrected story method names (GetQueueCountAsync, IScanHistoryService); locked Setup-UI-for-interval and null-battery-OK answers from user |
| 2026-04-28 | Claude (Opus 4.7) | Review pass: dropped Phase 3 (Setup UI) per A3 decision — interval via appsettings.json only with silent clamp 30..600. Renumbered phases. Locked A1 (last scan = any status) and B1 (dedicated `"Heartbeat"` named HttpClient, not `"SmartLogApi"`). Corrected `ScanLogEntry.Timestamp` is `DateTimeOffset`. Removed inaccurate "SetupPage.xaml.cs is empty" claim. Total effort dropped from 3.5h → 2.5h. |
| 2026-04-28 | Claude (Opus 4.7) | Closed final open question per user "keep it simple" preference: no explicit `_heartbeat.StopAsync()` call from MainViewModel. `IAsyncDisposable` handles graceful shutdown via DI container; process termination handles abrupt close. Plan locked. |
