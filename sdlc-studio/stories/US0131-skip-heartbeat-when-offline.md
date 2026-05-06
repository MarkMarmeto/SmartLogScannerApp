# US0131: Skip Heartbeat POST When HealthCheck Reports Offline

> **Status:** Draft
> **Epic:** [EP0018: Scanner Slim-down](../epics/EP0018-scanner-slim-down.md)
> **Owner:** AI Assistant
> **Reviewer:** Mark Daniel Marmeto
> **Created:** 2026-05-06

## User Story

**As** the Scanner app
**I want** to skip the heartbeat HTTP POST when `HealthCheckService.IsOnline` is already known to be `false`
**So that** the app stops generating doomed-to-fail outbound traffic during known outages, reducing log noise and bandwidth use, while preserving heartbeat correctness when connectivity is intact or unknown

## Context

### Background

The Scanner app runs two complementary connectivity services:

- **`HealthCheckService`** (US0015): unauthenticated `GET /api/v1/health` every 15s with a stability window (2-in-a-row). Drives the `IsOnline` flag, online/offline UI indicator, and triggers `BackgroundSyncService` to flush the offline queue when connectivity is restored.
- **`HeartbeatService`** (US0120): authenticated `POST /api/v1/devices/heartbeat` every 60s (configurable, with exponential backoff up to 300s on failure). Sends device vitals (queue depth, last USB scan, scan history stats) so the admin dashboard can monitor the fleet.

Today the two services run independently. While the server is unreachable, `HealthCheckService.IsOnline` flips to `false` within ~30s (15s poll × 2-in-a-row stability window), but `HeartbeatService` keeps POSTing on its own schedule (60s + backoff), each POST guaranteed to fail. Each failed POST:

- Consumes a TCP connect attempt + DNS lookup + handshake timeout (the 10s `RequestTimeoutSeconds` default).
- Writes a warning log line.
- Advances the backoff timer.

This is wasteful. `HealthCheckService.IsOnline == false` is already an authoritative "don't bother" signal; using it lets `HeartbeatService` skip the doomed POST while still advancing its backoff state so it resumes at the right cadence when connectivity returns.

### Why this is *not* an over-optimization

The two services answer different questions and remain independent:

- HealthCheck answers "is the server reachable?" — fast (15s), unauthenticated, used to drive UI + queue flush.
- Heartbeat answers "what's this device's state, sent to the admin?" — slower, authenticated, payload-heavy.

The optimization is a one-way information flow: `HealthCheck → Heartbeat` ("don't even try if I already know it's down"). It does not merge the services or reduce coverage. If HealthCheck itself fails or returns `null` (initial startup before first poll), Heartbeat continues unchanged — protecting against the case where HealthCheck is the failing component.

### Important null-handling

`HealthCheckService.IsOnline` is `bool?`:
- `true` — server reachable (or optimistic-online at startup, see line 22 of `HealthCheckService.cs`)
- `false` — confirmed offline (after stability window)
- `null` — only at very early startup before initial state is established (transitional)

The skip check must use `IsOnline == false` specifically, **not** `!= true`. Using `!= true` would skip on `null`, which is wrong: at app launch the flag is `null` for a brief window and we still want the first heartbeat to attempt normally so the device registers with the admin server.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| EP0018 | Behavior | No payload or interval changes | Heartbeat schedule, backoff parameters, and request body unchanged |
| US0015 | Architecture | `IHealthCheckService.IsOnline` is `bool?` with semantics: `true=online`, `false=offline`, `null=unknown` | Skip check must be `IsOnline == false`, never `!= true` |
| US0120 | Architecture | Heartbeat uses `Task.Delay` loop with `ComputeNextInterval` for exponential backoff | Skipping the POST should still advance the backoff timer so resumption cadence is correct |
| TRD | Platform | Cross-platform | No platform-specific behavior; both TFMs must build clean |

---

## Acceptance Criteria

### AC1: HeartbeatService depends on IHealthCheckService

- **Given** `HeartbeatService.cs` constructor before this story takes 8 dependencies (httpClientFactory, config, preferences, secureConfig, offlineQueue, scanHistory, usbScanner, logger)
- **When** the story is complete
- **Then** the constructor takes 9 dependencies, with `IHealthCheckService healthCheck` added; the field is stored and not used for any purpose other than reading `IsOnline`

### AC2: POST is skipped when IsOnline == false

- **Given** `IHealthCheckService.IsOnline` is `false`
- **When** `HeartbeatService.RunLoopAsync` reaches the point where it would call `SendHeartbeatAsync`
- **Then** no HTTP POST is attempted; a single debug-level log line is emitted (e.g., `"Heartbeat skipped — HealthCheck reports offline"`); the next `Task.Delay` advances using the same backoff treatment as a failed POST (so resumption cadence matches existing behavior)

### AC3: POST proceeds when IsOnline == true

- **Given** `IHealthCheckService.IsOnline` is `true`
- **When** the heartbeat loop fires
- **Then** the POST is attempted exactly as today (no behavior change)

### AC4: POST proceeds when IsOnline == null

- **Given** `IHealthCheckService.IsOnline` is `null` (early startup, pre-stability)
- **When** the heartbeat loop fires
- **Then** the POST is attempted (the skip is conditional on `== false` only, not `!= true`)

### AC5: Backoff is honored even when skipping

- **Given** consecutive skip cycles while `IsOnline == false`
- **When** the loop runs N times
- **Then** the inter-iteration delay computed by `ComputeNextInterval(currentSeconds, success: false, ...)` is used after each skip — the backoff state advances as if the POST had failed, capped at `maxBackoffSeconds` (default 300s)

### AC6: Resumption is timely after IsOnline returns to true

- **Given** `IsOnline` flips from `false` back to `true`
- **When** the next heartbeat loop iteration occurs
- **Then** a POST is attempted within one `_currentIntervalSeconds` cycle of the flip; on a successful POST `_currentIntervalSeconds` resets to `_baseIntervalSeconds` (existing behavior, unchanged)

### AC7: Unit tests cover the new branch

- **Given** `HeartbeatServiceTests` exists
- **When** the story is complete
- **Then** the test suite contains:
  - A test asserting no `HttpClient.SendAsync` (or equivalent) call when `IHealthCheckService.IsOnline` returns `false`
  - A test asserting the call is made when `IsOnline` returns `true`
  - A test asserting the call is made when `IsOnline` returns `null`

### AC8: Builds and tests pass on both platforms

- **Given** the changes from AC1–AC7
- **When** `dotnet test SmartLog.Scanner.Tests` and both TFM builds are run
- **Then** all tests pass and both `net8.0-maccatalyst` and `net8.0-windows10.0.19041.0` builds succeed with no new warnings

---

## Scope

### In Scope
- Constructor injection of `IHealthCheckService` into `HeartbeatService`
- Pre-POST skip check in `HeartbeatService.RunLoopAsync`
- Backoff advancement when skipping (so resumption cadence is correct)
- A single debug-level log when skipping (not warning — the situation is expected)
- Unit tests covering the three `IsOnline` cases (`true` / `false` / `null`)

### Out of Scope
- Changing heartbeat payload, interval defaults, or backoff parameters
- Reactive subscription to `IHealthCheckService.ConnectivityChanged` event (we only need to read `IsOnline` at the top of each loop iteration — pulling is sufficient and avoids subscribe/unsubscribe lifecycle concerns)
- Suppressing log noise from `BackgroundSyncService` or other services (separate concern)
- Reducing HealthCheck poll frequency (not needed; HealthCheck stays at 15s)
- Merging the two services

---

## Technical Notes

### Implementation sketch

```csharp
// In HeartbeatService.cs, RunLoopAsync — before the existing try { SendHeartbeatAsync(...) } block:

private async Task RunLoopAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        try
        {
            bool success;
            if (_healthCheck.IsOnline == false)
            {
                _logger.LogDebug("Heartbeat skipped — HealthCheck reports offline");
                success = false;  // treat as failure for backoff purposes
            }
            else
            {
                success = await SendHeartbeatAsync(ct);
            }

            _currentIntervalSeconds = success
                ? _baseIntervalSeconds
                : ComputeNextInterval(_currentIntervalSeconds, false, _baseIntervalSeconds, _maxBackoffSeconds);
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in heartbeat loop");
            _currentIntervalSeconds = ComputeNextInterval(_currentIntervalSeconds, false, _baseIntervalSeconds, _maxBackoffSeconds);
        }

        try { await Task.Delay(TimeSpan.FromSeconds(_currentIntervalSeconds), ct); }
        catch (OperationCanceledException) { break; }
    }
}
```

### Files to change

| File | Change |
|------|--------|
| `SmartLog.Scanner.Core/Services/HeartbeatService.cs` | Add `IHealthCheckService` constructor dependency + field; add `IsOnline == false` check at top of `RunLoopAsync` body; on skip, treat as `success = false` for backoff and emit debug log |
| `SmartLog.Scanner.Tests/Services/HeartbeatServiceTests.cs` | Add three tests covering `IsOnline ∈ { true, false, null }` |
| (Verify) `SmartLog.Scanner/MauiProgram.cs` | No change — `IHealthCheckService` is already registered as a singleton |

### Why pull, not subscribe

`HeartbeatService.RunLoopAsync` already wakes once per interval. Reading `_healthCheck.IsOnline` at the top of each iteration is one property read — no subscription, no event handler lifecycle, no race conditions on `Start/StopAsync`. Subscribing to `ConnectivityChanged` would be over-engineering.

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|--------------------|
| `IsOnline == null` at first iteration (app just started) | POST proceeds normally; first heartbeat registers device with admin server |
| `IsOnline == false` during all of a 5-minute simulated outage | Zero POSTs; backoff advances each cycle; resumption within one interval after flip-back |
| `IsOnline` flips `false → true` mid-iteration (during the `Task.Delay`) | Next iteration sees `true` and POSTs. Acceptable — at most one extra wait of `_currentIntervalSeconds` before resumption |
| `HealthCheckService.StartAsync` was never called (test or unusual DI setup) | `IsOnline` returns its initial value (`true` per `HealthCheckService.cs:22` optimistic default) → POST proceeds; no regression vs today |
| `HealthCheckService` is itself broken and stuck reporting `false` | Heartbeat skips indefinitely. Acceptable — the root cause is HealthCheck, not heartbeat. Operator-visible because the offline UI indicator is also stuck on. |

---

## Test Scenarios

### Unit (xUnit + Moq, in `HeartbeatServiceTests`)

- [ ] `IHealthCheckService.IsOnline == false` → `SendHeartbeatAsync` not called; debug log emitted; `_currentIntervalSeconds` increased per backoff
- [ ] `IsOnline == true` → `SendHeartbeatAsync` called once
- [ ] `IsOnline == null` → `SendHeartbeatAsync` called once (first-launch case)
- [ ] Backoff sequence over 3 consecutive skips matches `ComputeNextInterval` math (60→120→240, capped at 300)
- [ ] Resumption: one skip then `IsOnline → true`, next iteration POSTs, on success `_currentIntervalSeconds` resets to 60

### Manual

- [ ] Start app with server reachable → first heartbeat fires within 60s (existing behavior)
- [ ] Block server (firewall / unplug LAN) → within ~30s `IsOnline` flips false → confirm zero heartbeat POSTs in network logs for next 5 minutes
- [ ] Restore server reachability → confirm heartbeat resumes within one base-interval (60s)
- [ ] Both TFM builds clean

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| [US0015](US0015-health-check-monitoring-service.md) | Predecessor | `IHealthCheckService.IsOnline` exists with three-valued semantics | Done |
| [US0120](US0120-heartbeat-service.md) | Predecessor | `HeartbeatService` and its `RunLoopAsync` exist | Done |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| None | — | — |

---

## Estimation

**Story Points:** 2
**Complexity:** Low — one constructor parameter, one `if` branch, three unit tests. Slightly above 1pt because of the unit test infrastructure for `HeartbeatService` (needs a `Mock<IHealthCheckService>` and verification that `SendAsync` is or isn't called — message-pump test patterns).

---

## Open Questions

- [ ] On skip, should we treat the cycle as `success = false` (advancing backoff) or `success = true` (keeping base interval)? **Proposed: `success = false`** — that way, when connectivity returns we have already paid down the interval and the next attempt fires in <= base interval. Treating as success would keep firing every base interval while offline, producing zero traffic but missing the natural backoff signal.
- [ ] Should the skip log be `Debug` or `Information`? **Proposed: Debug** — situation is expected and the operator already has the offline UI indicator. Information-level would be noisy.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-05-06 | AI Assistant | Initial draft from Scanner Slim-down review (EP0018). |
