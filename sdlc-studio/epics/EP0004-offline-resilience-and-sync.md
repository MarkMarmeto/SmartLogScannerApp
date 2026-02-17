# EP0004: Offline Resilience and Sync

> **Status:** Done
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-02-13
> **Completed:** 2026-02-13
> **Target Release:** 1.0.0

## Summary

Implement the offline resilience layer — SQLite-based local scan queue, automatic background sync when connectivity returns, and continuous server health monitoring. This epic ensures that Guard Gary never loses a scan, regardless of network conditions, and that IT Admin Ian can trust the system to self-recover without intervention.

## Inherited Constraints

> See PRD and TRD for full constraint details. Key constraints for this epic:

| Source | Type | Constraint | Impact |
|--------|------|------------|--------|
| PRD | Availability | Must function fully offline; zero data loss | Every validated scan must be either submitted or queued |
| PRD | Scalability | SQLite queue supports 10,000+ pending scans | EF Core must handle large datasets without degradation |
| TRD | Architecture | Background sync must not block UI thread | Use MainThread.InvokeOnMainThreadAsync() for UI updates |
| TRD | Tech Stack | SQLite via EF Core (Microsoft.EntityFrameworkCore.Sqlite) | EF Core migrations, FileSystem.AppDataDirectory for DB path |

---

## Business Context

### Problem Statement
School LANs are not always reliable. Network drops, server restarts, and infrastructure maintenance can all take the SmartLog server offline temporarily. During these outages, Gate Gary must continue scanning students without interruption. When connectivity returns, queued scans must sync automatically without any manual action from Gary or Ian.

**PRD Reference:** [Feature Inventory](../prd.md#3-feature-inventory)

### Value Proposition
No scan is ever lost. The system degrades gracefully — switching from live submission to local queuing seamlessly, and recovering just as seamlessly. IT Admin Ian never gets a call about "scans missing during the outage" because the system handles it transparently.

### Success Metrics

| Metric | Current | Target | Measurement |
|--------|---------|--------|-------------|
| Data loss during outages | N/A | 0 scans lost | Simulate outage → verify all scans sync after recovery |
| Auto-sync reliability | N/A | 100% of PENDING scans synced within 5 minutes of recovery | Monitor sync status after simulated outage |
| Health check accuracy | N/A | Online/Offline state matches actual server state within 15s | Compare indicator vs server availability |
| Queue capacity | N/A | 10,000+ scans without performance degradation | Load test SQLite queue with 10K+ entries |

---

## Scope

### In Scope
- QueuedScan SQLite entity with EF Core (Id, QrPayload, ScannedAt, ScanType, CreatedAt, SyncStatus, SyncAttempts, LastSyncError, ServerScanId)
- ScannerDbContext with migrations
- SQLite database at FileSystem.AppDataDirectory/scanner_queue.db
- IOfflineQueueService for queue CRUD operations
- Background sync service polling /api/v1/health every 15 seconds (configurable)
- FIFO sync of PENDING scans in batches of 50 (configurable)
- Sync success: SyncStatus = SYNCED, store ServerScanId
- Sync failure: increment SyncAttempts, store error, exponential backoff
- Max 10 retry attempts (configurable) before marking FAILED
- App restart: automatic resume of PENDING scan sync
- Health check status indicator: green dot + "Online" or red dot + "Offline"
- IHealthCheckService for connectivity state management
- Seamless online ↔ offline transition with no user action required
- UI updates from background threads via MainThread.InvokeOnMainThreadAsync()

### Out of Scope
- Manual queue management UI (view/edit/delete individual queued scans)
- Offline scan data export to file
- Multi-server failover
- Queue prioritization (all scans treated equally in FIFO order)
- FAILED scan recovery (requires manual review — documented limitation)

### Affected Personas
- **Guard Gary:** Sees blue "Scan queued (offline)" when offline; sees online/offline status indicator; unaware of sync mechanics
- **IT Admin Ian:** Trusts that auto-sync handles outages; may check pending queue count for diagnostics

---

## Acceptance Criteria (Epic Level)

- [ ] QueuedScan entity persisted in SQLite at FileSystem.AppDataDirectory/scanner_queue.db
- [ ] EF Core context (ScannerDbContext) with migrations for QueuedScan table
- [ ] When server unreachable, validated scans stored in SQLite queue with SyncStatus = PENDING
- [ ] Background service polls GET /api/v1/health every 15 seconds
- [ ] When healthy, PENDING scans submitted in FIFO order, batches of 50
- [ ] Successful sync: SyncStatus = SYNCED, ServerScanId stored
- [ ] Failed sync: SyncAttempts incremented, LastSyncError stored, exponential backoff
- [ ] After 10 failed attempts: SyncStatus = FAILED
- [ ] On app restart: automatically resume sync of PENDING scans
- [ ] Status indicator shows green "Online" or red "Offline" in header bar
- [ ] Connectivity state drives scan submission behavior (online → POST, offline → queue)
- [ ] Seamless transition: server goes down mid-operation → queue mode with no user action
- [ ] All background UI updates use MainThread.InvokeOnMainThreadAsync()
- [ ] Queue supports 10,000+ entries without degradation

---

## Dependencies

### Blocked By

| Dependency | Type | Status | Owner |
|------------|------|--------|-------|
| EP0001: Device Setup and Configuration | Epic | Draft | Unassigned |
| F13: Self-Signed TLS Support | Feature | Not Started | — |

### Blocking

| Item | Type | Impact |
|------|------|--------|
| EP0003: Scan Processing & Feedback | Epic | F05 Scan Submission falls back to offline queue on network error |

---

## Risks & Assumptions

### Assumptions
- EF Core SQLite provider is stable and performant on both macOS and Windows in MAUI context
- FileSystem.AppDataDirectory is writable and persists across app restarts on both platforms
- GET /api/v1/health requires no authentication (publicly accessible endpoint)
- Background service (IHostedService or timer-based) runs reliably in MAUI app lifecycle
- Server availability is binary — either responds to health check or doesn't

### Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| SQLite database locked during concurrent write from scan + sync | Medium | High | Use single ScannerDbContext with async locking; serialize writes |
| MAUI app lifecycle suspends background sync on macOS | Medium | Medium | Use PeriodicTimer; handle app resume events to restart sync |
| Health check gives false positive (server up but scans fail) | Low | Medium | Use scan submission failure as secondary offline indicator |
| Large queue (10K+) causes slow sync startup | Low | Medium | Index SyncStatus column; batch queries with pagination |

---

## Technical Considerations

### Architecture Impact
- Introduces ScannerDbContext as the EF Core database context (registered as scoped service)
- IOfflineQueueService: EnqueueAsync, GetPendingAsync, MarkSyncedAsync, MarkFailedAsync
- IHealthCheckService: exposes IsOnline observable property consumed by MainViewModel
- Background sync implemented as a timer-based service (not IHostedService, since MAUI doesn't use Generic Host by default)
- Connectivity state shared across services via IHealthCheckService singleton
- Database migrations run at app startup (EnsureCreated or Migrate)

### Integration Points
- GET /api/v1/health — unauthenticated health check for connectivity monitoring
- POST /api/v1/scans — batch sync of queued scans when online
- SQLite via EF Core — local persistence at FileSystem.AppDataDirectory
- MainViewModel — observes IsOnline and QueuePendingCount for UI binding
- EP0003 (F05 Scan Submission) — calls IOfflineQueueService.EnqueueAsync on network failure

---

## Sizing

**Story Points:** 23
**Estimated Story Count:** 4

**Complexity Factors:**
- EF Core + SQLite setup with migrations in MAUI context
- Background sync timing, batching, and exponential backoff logic
- Thread-safe database access (scan writes + sync reads/writes)
- Seamless online/offline state transitions
- App lifecycle handling (startup sync resume, background timer management)

---

## Story Breakdown

- [ ] [US0014: Implement SQLite Offline Scan Queue](../stories/US0014-sqlite-offline-scan-queue.md)
- [ ] [US0015: Implement Health Check Monitoring Service](../stories/US0015-health-check-monitoring-service.md)
- [ ] [US0016: Implement Background Sync Service](../stories/US0016-background-sync-service.md)
- [ ] [US0017: Implement Seamless Online/Offline State Transitions](../stories/US0017-seamless-online-offline-transitions.md)

---

## Test Plan

> Test spec to be generated via `/sdlc-studio test-spec --epic EP0004`

---

## Open Questions

- [ ] Is there a maximum offline queue duration before scans should be considered stale? - Owner: Product
- [ ] Should FAILED scans be retried manually, or is there an admin action to requeue them? - Owner: Product
- [ ] What is the exact exponential backoff formula? (e.g., 2^attempt * base_delay) - Owner: Architect

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial epic created from PRD features F07, F08, F09 |
