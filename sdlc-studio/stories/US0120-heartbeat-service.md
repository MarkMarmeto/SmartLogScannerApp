# US0120: Implement Heartbeat Service

> **Status:** Draft
> **Epic:** EP0005: Scanner Integration (cross-project — epic tracked in WebApp)
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-04-28

## User Story

**As a** Tech-Savvy Tony (Super Admin)
**I want** each scanner app to periodically push a heartbeat to the server
**So that** I can see real-time Online/Stale/Offline health status for every gate scanner in the admin UI without waiting for a scan event to prove the device is alive

## Context

### Persona Reference
**Tech-Savvy Tony** - IT Administrator responsible for keeping gate scanners operational.
[Full persona details](../personas.md)

### Background
US0119 (WebApp) shipped the server-side heartbeat endpoint (`POST /api/v1/devices/heartbeat`) and the admin UI health dashboard. The endpoint sits idle until each scanner sends heartbeats. This story delivers the scanner-side companion: `IHeartbeatService`, a singleton background service that fires every 60 seconds, sends a JSON snapshot to the server, and backs off exponentially (up to 5 min) when the server is unreachable. It is distinct from the existing `IHealthCheckService` — health check *pulls* (GET /api/v1/health) to detect offline mode; heartbeat *pushes* to report scanner vitals to Tony.

### Relationship to HealthCheckService (US0015)
`IHealthCheckService` remains unchanged. The two services run in parallel:

| | HealthCheckService | HeartbeatService |
|---|---|---|
| Direction | Pull (GET) | Push (POST) |
| Auth | None | X-API-Key |
| Interval | 15 s | 60 s (backoff up to 5 min) |
| Purpose | Drive online/offline UI indicator | Report scanner vitals to admin |
| Queued on failure? | N/A | No — best-effort |

---

## Inherited Constraints

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | Security | All device API calls must include X-API-Key header | Heartbeat uses same key stored by SecureConfigService |
| PRD | Availability | Background operations must not block UI thread | HeartbeatService runs on background Task |
| TRD | Tech Stack | HttpClient from IHttpClientFactory with TLS config | Heartbeat inherits self-signed cert acceptance via AcceptSelfSignedCerts preference |
| US0119 AC3 | Feature | Scanner sends heartbeat every 60 s; retries with exponential backoff (max 5 min) | Interval doubles on failure; resets on success |
| US0119 AC3 | Feature | Heartbeats are best-effort — not queued for replay | Failure is logged only; no OfflineQueue involvement |

---

## Acceptance Criteria

### AC1: HeartbeatService registered as singleton
- **Given** the app starts and setup is completed (Setup.Completed = true)
- **When** the DI container resolves IHeartbeatService
- **Then** a single instance begins sending heartbeats after setup completes

### AC2: Sends POST /api/v1/devices/heartbeat every 60 seconds
- **Given** IHeartbeatService is running and the previous heartbeat succeeded
- **When** 60 seconds have elapsed since the last attempt
- **Then** a POST request is sent to `{ServerBaseUrl}/api/v1/devices/heartbeat` with `X-API-Key` header and JSON body:
  ```json
  {
    "appVersion": "1.4.2",
    "osVersion": "Windows 11 (10.0.22621)",
    "batteryPercent": null,
    "isCharging": null,
    "networkType": "ETHERNET",
    "lastScanAt": "2026-04-28T08:32:11Z",
    "queuedScansCount": 3,
    "clientTimestamp": "2026-04-28T08:32:45Z"
  }
  ```

### AC3: Server responds 204 — interval resets to 60 s
- **Given** a heartbeat POST is sent
- **When** the server responds with HTTP 204 No Content
- **Then** the next heartbeat is scheduled 60 seconds later (base interval)
- **And** any active backoff is cleared

### AC4: Server unreachable — exponential backoff up to 5 minutes
- **Given** a heartbeat POST is sent
- **When** the request fails (connection refused, timeout, non-2xx response)
- **Then** the next heartbeat is delayed by doubling the previous interval: 60 s → 120 s → 240 s → 300 s (capped)
- **And** the failure is logged at Warning level
- **And** no entry is written to the offline scan queue

### AC5: Payload fields populated from device APIs
- **Given** the heartbeat is being composed
- **Then** the payload contains:
  - `appVersion` — `AppInfo.VersionString` (MAUI)
  - `osVersion` — `DeviceInfo.VersionString` (MAUI)
  - `batteryPercent` — `Battery.Default.ChargeLevel * 100` cast to int; `null` if `BatteryState.Unknown` or not available
  - `isCharging` — `true` when `Battery.Default.State == BatteryState.Charging`; `null` if unavailable
  - `networkType` — derived from `Connectivity.NetworkAccess` + profiles: `"WIFI"`, `"ETHERNET"`, `"CELLULAR"`, `"OFFLINE"`
  - `lastScanAt` — UTC timestamp of the most recent accepted scan from `IScanStatisticsService` (or `null` if none today)
  - `queuedScansCount` — current count from `IOfflineQueueService.GetQueueCountAsync()`
  - `clientTimestamp` — `DateTime.UtcNow` at point of send

### AC6: Uses configured HttpClient with TLS settings
- **Given** `AcceptSelfSignedCerts` is true in setup config
- **When** the heartbeat POST is sent
- **Then** the request succeeds because the HttpClient accepts the self-signed certificate (same pattern as HealthCheckService)

### AC7: Polling starts only after setup completion
- **Given** Setup.Completed is false (app on SetupPage)
- **When** IHeartbeatService is resolved
- **Then** no heartbeat is sent until Setup.Completed becomes true and navigation reaches MainPage (same lifecycle gate as HealthCheckService)

### AC8: App shutdown cancels heartbeat loop cleanly
- **Given** the app is shutting down
- **When** StopAsync is called with a CancellationToken
- **Then** any in-progress Task.Delay or pending POST is cancelled without throwing unhandled exceptions

---

## Scope

### In Scope
- `IHeartbeatService` interface with `StartAsync` / `StopAsync`
- `HeartbeatService` implementation with variable-interval loop (60 s base, exponential backoff, 300 s cap)
- Payload composition from MAUI device APIs, `IOfflineQueueService`, and scan statistics
- X-API-Key authentication using `SecureConfigService`
- TLS handling via `IPreferencesService.GetAcceptSelfSignedCerts()` (same approach as HealthCheckService)
- Configurable base interval via `appsettings.json` key `Heartbeat:BaseIntervalSeconds` (default 60, clamped 30..600)
- Configurable max backoff via `appsettings.json` key `Heartbeat:MaxBackoffSeconds` (default 300, clamped 60..3600)
- Serilog logging of send attempts, successes, failures, and backoff state

### Out of Scope
- Queuing heartbeats for offline replay (best-effort only per US0119 AC10)
- Displaying heartbeat status in the scanner UI (Tony sees it in the WebApp, not in the scanner)
- Battery / network display in the scanner UI
- Per-field null handling for platforms where MAUI APIs are unavailable (null is the correct sentinel)

---

## Technical Notes

### Service pattern

Use a manual loop with `Task.Delay` (not `PeriodicTimer`) to support variable backoff intervals cleanly:

```csharp
public class HeartbeatService : IHeartbeatService, IAsyncDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private int _intervalSeconds = BaseInterval; // reset on success, double on failure

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await SendHeartbeatAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), ct);
        }
    }
}
```

On success: `_intervalSeconds = BaseInterval`.
On failure: `_intervalSeconds = Math.Min(_intervalSeconds * 2, MaxBackoff)`.

### API key retrieval

```csharp
var apiKey = await _secureConfig.GetApiKeyAsync();
if (string.IsNullOrEmpty(apiKey)) { /* skip — setup incomplete */ return; }
```

### Network type mapping

```csharp
private static string GetNetworkType()
{
    var access = Connectivity.NetworkAccess;
    if (access == NetworkAccess.None) return "OFFLINE";
    var profiles = Connectivity.ConnectionProfiles;
    if (profiles.Contains(ConnectionProfile.WiFi)) return "WIFI";
    if (profiles.Contains(ConnectionProfile.Ethernet)) return "ETHERNET";
    if (profiles.Contains(ConnectionProfile.Cellular)) return "CELLULAR";
    return "OFFLINE";
}
```

### LastScanAt

Pull from `ScanStatisticsService` (or whichever service tracks today's scan count and last scan time). If no accepted scan exists today, send `null`.

### API Contracts

**POST /api/v1/devices/heartbeat**

```
POST /api/v1/devices/heartbeat HTTP/1.1
Host: 192.168.10.1:8443
X-API-Key: sk_live_xxx
Content-Type: application/json

{
  "appVersion": "1.4.2",
  "osVersion": "Windows 11 (10.0.22621)",
  "batteryPercent": null,
  "isCharging": null,
  "networkType": "ETHERNET",
  "lastScanAt": "2026-04-28T08:32:11Z",
  "queuedScansCount": 0,
  "clientTimestamp": "2026-04-28T08:32:45Z"
}
```

Success: `204 No Content`
Auth failure: `401 Unauthorized` — log at Warning, back off (device may be revoked)

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| API key not yet configured (setup incomplete) | Skip heartbeat silently; StartAsync not called until after setup |
| Server returns 401 (revoked device) | Log Warning; apply backoff; do not crash the loop |
| Server returns 503 or 5xx | Treat as failure; apply backoff |
| MAUI Battery API throws (desktop platform) | Catch, send `null` for battery fields |
| MAUI Connectivity API throws | Catch, send `"OFFLINE"` as fallback |
| OfflineQueueService throws getting count | Catch, send `null` for queuedScansCount |
| Heartbeat request timeout (10 s) | OperationCanceledException caught; treated as failure; backoff applied |
| App backgrounded (macOS power management) | Task.Delay continues; OS may throttle; acceptable |
| StartAsync called twice | Guard: if `_cts != null`, log warning and return without starting second loop |

---

## Test Scenarios

- [ ] HeartbeatService registered as singleton in DI container
- [ ] StartAsync begins loop; first heartbeat fires immediately (before first delay)
- [ ] On 204 response, interval resets to base (60 s)
- [ ] On connection failure, interval doubles; caps at MaxBackoff (300 s)
- [ ] On 401 response, backoff applies; loop continues (no crash)
- [ ] Payload `networkType` correctly maps WIFI / ETHERNET / OFFLINE from ConnectionProfile
- [ ] Payload `batteryPercent` is null when BatteryState is Unknown
- [ ] Payload `queuedScansCount` matches IOfflineQueueService.GetQueuedCountAsync()
- [ ] Payload includes `X-API-Key` header
- [ ] Heartbeat does not start when API key is empty (setup incomplete)
- [ ] StopAsync cancels loop cleanly; no OperationCanceledException propagated to caller
- [ ] StartAsync called twice: second call is a no-op (logged warning)
- [ ] HttpClient respects AcceptSelfSignedCerts (TLS accepted when flag is true)

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| [US0001](US0001-secure-configuration-storage-service.md) | Hard | `SecureConfigService.GetApiKeyAsync()` for X-API-Key header | Done |
| [US0002](US0002-self-signed-tls-and-http-client-infrastructure.md) | Hard | Named HttpClient with TLS configuration | Done |
| [US0015](US0015-health-check-monitoring-service.md) | Reference | Lifecycle pattern (start after setup, singleton) | Done |
| [US0119](../../SmartLogWebApp/sdlc-studio/stories/US0119-scanner-health-monitoring.md) | Hard | `POST /api/v1/devices/heartbeat` endpoint on server | Done (WebApp) |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| SmartLog Admin Web App | API | Done — endpoint live in WebApp PL0039 |

---

## Estimation

**Story Points:** 3
**Complexity:** Low-Medium

The endpoint contract and TLS/auth patterns are already established by HealthCheckService and ScansApiController. The main new work is the variable-interval loop, MAUI device API calls, and payload composition.

---

## Open Questions

> Both resolved during plan review (PL0018, 2026-04-28):
> - **Heartbeat interval surface:** appsettings.json only (no Setup UI). Restart-required.
> - **Desktop battery null:** acceptable. WebApp UI already renders `—` for null fields.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-28 | Claude (Sonnet 4.6) | Initial story created as companion to WebApp US0119 / PL0039 |
