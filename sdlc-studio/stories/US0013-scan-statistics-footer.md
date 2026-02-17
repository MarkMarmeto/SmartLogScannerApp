# US0013: Implement Scan Statistics Footer

> **Status:** Draft
> **Epic:** [EP0003: Scan Processing and Feedback](../epics/EP0003-scan-processing-and-feedback.md)
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-02-13

## User Story

**As a** Guard Gary
**I want** to see today's total scan count and pending queue size in a footer bar
**So that** I have a sense of progress through the shift and can see at a glance whether scans are queuing up

## Context

### Persona Reference
**Guard Gary** - School security guard, novice technical proficiency. Processes hundreds of students during peak times. Needs simple, always-visible status information without having to navigate or tap anything.
[Full persona details](../personas.md#guard-gary)

### Background
During a shift, Guard Gary processes hundreds of scans. A simple footer showing "Queue: N pending | Today: N scans" gives him confidence the system is working and a sense of progress. The queue count also serves as an indirect indicator of offline status — if the number keeps growing, something may be wrong. Both counters update in real-time as scans are submitted or synced.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | UX | Footer always visible on MainPage | Footer must not scroll off-screen; fixed position |
| TRD | Architecture | Background sync updates use MainThread.InvokeOnMainThreadAsync() | Counter updates from background threads must marshal to UI thread |
| PRD | Feature | Queue count updated in real-time | Must observe IOfflineQueueService changes reactively |

---

## Acceptance Criteria

### AC1: Footer bar displays queue count and today's scan count
- **Given** the MainPage is displayed after setup completion
- **When** Guard Gary looks at the bottom of the screen
- **Then** a footer bar is visible showing "Queue: 0 pending | Today: 0 scans" (initial state)

### AC2: Today's count increments on successful online scan
- **Given** Guard Gary is on MainPage and today's count shows "Today: 5 scans"
- **When** a scan is submitted online and the server responds with ACCEPTED (200)
- **Then** the footer updates to "Today: 6 scans"

### AC3: Today's count increments on queued scan
- **Given** Guard Gary is on MainPage and today's count shows "Today: 5 scans"
- **When** a scan is queued offline (server unreachable)
- **Then** the footer updates to "Today: 6 scans"

### AC4: Queue count increments when scan is queued
- **Given** the footer shows "Queue: 2 pending"
- **When** a new scan is queued offline
- **Then** the footer updates to "Queue: 3 pending"

### AC5: Queue count decrements when scan is synced
- **Given** the footer shows "Queue: 5 pending" and background sync is running
- **When** one queued scan is successfully synced to the server
- **Then** the footer updates to "Queue: 4 pending"

### AC6: Today's count resets on new day
- **Given** the app has been running since yesterday with "Today: 150 scans"
- **When** the first scan occurs after midnight (or the app is restarted after midnight)
- **Then** the today's count resets to "Today: 1 scans"

### AC7: Footer always visible
- **Given** MainPage is displayed
- **When** the scan area or result display is showing any state (IDLE, ACCEPTED, REJECTED, QUEUED)
- **Then** the footer bar remains fixed at the bottom of the page, never obscured or scrolled away

---

## Scope

### In Scope
- Footer bar UI component on MainPage (fixed bottom position)
- "Queue: N pending" counter bound to IOfflineQueueService.GetPendingCountAsync()
- "Today: N scans" counter tracking scans since midnight
- Real-time updates via observable properties on MainViewModel
- MainThread.InvokeOnMainThreadAsync() for background thread updates
- Today's count persisted to Preferences for app restart resilience (optional) or recalculated from queue

### Out of Scope
- Scan history list or detailed log viewer
- Statistics for previous days
- Export or reporting of scan statistics
- Chart or graph visualizations

---

## Technical Notes

**MainViewModel additions:**
```csharp
[ObservableProperty] private int _queuePendingCount;
[ObservableProperty] private int _todayScanCount;
```

Subscribe to IOfflineQueueService queue change events (or poll on scan/sync events) and update QueuePendingCount. Increment TodayScanCount on each successful scan submission and each offline queue addition.

For midnight rollover, store the date of the last scan in Preferences ("Scanner.LastScanDate"). On each scan, compare to current date — if different, reset counter.

### Data Requirements

| Property | Source | Update Trigger |
|----------|--------|----------------|
| QueuePendingCount | IOfflineQueueService.GetPendingCountAsync() | After queue add, after sync complete |
| TodayScanCount | In-memory counter + Preferences | After each scan (online or queued) |
| LastScanDate | Preferences ("Scanner.LastScanDate") | After each scan |

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Very high counts (10,000+) | Display full number (e.g., "Queue: 10532 pending"); no truncation |
| Queue count reaches zero after sync | Display "Queue: 0 pending" (not hidden) |
| Midnight rollover while app is running | Next scan resets today's count to 1; compare current date vs LastScanDate |
| App restart after midnight | Today's count starts at 0 (or recalculated from today's queue entries) |
| Background sync updates queue count from background thread | Use MainThread.InvokeOnMainThreadAsync() to update QueuePendingCount |
| Rapid scans (multiple per second) | Each scan increments counter; no debounce on counter updates |
| GetPendingCountAsync() throws database error | Log error; display last known count; do not crash |

---

## Test Scenarios

- [ ] Footer bar is visible on MainPage after setup completion
- [ ] Footer displays "Queue: 0 pending | Today: 0 scans" on fresh start
- [ ] Today's count increments by 1 after successful online scan (ACCEPTED)
- [ ] Today's count increments by 1 after scan queued offline
- [ ] Today's count does NOT increment on DUPLICATE response
- [ ] Today's count does NOT increment on REJECTED response
- [ ] Queue count increments by 1 when scan is queued
- [ ] Queue count decrements by 1 when queued scan is synced
- [ ] Queue count shows 0 when all scans are synced
- [ ] Today's count resets to 0 after midnight (new date detected)
- [ ] Footer remains visible when scan result is displayed
- [ ] Counter updates from background sync use MainThread dispatch
- [ ] GetPendingCountAsync failure is handled gracefully (no crash)

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| [US0014](US0014-sqlite-offline-scan-queue.md) | Service | IOfflineQueueService.GetPendingCountAsync() for queue count | Draft |
| [US0010](US0010-scan-submission-to-server-api.md) | Service | IScanApiService scan result events for today's count | Draft |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| None | — | — |

---

## Estimation

**Story Points:** 3
**Complexity:** Low

---

## Open Questions

- [ ] Should today's count include DUPLICATE and REJECTED scans, or only ACCEPTED and QUEUED? - Owner: Product
- [ ] Should today's count survive app restart (persisted) or reset to 0 on each launch? - Owner: Product

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
