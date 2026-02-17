# US0016: Implement Background Sync Service

> **Status:** Done
> **Epic:** [EP0004: Offline Resilience and Sync](../epics/EP0004-offline-resilience-and-sync.md)
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-02-13
> **Completed:** 2026-02-13

## User Story

**As a** system
**I want** to automatically sync queued scans to the server when connectivity is restored
**So that** attendance data is submitted without manual intervention and no scans are lost

## Context

### Persona Reference
**Guard Gary** - School security guard, novice technical proficiency. Gary is unaware of the sync mechanics — he just sees the queue count decrease when the system recovers. He never needs to take any action.
[Full persona details](../personas.md#guard-gary)

### Background
When the SmartLog server is unreachable, validated scans are queued locally in SQLite (US0014). The background sync service monitors IHealthCheckService.IsOnline (US0015) and, when connectivity returns, submits PENDING queued scans in FIFO order via POST /api/v1/scans. It handles failures with exponential backoff and marks scans as FAILED after 10 attempts. The service runs on a background thread and must never block the UI.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | Availability | Zero data loss; auto-recovery from outages | Every PENDING scan must eventually be synced or marked FAILED |
| PRD | Scalability | Batch size 50 scans per cycle | Do not submit all pending scans at once |
| TRD | Architecture | Background sync must not block UI thread | Use async/await; marshal UI updates to main thread |
| PRD | Feature | Max 10 retry attempts before FAILED | Configurable via appsettings.json |

---

## Acceptance Criteria

### AC1: Sync triggers when IsOnline becomes true
- **Given** IHealthCheckService.IsOnline transitions from false to true
- **When** there are PENDING scans in the offline queue
- **Then** the background sync service begins submitting PENDING scans

### AC2: FIFO order submission
- **Given** 10 PENDING scans exist in the queue with different CreatedAt timestamps
- **When** sync begins
- **Then** scans are submitted in order of CreatedAt (oldest first)

### AC3: Batch size limit
- **Given** 120 PENDING scans exist in the queue
- **When** a sync cycle runs
- **Then** at most 50 scans are submitted in that cycle; remaining scans wait for the next cycle

### AC4: Successful sync marks scan as SYNCED
- **Given** a PENDING scan is submitted via POST /api/v1/scans
- **When** the server responds with 200 (ACCEPTED or DUPLICATE)
- **Then** the QueuedScan.SyncStatus is set to "SYNCED" and QueuedScan.ServerScanId is set to the scanId from the response

### AC5: Failed sync increments retry counter
- **Given** a PENDING scan is submitted and the server returns an error (non-200) or the request fails
- **When** the sync attempt fails
- **Then** QueuedScan.SyncAttempts is incremented by 1, QueuedScan.LastSyncError stores the error message, and the scan remains PENDING

### AC6: Exponential backoff between retries
- **Given** a scan has failed SyncAttempts times
- **When** the next sync cycle considers this scan
- **Then** the scan is skipped if insufficient time has elapsed since the last attempt (backoff = 2^SyncAttempts * 1 second, capped at 5 minutes)

### AC7: Scan marked FAILED after 10 attempts
- **Given** a PENDING scan has SyncAttempts = 9 (about to be attempt 10)
- **When** the 10th sync attempt fails
- **Then** QueuedScan.SyncStatus is set to "FAILED" and it is no longer retried

### AC8: App restart resumes sync
- **Given** the app was closed while 25 PENDING scans remained in the queue
- **When** the app restarts and IHealthCheckService.IsOnline is true
- **Then** the background sync service resumes submitting the 25 PENDING scans

### AC9: UI updates from background sync
- **Given** background sync is processing scans
- **When** a scan is synced or fails
- **Then** queue count in the footer updates via MainThread.InvokeOnMainThreadAsync()

### AC10: Sync does not block UI
- **Given** background sync is processing a batch of 50 scans
- **When** Guard Gary scans a new QR code
- **Then** the new scan is processed immediately (online submission or queue) with no perceptible delay

---

## Scope

### In Scope
- BackgroundSyncService observing IHealthCheckService.IsOnline
- FIFO batch submission of PENDING scans (configurable batch size, default 50)
- Per-scan success/failure handling with SyncStatus updates
- Exponential backoff (2^attempts * 1s, capped at 5 minutes)
- Max retry limit (configurable, default 10) → FAILED status
- App restart resilience (resume PENDING scans)
- Thread-safe database access (no concurrent writes conflicting with scan queue)
- Serilog logging of sync operations

### Out of Scope
- Manual trigger for sync ("Sync Now" button)
- FAILED scan recovery or re-queue mechanism
- Sync progress UI (percentage or progress bar)
- Priority-based sync (all PENDING treated equally)
- Sync across multiple server endpoints

---

## Technical Notes

**Service lifecycle:**
```csharp
public class BackgroundSyncService : IBackgroundSyncService, IDisposable
{
    public async Task StartAsync(CancellationToken ct)
    {
        // Subscribe to IHealthCheckService.IsOnline changes
        // When online: run sync cycle
        // When offline: pause
    }

    private async Task RunSyncCycleAsync(CancellationToken ct)
    {
        var pending = await _queueService.GetPendingAsync(batchSize: 50);
        foreach (var scan in pending)
        {
            if (ShouldSkipDueToBackoff(scan)) continue;
            var result = await SubmitScanAsync(scan);
            if (result.Success)
                await _queueService.MarkSyncedAsync(scan.Id, result.ServerScanId);
            else
                await _queueService.MarkFailedAttemptAsync(scan.Id, result.Error);
        }
    }
}
```

**Backoff calculation:**
```csharp
private bool ShouldSkipDueToBackoff(QueuedScan scan)
{
    var backoffSeconds = Math.Min(Math.Pow(2, scan.SyncAttempts), 300); // cap at 5 min
    var nextRetryTime = scan.LastAttemptAt.AddSeconds(backoffSeconds);
    return DateTime.UtcNow < nextRetryTime;
}
```

**Thread safety:** Use `SemaphoreSlim(1, 1)` to prevent concurrent sync cycles. Database writes serialized through IOfflineQueueService.

### API Contracts

Uses same POST /api/v1/scans as US0010:
```
POST /api/v1/scans
X-API-Key: {device-api-key}
{ "qrPayload": "...", "scannedAt": "ISO8601", "scanType": "ENTRY" }
```

### Data Requirements

**QueuedScan fields used:**

| Field | Read/Write | Purpose |
|-------|-----------|---------|
| SyncStatus | R/W | Filter PENDING; update to SYNCED or FAILED |
| SyncAttempts | R/W | Track retries; increment on failure |
| LastSyncError | W | Store error message on failure |
| ServerScanId | W | Store server-assigned ID on success |
| CreatedAt | R | FIFO ordering |
| QrPayload, ScannedAt, ScanType | R | Build POST request body |

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Server goes offline mid-batch (fails after 20 of 50 scans) | Stop batch; 20 scans marked SYNCED; remaining 30 stay PENDING for next cycle |
| Individual scan rejected by server (400 REJECTED) | Increment SyncAttempts; store error; scan may eventually reach FAILED status |
| Queue empty when sync starts | Sync cycle completes immediately; no requests sent |
| New scans added to queue during active sync cycle | New scans picked up in next sync cycle (not current batch) |
| Sync cycle overlaps with previous cycle | SemaphoreSlim prevents concurrent cycles; second cycle waits |
| App shutdown during sync (CancellationToken fired) | Current request completes or is cancelled; partial progress preserved |
| Very large queue (10,000+ pending) | Processed in batches of 50; many cycles needed; no memory issues |
| SyncAttempts exactly at 10 threshold | Scan marked FAILED; no 11th attempt |
| Exponential backoff overflow (2^10 = 1024 > 300 cap) | Capped at 300 seconds (5 minutes) |
| Server returns 429 during batch sync | Respect Retry-After; pause batch; resume after delay |
| Scan already SYNCED gets picked up again (race condition) | Check SyncStatus before submitting; skip if not PENDING |
| Server returns 200 DUPLICATE for queued scan | Treat as success; mark SYNCED with ServerScanId |

---

## Test Scenarios

- [ ] Sync starts when IHealthCheckService.IsOnline transitions false → true
- [ ] Sync does not start when IsOnline is false
- [ ] PENDING scans submitted in FIFO order (oldest CreatedAt first)
- [ ] Batch limited to 50 scans per cycle
- [ ] Successful sync: SyncStatus set to "SYNCED"
- [ ] Successful sync: ServerScanId stored from response
- [ ] Failed sync: SyncAttempts incremented by 1
- [ ] Failed sync: LastSyncError stores error message
- [ ] Scan with SyncAttempts = 10 marked as "FAILED"
- [ ] FAILED scans not retried in subsequent cycles
- [ ] Exponential backoff skips scans where insufficient time has elapsed
- [ ] Backoff capped at 300 seconds (5 minutes)
- [ ] App restart: PENDING scans from previous session are synced
- [ ] UI queue count updates during sync via MainThread dispatch
- [ ] Sync does not block UI thread (new scan during sync has no delay)
- [ ] SemaphoreSlim prevents concurrent sync cycles
- [ ] Server offline mid-batch: remaining scans stay PENDING
- [ ] Empty queue: sync cycle completes with no HTTP requests
- [ ] Server returns 429: sync pauses and respects Retry-After
- [ ] CancellationToken stops sync gracefully on app shutdown

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| [US0014](US0014-sqlite-offline-scan-queue.md) | Hard | IOfflineQueueService for queue CRUD (GetPendingAsync, MarkSyncedAsync, MarkFailedAttemptAsync) | Draft |
| [US0015](US0015-health-check-monitoring-service.md) | Hard | IHealthCheckService.IsOnline to trigger sync | Draft |
| [US0002](US0002-self-signed-tls-and-http-client-infrastructure.md) | Hard | HttpClient with TLS and Polly policies for POST /api/v1/scans | Draft |
| [US0001](US0001-secure-configuration-storage-service.md) | Hard | ISecureConfigService for API key in X-API-Key header | Draft |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| SmartLog Admin Web App | API | POST /api/v1/scans endpoint must accept queued scans |

---

## Estimation

**Story Points:** 8
**Complexity:** High

---

## Open Questions

- [ ] What is the exact exponential backoff formula? Using 2^attempts * 1s capped at 5 minutes — is this acceptable? - Owner: Architect
- [ ] Should FAILED scans have a manual re-queue mechanism in a future release? - Owner: Product
- [ ] Should the sync service log each individual scan submission, or only batch summaries? - Owner: Architect

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
