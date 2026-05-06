# PL0029: Skip Heartbeat POST When HealthCheck Reports Offline — Implementation Plan

> **Status:** Draft
> **Story:** [US0131: Skip Heartbeat POST When HealthCheck Reports Offline](../stories/US0131-skip-heartbeat-when-offline.md)
> **Epic:** [EP0018: Scanner Slim-down](../epics/EP0018-scanner-slim-down.md)
> **Created:** 2026-05-06
> **Language:** C# / xUnit + Moq

---

## Overview

Inject `IHealthCheckService` into `HeartbeatService`. Add a single early-return check at the top of `SendHeartbeatAsync`: if `_healthCheck.IsOnline == false`, log a debug line and return `false` without making an HTTP request. Returning `false` causes the existing `RunLoopAsync` to advance backoff via `ComputeNextInterval` — no other change needed. Add three xUnit tests covering `IsOnline ∈ { true, false, null }`.

**Departure from the story's implementation sketch:** the story sketched putting the check in `RunLoopAsync`. The plan puts it in `SendHeartbeatAsync` instead — same observable behavior, but reuses the existing `internal SendHeartbeatAsync` test seam (used by 8+ existing tests in `HeartbeatServiceTests`). Putting the check in `RunLoopAsync` would require driving the loop via `StartAsync/StopAsync` with timing-sensitive assertions, which is fragile. Story ACs are restated in plan terms below.

---

## Acceptance Criteria Summary

| AC | Name | Plan rephrasing |
|----|------|-----------------|
| AC1 | Constructor takes `IHealthCheckService` | Same |
| AC2 | POST skipped when `IsOnline == false` | `SendHeartbeatAsync` returns `false` without HTTP call when `_healthCheck.IsOnline == false`; debug log emitted |
| AC3 | POST proceeds when `IsOnline == true` | Same — no behavior change |
| AC4 | POST proceeds when `IsOnline == null` | Same — guard is `== false`, not `!= true` |
| AC5 | Backoff honored even when skipping | `RunLoopAsync` already advances backoff on `success == false`; achieved automatically by `SendHeartbeatAsync` returning `false` |
| AC6 | Resumption timely after `IsOnline → true` | Within one current interval; existing reset-on-success behavior unchanged |
| AC7 | Unit tests cover the new branch | 3 new tests in `HeartbeatServiceTests.cs` for the three `IsOnline` values |
| AC8 | Builds + tests pass on both TFMs | Same |

---

## Technical Context

### Language & Framework
- **Primary Language:** C# 12 / .NET 8
- **Test Framework:** xUnit + Moq
- **HTTP mocking:** `Mock<HttpMessageHandler>` (existing pattern in `HeartbeatServiceTests`)

### Existing Patterns

`HeartbeatService` constructor (current 8 dependencies):

```csharp
public HeartbeatService(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    IPreferencesService preferences,
    ISecureConfigService secureConfig,
    IOfflineQueueService offlineQueue,
    IScanHistoryService scanHistory,
    IQrScannerService usbScanner,
    ILogger<HeartbeatService> logger)
```

`SendHeartbeatAsync` is `internal async Task<bool>` (visible to test project via `InternalsVisibleTo` — confirmed by existing tests calling it directly). Returns `true` on success or harmless no-op (empty API key, empty server URL); `false` on failure.

`RunLoopAsync`:

```csharp
var success = await SendHeartbeatAsync(ct);
_currentIntervalSeconds = success
    ? _baseIntervalSeconds
    : ComputeNextInterval(_currentIntervalSeconds, false, _baseIntervalSeconds, _maxBackoffSeconds);
```

So returning `false` from `SendHeartbeatAsync` already advances the backoff exactly as the story's AC5 requires. **No `RunLoopAsync` change is needed.**

`MauiProgram.cs` already registers `IHealthCheckService` as a singleton (line ~290 area, per the US0015 implementation). Confirm during Phase 1 — no DI graph change expected.

### Existing test seam

`HeartbeatServiceTests` already constructs the service with mocks for every dependency and calls `SendHeartbeatAsync(...)` directly (e.g., `SendHeartbeatAsync_EmptyApiKey_ReturnsTrueWithoutPost`, `SendHeartbeatAsync_204Response_ReturnsTrue`). Adding a 9th mock and three more `[Fact]` tests fits cleanly.

The `Mock<HttpMessageHandler>` lets us assert "no SendAsync called" via `_mockHandler.Protected().Verify("SendAsync", Times.Never(), ...)`.

### Three-valued IsOnline semantics
- `true` — server reachable (or optimistic-online startup default)
- `false` — confirmed offline after stability window
- `null` — only at very early startup before initial poll

The skip check **must** be `IsOnline == false` (specific equality to `false`). Using `!= true` would skip on `null`, which wrongly suppresses the very first heartbeat at app launch.

---

## Recommended Approach

**Strategy:** Test-Together
**Rationale:** A new behavior branch is being introduced. Co-developing the implementation and unit tests prevents the implementation from drifting away from the spec while writing it. Three small Mock-based tests are quick to write alongside the change.

### Test Priority
1. `SendHeartbeatAsync` skips POST when `IsOnline == false` (AC2)
2. `SendHeartbeatAsync` posts when `IsOnline == true` (AC3 — regression net)
3. `SendHeartbeatAsync` posts when `IsOnline == null` (AC4 — startup case)

---

## Implementation Tasks

| # | Task | File | Depends On | Status |
|---|------|------|------------|--------|
| 1 | Verify `IHealthCheckService` is registered as singleton in `MauiProgram` | `MauiProgram.cs` | — | [ ] |
| 2 | Add `IHealthCheckService` constructor parameter + private field | `Core/Services/HeartbeatService.cs` | 1 | [ ] |
| 3 | Add early-return check at top of `SendHeartbeatAsync` | `Core/Services/HeartbeatService.cs` | 2 | [ ] |
| 4 | Update existing test setup: add `Mock<IHealthCheckService>` field; pass to all `new HeartbeatService(...)` calls | `Scanner.Tests/Services/HeartbeatServiceTests.cs` | 2 | [ ] |
| 5 | Default `Mock<IHealthCheckService>` returns `IsOnline = true` so existing tests keep passing | `Scanner.Tests/Services/HeartbeatServiceTests.cs` | 4 | [ ] |
| 6 | Add 3 new tests for `IsOnline ∈ { true, false, null }` | `Scanner.Tests/Services/HeartbeatServiceTests.cs` | 3, 5 | [ ] |
| 7 | Run `dotnet test`; confirm 3 new tests pass and existing 8+ unaffected | `Scanner.Tests` | 6 | [ ] |
| 8 | Both TFMs build clean | (n/a — verification) | 3 | [ ] |
| 9 | Manual: 5-min offline simulation; verify no POSTs in network log | (n/a — verification) | 7, 8 | [ ] |
| 10 | Manual: restore connectivity; verify POST resumes within base interval | (n/a — verification) | 9 | [ ] |

### Parallel Execution Groups

| Group | Tasks | Prerequisite |
|-------|-------|--------------|
| A | 1 (DI verification) | — |
| B | 2, 3 (HeartbeatService changes) | A |
| C | 4, 5 (test scaffolding) | B |
| D | 6 (new tests) | C |
| E | 7, 8 (verification) | D |
| F | 9, 10 (manual smoke) | E |

---

## Implementation Phases

### Phase 1: Verify DI registration

**File:** `SmartLog.Scanner/MauiProgram.cs`

Confirm a registration like:

```csharp
builder.Services.AddSingleton<IHealthCheckService, HealthCheckService>();
```

exists. (Per US0015 it should — `HealthCheckService` is a long-lived singleton so other services can read `IsOnline`.) If absent or scoped differently, **stop and surface** before continuing — `HeartbeatService` requires the same instance that's actively running its 15s poll, not a fresh one per-request.

**Expected:** registration is a singleton; no change needed.

---

### Phase 2: Modify `HeartbeatService`

**File:** `SmartLog.Scanner.Core/Services/HeartbeatService.cs`

#### 2a — Add field and constructor parameter

```csharp
private readonly IHttpClientFactory _httpClientFactory;
private readonly IConfiguration _config;
private readonly IPreferencesService _preferences;
private readonly ISecureConfigService _secureConfig;
private readonly IOfflineQueueService _offlineQueue;
private readonly IScanHistoryService _scanHistory;
private readonly IQrScannerService _usbScanner;
private readonly IHealthCheckService _healthCheck;     // ← new
private readonly ILogger<HeartbeatService> _logger;
```

```csharp
public HeartbeatService(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    IPreferencesService preferences,
    ISecureConfigService secureConfig,
    IOfflineQueueService offlineQueue,
    IScanHistoryService scanHistory,
    IQrScannerService usbScanner,
    IHealthCheckService healthCheck,                    // ← new
    ILogger<HeartbeatService> logger)
{
    _httpClientFactory = httpClientFactory;
    _config = config;
    _preferences = preferences;
    _secureConfig = secureConfig;
    _offlineQueue = offlineQueue;
    _scanHistory = scanHistory;
    _usbScanner = usbScanner;
    _healthCheck = healthCheck;                          // ← new
    _logger = logger;

    _usbScanner.ScanCompleted += OnUsbScan;              // unchanged
}
```

Order note: place `IHealthCheckService` between `IQrScannerService` and `ILogger<HeartbeatService>` so logger stays last (matches existing convention).

#### 2b — Add early-return check at top of `SendHeartbeatAsync`

```csharp
internal async Task<bool> SendHeartbeatAsync(CancellationToken ct)
{
    // EP0018/US0131: skip POST when HealthCheck has confirmed offline.
    // Use == false explicitly: null (early startup) and true both proceed normally.
    if (_healthCheck.IsOnline == false)
    {
        _logger.LogDebug("Heartbeat skipped — HealthCheck reports offline");
        return false;  // drive backoff exactly like a network failure would
    }

    var apiKey = await _secureConfig.GetApiKeyAsync();
    // ... existing body unchanged
}
```

**No change to `RunLoopAsync`.** Returning `false` already triggers `ComputeNextInterval(currentSeconds, false, ...)` per the existing logic at lines ~113–115. AC5 (backoff advances) and AC6 (resumption within one interval) are achieved automatically.

---

### Phase 3: Update existing tests

**File:** `SmartLog.Scanner.Tests/Services/HeartbeatServiceTests.cs`

#### 3a — Add field

```csharp
private readonly Mock<IHealthCheckService> _mockHealthCheck;
```

#### 3b — Initialize in constructor (default = online)

```csharp
public HeartbeatServiceTests()
{
    // ... existing mocks ...
    _mockHealthCheck = new Mock<IHealthCheckService>();
    _mockHealthCheck.SetupGet(h => h.IsOnline).Returns(true);  // default for existing tests
    // ... existing config/handler setup ...
}
```

#### 3c — Pass to every `new HeartbeatService(...)` call

Find every constructor invocation in the file (likely in a `CreateService()` helper or inlined in tests). Add `_mockHealthCheck.Object` in the new parameter position.

Example:

```csharp
// Before:
var service = new HeartbeatService(
    _mockFactory.Object, _config, _mockPreferences.Object,
    _mockSecureConfig.Object, _mockOfflineQueue.Object,
    _mockScanHistory.Object, _mockUsbScanner.Object,
    _mockLogger.Object);

// After:
var service = new HeartbeatService(
    _mockFactory.Object, _config, _mockPreferences.Object,
    _mockSecureConfig.Object, _mockOfflineQueue.Object,
    _mockScanHistory.Object, _mockUsbScanner.Object,
    _mockHealthCheck.Object,                              // ← new arg
    _mockLogger.Object);
```

If a `CreateService()` helper exists, the change is a single-site addition. Otherwise update each test (Moq does not infer constructor args).

---

### Phase 4: Add three new tests

**File:** same.

```csharp
// ── SendHeartbeatAsync — IsOnline branching (US0131) ──────────────────────

[Fact]
public async Task SendHeartbeatAsync_HealthCheckReportsOffline_SkipsPost()
{
    _mockHealthCheck.SetupGet(h => h.IsOnline).Returns(false);
    var service = CreateService();  // or inline as in existing tests

    var result = await service.SendHeartbeatAsync(CancellationToken.None);

    Assert.False(result);  // returns false to drive backoff
    _mockHandler.Protected().Verify(
        "SendAsync",
        Times.Never(),
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>());
}

[Fact]
public async Task SendHeartbeatAsync_HealthCheckReportsOnline_PostsNormally()
{
    _mockHealthCheck.SetupGet(h => h.IsOnline).Returns(true);
    SetupHandler204();  // existing helper
    var service = CreateService();

    var result = await service.SendHeartbeatAsync(CancellationToken.None);

    Assert.True(result);
    _mockHandler.Protected().Verify(
        "SendAsync",
        Times.Once(),
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>());
}

[Fact]
public async Task SendHeartbeatAsync_HealthCheckUnknown_PostsNormally()
{
    _mockHealthCheck.SetupGet(h => h.IsOnline).Returns((bool?)null);  // null = startup, unknown
    SetupHandler204();
    var service = CreateService();

    var result = await service.SendHeartbeatAsync(CancellationToken.None);

    Assert.True(result);
    _mockHandler.Protected().Verify(
        "SendAsync",
        Times.Once(),
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>());
}
```

If `SetupHandler204()` and `CreateService()` helpers don't already exist in the file, inline equivalents from the existing happy-path tests (e.g., `SendHeartbeatAsync_204Response_ReturnsTrue` shows the handler setup pattern).

---

### Phase 5: Verification

```bash
dotnet test SmartLog.Scanner.Tests -c Release --logger "console;verbosity=normal"
dotnet build SmartLog.Scanner -f net8.0-maccatalyst
dotnet build SmartLog.Scanner -f net8.0-windows10.0.19041.0
```

**Expected:** all green; 3 new tests show as `Passed`; no new warnings.

**Manual smoke:**

1. Start the app with the server reachable. Confirm first heartbeat fires within 60s (existing behavior; check Serilog logs).
2. Block the server (firewall rule or unplug LAN). Wait ~30s for `IsOnline` to flip to `false` (look for "Server connectivity lost" log line).
3. Watch network monitor / Serilog for the next 5 minutes. Expected: zero outbound POST attempts to `/api/v1/devices/heartbeat`. Debug log line "Heartbeat skipped — HealthCheck reports offline" appears every 60–300s.
4. Restore connectivity. Confirm:
   - HealthCheck flips back to `online` within ~30s (2 successful 15s polls).
   - Heartbeat POST resumes within one current-interval (could be up to 300s if backoff was at max; first successful POST resets to 60s).

---

## Edge Case Handling Plan

| # | Edge Case (from Story) | Handling Strategy | Phase |
|---|------------------------|-------------------|-------|
| 1 | `IsOnline == null` at first iteration | Guard is `== false`, so `null` proceeds normally; first heartbeat registers device | Phase 2b |
| 2 | 5-minute offline period | Zero POSTs; backoff advances each cycle; debug log emitted | Phase 2b + 5 (manual) |
| 3 | `IsOnline` flips during `Task.Delay` | Next iteration sees new value; at most one extra wait (acceptable) | n/a — natural |
| 4 | `HealthCheckService.StartAsync` never called | `IsOnline` defaults to `true` (per `HealthCheckService.cs:22` optimistic init); POST proceeds; no regression | n/a — pre-existing |
| 5 | HealthCheck stuck reporting `false` | Heartbeat skips indefinitely; UI also stuck offline (operator-visible). Root cause is HealthCheck; not our concern | n/a |

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `IHealthCheckService` is registered as transient or scoped instead of singleton | `HeartbeatService` reads a different instance's flag, always-true → behavior unchanged from today | Phase 1 verifies singleton registration before any code change |
| `SendHeartbeatAsync` is `private` not `internal` | Existing tests would not compile — but they do (8+ tests call it directly), so it's `internal`. Confirmed | Phase 3 fails fast if visibility differs |
| Existing tests break because they didn't set `IsOnline` | Tests fail with NullReferenceException or unexpected skip | Phase 3b sets default `IsOnline = true` in test ctor — all existing tests inherit online state |
| Three-valued semantics misread (e.g., using `!= true` instead of `== false`) | First heartbeat at app launch silently dropped → device never registers | Story AC4 + plan Phase 2b explicitly call out the equality form; AC4 unit test guards against regression |
| `Mock<IHealthCheckService>.SetupGet(h => h.IsOnline)` does not work because `IsOnline` is a property without setter on the interface | Mock setup compile error | Verified: `IHealthCheckService.IsOnline` is a get-only `bool?` property — Moq's `SetupGet` works for get-only properties on interfaces |
| Manual smoke timing varies by machine | Inconsistent verification | Manual is supplementary to unit tests; the unit tests are the source of truth |

---

## Definition of Done

- [ ] All 8 ACs implemented
- [ ] `HeartbeatService` constructor takes 9 dependencies including `IHealthCheckService`
- [ ] `SendHeartbeatAsync` returns `false` without HTTP call when `IsOnline == false`
- [ ] `SendHeartbeatAsync` proceeds normally when `IsOnline ∈ { true, null }`
- [ ] 3 new tests in `HeartbeatServiceTests` cover the three `IsOnline` values
- [ ] All existing `HeartbeatServiceTests` still pass (no regressions)
- [ ] `dotnet test SmartLog.Scanner.Tests` is green
- [ ] Both TFMs build clean with no new warnings
- [ ] Manual 5-minute offline test produces zero heartbeat POSTs; resumption within one base interval after reconnection

---

## Notes

- **Decision recap:** the story sketched the skip in `RunLoopAsync`. The plan moves it to `SendHeartbeatAsync` because:
  - Existing tests already drive `SendHeartbeatAsync` directly — the new tests fit the same shape.
  - Driving `RunLoopAsync` from a test would require `StartAsync` + `Task.Delay` + `StopAsync`, with timing-sensitive assertions about whether `SendAsync` was invoked. Fragile.
  - Observable behavior is identical: `RunLoopAsync` calls `SendHeartbeatAsync`, gets `false` when offline, runs the same `ComputeNextInterval` branch as a network failure.
  - The story's AC2 says "where it would call `SendHeartbeatAsync`" — this implementation honors the spirit (no POST when offline) while reusing the test seam.
- The skip log is at `Debug` level intentionally (story Open Question, proposed: Debug). Operator already has the offline UI indicator; an Information-level line every 60s is noise.
- No new `Preferences` keys, NuGet deps, or schema changes.
- This plan does **not** touch `BackgroundSyncService` even though it has similar online/offline interaction. That's working correctly (it already only flushes when HealthCheck reports online).
