# PL0012: Error Isolation and Auto-Recovery — Implementation Plan

> **Status:** Complete
> **Story:** [US0070: Error Isolation and Auto-Recovery](../stories/US0070-error-isolation-and-recovery.md)
> **Epic:** EP0011: Multi-Camera Scanning
> **Created:** 2026-04-18
> **Language:** C# / .NET MAUI 8

## Overview

Per-camera error isolation with automatic recovery (3 × 10 s), race-condition-safe via `Dictionary<int, CancellationTokenSource>`. Manual restart via `RestartCommand` on each status card. Frame rate display updated by 1-s timer. All phases complete.

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | Error Isolation | Exception in camera N → only N enters Error; others unaffected |
| AC2 | Auto-Recovery | 3 attempts × 10 s delay before giving up |
| AC3 | Offline After 3 Failures | `CameraStatus.Offline` + `CameraStatusChanged` fired |
| AC4 | Manual Restart | `RestartCameraAsync` resets `ReconnectAttempts`; fresh start attempt |
| AC5 | Race Condition Guard | Previous `CancellationTokenSource` cancelled before new recovery loop |
| AC6 | Frame Rate Display | "N fps" while Scanning; "—" otherwise |

---

## Implementation Phases

### Phase 1: HandleCameraErrorAsync ✅
- [x] Sets `cam.Status = CameraStatus.Error`, `cam.ErrorMessage`
- [x] Fires `CameraStatusChanged`
- [x] Calls `TriggerAutoRecovery` if `cam.IsEnabled`

### Phase 2: TriggerAutoRecovery ✅
- [x] `CancelRecovery(index)` cancels existing `CancellationTokenSource`
- [x] New `CancellationTokenSource` stored in `_recoveryCts[index]`
- [x] `Task.Run` loop: wait 10 s → `StartCameraInternalAsync` → check status → repeat up to 3 times
- [x] After 3 failures: `cam.Status = CameraStatus.Offline`; fires `CameraStatusChanged`

### Phase 3: ICameraWorker.ErrorOccurred Wiring ✅
- [x] `MultiCameraManager.InitializeAsync`: `worker.ErrorOccurred += (_, error) => _ = HandleCameraErrorAsync(cam.Index, error)`

### Phase 4: CameraSlotState Status Display ✅
- [x] `CanRestart` computed: `Status is Error or Offline`
- [x] `StatusBrush` computed: green/red/grey/orange/light-grey by status
- [x] `StatusText` computed: ● Scanning / ⚠ Error:msg / ⊘ Offline / ? No Signal / ○ Idle
- [x] `OnStatusChanged` partial notifies `CanRestart`, `StatusBrush`, `StatusText`
- [x] `RestartCommand` with `Func<int, Task>` callback → `MultiCameraManager.RestartCameraAsync`

### Phase 5: Frame Rate Display ✅
- [x] `CameraSlotState.IncrementFrameCount()` — `Interlocked.Increment`
- [x] `CameraSlotState.UpdateFrameRate()` — `Interlocked.Exchange` to read+reset counter; sets `FrameRateDisplay`
- [x] `MainViewModel._frameRateTimer` — 1-s `DispatcherTimer`; started in `InitializeAsync`; calls `UpdateFrameRate()` on all slots

---

## Edge Case Handling

| # | Edge Case | Handling Strategy | Phase |
|---|-----------|-------------------|-------|
| 1 | Manual stop during recovery | `CancelRecovery` + `IsEnabled=false` stops loop | Phase 2 |
| 2 | All cameras fail | Each has independent recovery loop | Phase 2 |
| 3 | Restart on already-scanning camera | `worker.IsRunning` guard; no-op | Phase 1 |
| 4 | `ErrorOccurred` fires with null message | `HandleCameraErrorAsync` accepts null | Phase 1 |
| 5 | Recovery succeeds on attempt 2 | Loop exits early; `ReconnectAttempts` not reset (informational only) | Phase 2 |

**Coverage:** 5/5

---

## Definition of Done

- [x] All acceptance criteria implemented
- [x] Race condition guard verified via `_recoveryCts`
- [x] Build succeeds (0 errors)
