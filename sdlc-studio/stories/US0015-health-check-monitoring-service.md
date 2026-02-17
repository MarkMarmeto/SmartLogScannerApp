# US0015: Implement Health Check Monitoring Service

> **Status:** Done
> **Epic:** [EP0004: Offline Resilience and Sync](../epics/EP0004-offline-resilience-and-sync.md)
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-02-13
> **Completed:** 2026-02-13

## User Story

**As a** Guard Gary
**I want** to see whether the scanner is online or offline via a status indicator
**So that** I know if scans are being submitted live or queued locally

## Context

### Persona Reference
**Guard Gary** - School security guard, novice technical proficiency. Needs instant, unambiguous visual indicators. A green dot means the system is working normally; a red dot means scans are being saved locally.
[Full persona details](../personas.md#guard-gary)

### Background
The IHealthCheckService polls GET /api/v1/health (no authentication required) at regular intervals to determine server connectivity. The service exposes an IsOnline observable property that drives the UI status indicator (green dot + "Online" or red dot + "Offline") and controls whether scans are submitted directly or queued. This is a singleton service that starts polling after setup completion and runs for the lifetime of the app.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | Availability | Seamless transition with no user action | IsOnline changes must propagate automatically to scan pipeline |
| PRD | Feature | Poll GET /api/v1/health at 15-second intervals | Configurable via appsettings.json OfflineQueue.HealthCheckIntervalSeconds |
| TRD | Architecture | Background operations must not block UI thread | Use PeriodicTimer; marshal UI updates to main thread |
| TRD | Tech Stack | HttpClient from IHttpClientFactory with TLS config | Health check inherits self-signed cert acceptance |

---

## Acceptance Criteria

### AC1: Health check service registered as singleton
- **Given** the app starts and setup is completed (Setup.Completed = true)
- **When** the DI container resolves IHealthCheckService
- **Then** a single instance of HealthCheckService is returned and begins polling

### AC2: Polls GET /api/v1/health at 15-second intervals
- **Given** IHealthCheckService is running
- **When** 15 seconds have elapsed since the last poll
- **Then** a GET request is sent to {ServerBaseUrl}/api/v1/health (no X-API-Key header)

### AC3: Server responds 200 — IsOnline becomes true
- **Given** the health check poll is sent
- **When** the server responds with HTTP 200
- **Then** IHealthCheckService.IsOnline is set to true

### AC4: Server unreachable — IsOnline becomes false
- **Given** the health check poll is sent
- **When** the request fails (connection refused, timeout, DNS failure, or non-200 status)
- **Then** IHealthCheckService.IsOnline is set to false

### AC5: Online status indicator displayed in header
- **Given** IHealthCheckService.IsOnline is true
- **When** Guard Gary looks at the MainPage header bar
- **Then** a green dot and the text "Online" are displayed

### AC6: Offline status indicator displayed in header
- **Given** IHealthCheckService.IsOnline is false
- **When** Guard Gary looks at the MainPage header bar
- **Then** a red dot and the text "Offline" are displayed

### AC7: Initial state before first health check completes
- **Given** the app just launched and the first health check has not yet returned
- **When** Guard Gary sees the MainPage header
- **Then** the status shows a gray dot and "Connecting..." until the first poll completes

### AC8: Health check uses configured HttpClient with TLS settings
- **Given** the server uses a self-signed TLS certificate and AcceptSelfSignedCerts is true
- **When** the health check poll is sent
- **Then** the request succeeds (TLS accepted) because the HttpClient inherits the configured ServerCertificateCustomValidationCallback

### AC9: Polling starts only after setup completion
- **Given** Setup.Completed is false (app on SetupPage)
- **When** IHealthCheckService is resolved
- **Then** polling does NOT start until Setup.Completed becomes true and navigation reaches MainPage

---

## Scope

### In Scope
- IHealthCheckService interface with IsOnline property and StartAsync/StopAsync methods
- HealthCheckService implementation using PeriodicTimer (15s interval, configurable)
- GET /api/v1/health request (unauthenticated)
- IsOnline observable property with change notification
- Status indicator UI in MainPage header (green/red dot + text)
- Configurable poll interval via appsettings.json
- Logging of connectivity state changes via Serilog

### Out of Scope
- Authenticated health check (GET /api/v1/health/details) — that's used only in setup (US0005)
- Health check history or uptime tracking
- Network quality measurement (latency, bandwidth)
- Manual refresh button for health check

---

## Technical Notes

**Service pattern:**
```csharp
public class HealthCheckService : IHealthCheckService, IDisposable
{
    private PeriodicTimer? _timer;
    private bool _isOnline;
    public bool IsOnline { get => _isOnline; private set { /* notify */ } }

    public async Task StartAsync(CancellationToken ct)
    {
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(_interval));
        // Immediate first check
        await CheckHealthAsync();
        // Then periodic
        while (await _timer.WaitForNextTickAsync(ct))
            await CheckHealthAsync();
    }
}
```

Use `INotifyPropertyChanged` or an event to propagate IsOnline changes to MainViewModel. MainViewModel binds status indicator to IsOnline.

### API Contracts

**GET /api/v1/health**

Request:
```
GET /api/v1/health HTTP/1.1
Host: 192.168.1.100:8443
```
(No authentication header required)

Success Response (200):
```json
{ "status": "healthy" }
```

Any non-200 or connection failure = offline.

### Data Requirements

| Property | Type | Description |
|----------|------|-------------|
| IsOnline | bool | Current connectivity state |
| HealthCheckIntervalSeconds | int | Poll interval (default 15, from appsettings.json) |

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Server returns 200 but body is not JSON | Treated as online (status code is sufficient) |
| Server returns 503 Service Unavailable | IsOnline set to false |
| DNS resolution failure | IsOnline set to false; logged at Warning level |
| Timeout during health check (10s) | IsOnline set to false; does not block next poll cycle |
| Rapid online/offline flapping (alternating 200 and failure) | IsOnline updates on every poll; no debounce (immediate state change) |
| First check on app launch before server responds | Show "Connecting..." (gray dot) until first poll completes |
| Health check when HttpClient is disposed (app shutdown) | CancellationToken cancels the timer; no exception thrown |
| Network interface disabled (airplane mode, cable unplugged) | HttpRequestException caught; IsOnline set to false |
| Server returns HTTP 301 redirect | HttpClient follows redirect; if final response is 200, online |
| Concurrent health checks overlap (timer fires before previous completes) | Use SemaphoreSlim or lock to ensure only one check runs at a time |
| App goes to background on macOS | PeriodicTimer continues; may slow down based on OS power management |
| Poll interval changed in config after app start | Change takes effect on next app restart (not hot-reloaded) |

---

## Test Scenarios

- [ ] HealthCheckService registered as singleton in DI container
- [ ] StartAsync begins polling immediately (first check before first interval)
- [ ] Polls GET /api/v1/health at configured interval (default 15 seconds)
- [ ] Health check request has no X-API-Key header
- [ ] Server responds 200 → IsOnline is true
- [ ] Server responds 503 → IsOnline is false
- [ ] Connection refused → IsOnline is false
- [ ] Timeout → IsOnline is false
- [ ] DNS failure → IsOnline is false
- [ ] IsOnline changes from false to true when server recovers
- [ ] IsOnline changes from true to false when server goes down
- [ ] Status indicator shows green dot + "Online" when IsOnline is true
- [ ] Status indicator shows red dot + "Offline" when IsOnline is false
- [ ] Status indicator shows gray dot + "Connecting..." before first poll completes
- [ ] Polling does not start when Setup.Completed is false
- [ ] StopAsync cancels the PeriodicTimer cleanly
- [ ] Connectivity state change logged via Serilog (Information level)
- [ ] HttpClient inherits TLS configuration (self-signed cert acceptance)
- [ ] Concurrent polls are serialized (no overlapping requests)

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| [US0002](US0002-self-signed-tls-and-http-client-infrastructure.md) | Hard | Named HttpClient "SmartLogApi" with TLS configuration | Draft |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| SmartLog Admin Web App | API | Must expose GET /api/v1/health (unauthenticated) |

---

## Estimation

**Story Points:** 5
**Complexity:** Medium

---

## Open Questions

- [ ] Should health check implement debounce to avoid rapid state flapping (e.g., require 2 consecutive failures before going offline)? - Owner: Architect
- [ ] Should the health check interval be adjustable at runtime, or only via appsettings.json restart? - Owner: Product

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
