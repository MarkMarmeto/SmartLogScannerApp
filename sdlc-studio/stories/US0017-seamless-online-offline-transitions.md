# US0017: Implement Seamless Online/Offline State Transitions

> **Status:** Done
> **Epic:** [EP0004: Offline Resilience and Sync](../epics/EP0004-offline-resilience-and-sync.md)
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-02-13
> **Completed:** 2026-02-13

## User Story

**As a** Guard Gary
**I want** the scanner to seamlessly switch between online and offline modes without any action on my part
**So that** I can keep scanning students without interruption regardless of network conditions

## Context

### Persona Reference
**Guard Gary** - School security guard, novice technical proficiency. Gary processes hundreds of students during peak times. He cannot afford to stop and troubleshoot when the network drops. The system must handle transitions transparently — he just keeps scanning.
[Full persona details](../personas.md#guard-gary)

### Background
This story integrates the scan submission pipeline (US0010), offline queue (US0014), health check monitoring (US0015), and background sync (US0016) into a seamless experience. When online, scans go directly to the server. When offline, scans are queued locally. When connectivity returns, queued scans sync automatically. The transition between these states requires zero user intervention. The feedback color changes from GREEN/AMBER/RED (online responses) to BLUE (queued offline), giving Gary a clear visual cue.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | Availability | Must function fully offline with zero data loss | Every validated scan either submitted or queued, no exceptions |
| PRD | UX | Seamless transition with no user action | Guard Gary sees color change but never needs to tap/click anything |
| TRD | Architecture | Graceful degradation on server failure | Mid-request failures fall back to queue transparently |

---

## Acceptance Criteria

### AC1: Online scan submission path
- **Given** IHealthCheckService.IsOnline is true
- **When** Guard Gary scans a valid QR code
- **Then** the scan is submitted directly to POST /api/v1/scans and the result is displayed with the appropriate color (GREEN, AMBER, or RED)

### AC2: Offline scan queuing path
- **Given** IHealthCheckService.IsOnline is false
- **When** Guard Gary scans a valid QR code
- **Then** the scan is queued via IOfflineQueueService.EnqueueAsync() and the display shows BLUE with "Scan queued (offline)"

### AC3: Mid-request failure falls back to queue
- **Given** IHealthCheckService.IsOnline is true but the network drops during a scan submission
- **When** the POST /api/v1/scans request fails (timeout, connection reset, etc.)
- **Then** the scan is automatically queued via IOfflineQueueService.EnqueueAsync() and the display shows BLUE with "Scan queued (offline)"

### AC4: Online→Offline transition
- **Given** IHealthCheckService.IsOnline transitions from true to false
- **When** Guard Gary continues scanning
- **Then** all subsequent scans are queued locally with BLUE feedback; no error dialog or interruption occurs

### AC5: Offline→Online transition triggers sync
- **Given** IHealthCheckService.IsOnline transitions from false to true and there are PENDING queued scans
- **When** the transition occurs
- **Then** the background sync service (US0016) begins submitting PENDING scans automatically; new scans go directly to the server

### AC6: Header status indicator updates
- **Given** IHealthCheckService.IsOnline changes state
- **When** Guard Gary looks at the MainPage header
- **Then** the indicator updates from green "Online" to red "Offline" (or vice versa) within one poll cycle (15 seconds)

### AC7: Queue count updates during transitions
- **Given** scans are being queued during offline mode
- **When** Guard Gary looks at the footer
- **Then** the "Queue: N pending" count increases with each queued scan and decreases as sync processes them after recovery

---

## Scope

### In Scope
- IScanProcessor (or equivalent orchestrator) that routes scans based on IsOnline state
- Fallback from online submission failure to offline queue
- Integration of IHealthCheckService, IScanApiService, IOfflineQueueService
- Feedback display coordination (online colors vs BLUE queued)
- Queue count and status indicator real-time updates during transitions
- Integration test scenarios covering full transition flows

### Out of Scope
- Manual online/offline toggle (always automatic)
- Selective sync (choosing which scans to sync)
- Multi-server failover
- Notification when going offline/online (beyond status indicator)

---

## Technical Notes

**Scan processing orchestration in MainViewModel:**
```csharp
private async Task ProcessValidatedScanAsync(ValidatedQrPayload payload)
{
    var scanRequest = new ScanRequest(payload.QrPayload, DateTime.UtcNow, CurrentScanType);

    if (_healthCheckService.IsOnline)
    {
        var result = await _scanApiService.SubmitScanAsync(scanRequest);
        if (result.IsSuccess)
        {
            DisplayResult(result.Value); // GREEN, AMBER, or RED
        }
        else
        {
            // Network failure during submission — fall back to queue
            await QueueScanOfflineAsync(scanRequest);
        }
    }
    else
    {
        await QueueScanOfflineAsync(scanRequest);
    }
}

private async Task QueueScanOfflineAsync(ScanRequest request)
{
    await _offlineQueueService.EnqueueAsync(request);
    DisplayQueuedFeedback(); // BLUE + "Scan queued (offline)"
}
```

The key design point is that IScanApiService.SubmitScanAsync returns a result type (not throwing exceptions), allowing the caller to distinguish between server responses and network failures.

### Data Requirements

No new data models. This story integrates existing services:
- IHealthCheckService.IsOnline (US0015)
- IScanApiService.SubmitScanAsync (US0010)
- IOfflineQueueService.EnqueueAsync (US0014)
- BackgroundSyncService (US0016)

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Network drops during scan submission (mid-request) | Request times out or throws; scan automatically queued; BLUE feedback shown |
| Rapid online/offline toggling (every few seconds) | Each scan routed based on IsOnline at the moment of processing; no crashes or lost scans |
| Transition during batch sync (goes offline while syncing) | Background sync stops; partially synced batch preserved; remaining scans stay PENDING |
| Multiple scans in flight when going offline | Any in-flight scan that fails gets queued; no scan is lost |
| Going offline with empty queue, then scanning | First offline scan creates the first PENDING entry; BLUE feedback displayed |
| Going online with 10,000+ pending scans | Background sync processes in batches of 50; new online scans go directly to server in parallel |
| Health check says online but scan submission fails | Scan falls back to queue via the mid-request failure path (AC3); IsOnline may update on next health check |
| Two scans processed simultaneously (one online, one offline due to state change) | Each scan independently routed; no race condition; both either submitted or queued |

---

## Test Scenarios

- [ ] Online mode: scan submitted directly to server and GREEN/AMBER/RED feedback shown
- [ ] Offline mode: scan queued locally and BLUE "Scan queued (offline)" feedback shown
- [ ] Network failure during online submission: scan falls back to queue with BLUE feedback
- [ ] Online→Offline transition: subsequent scans automatically queue without error dialog
- [ ] Offline→Online transition: new scans go to server; background sync starts for queued scans
- [ ] Status indicator updates from "Online" to "Offline" on transition
- [ ] Status indicator updates from "Offline" to "Online" on recovery
- [ ] Queue count increases during offline scanning
- [ ] Queue count decreases during background sync after recovery
- [ ] No scan is ever lost during state transitions (verified by queue + server counts)
- [ ] Mid-batch sync interrupted by offline transition preserves remaining scans as PENDING
- [ ] Rapid state toggling does not cause crashes or duplicate submissions
- [ ] Health check online but submission fails: graceful fallback to queue

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| [US0010](US0010-scan-submission-to-server-api.md) | Hard | IScanApiService.SubmitScanAsync with result type (not exceptions) | Draft |
| [US0014](US0014-sqlite-offline-scan-queue.md) | Hard | IOfflineQueueService.EnqueueAsync for offline queuing | Draft |
| [US0015](US0015-health-check-monitoring-service.md) | Hard | IHealthCheckService.IsOnline state and change events | Draft |
| [US0016](US0016-background-sync-service.md) | Hard | BackgroundSyncService for automatic PENDING scan submission | Draft |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| None beyond dependent stories | — | — |

---

## Estimation

**Story Points:** 5
**Complexity:** Medium

---

## Open Questions

- [ ] Should there be a brief delay before falling back to queue on submission failure (to handle transient blips), or should fallback be immediate? - Owner: Architect
- [ ] Should the system detect "false online" (health check passes but submissions fail) and auto-switch to offline mode? - Owner: Architect

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
