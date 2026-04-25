# US0070: Error Isolation and Auto-Recovery

> **Status:** Done
> **Epic:** EP0011: Multi-Camera Scanning
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-04-18

## User Story

**As a** Guard (Gary)
**I want** camera failures to be isolated and automatically recovered without any action on my part
**So that** a disconnected or erroring camera does not bring down the other cameras, and the system self-heals within 30 seconds without requiring a manual restart

## Context

### Persona Reference
**Guard Gary** — School gate guard who operates the scanner all day. Should not need to diagnose technical failures.

### Background
In a multi-camera setup, USB cameras can disconnect mid-session (cable pulled, hub issue). US0070 ensures: (1) exceptions in one camera's decode pipeline are caught and isolated; (2) the camera enters Error state and an auto-recovery loop retries up to 3 times with 10-second delays; (3) after 3 failures the camera transitions to Offline and shows a manual Restart button; (4) a per-camera frame rate display shows "N fps" while scanning and "—" otherwise.

---

## Inherited Constraints

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| EP0011 | Reliability | One camera failure must not affect others | Try/catch per camera; each has own `CancellationTokenSource` |
| EP0011 | Recovery | Auto-recovery: 3 attempts, 10 s apart | `TriggerAutoRecovery` with `_recoveryCts` race condition guard |
| PRD | UX | Guard sees clear error state | Status card shows ⚠ Error + Restart button when `CanRestart=true` |

---

## Acceptance Criteria

### AC1: Error Isolation
- **Given** multiple cameras are scanning
- **When** camera N throws an exception during QR processing or worker startup
- **Then** only camera N enters `CameraStatus.Error`; all other cameras continue scanning unaffected

### AC2: Auto-Recovery (3 × 10 s)
- **Given** a camera enters Error state and `IsEnabled=true`
- **When** `TriggerAutoRecovery` is called
- **Then** the manager waits 10 s, attempts `StartCameraInternalAsync`, and repeats up to 3 times; on success the camera returns to Scanning

### AC3: Offline After 3 Failures
- **Given** 3 auto-recovery attempts have all failed
- **When** no more retries remain
- **Then** the camera transitions to `CameraStatus.Offline`; `CameraStatusChanged` fires; status card shows ⊘ Offline + Restart button

### AC4: Manual Restart Resets Recovery Counter
- **Given** a camera is Offline or Error
- **When** the user taps the Restart button on the status card
- **Then** `MultiCameraManager.RestartCameraAsync(index)` is called; `ReconnectAttempts` resets to 0; a fresh start attempt is made

### AC5: Race Condition Guard
- **Given** a camera enters Error state multiple times in quick succession
- **When** each error triggers `TriggerAutoRecovery`
- **Then** only one recovery loop runs at a time per camera (previous `CancellationTokenSource` is cancelled before the new one starts)

### AC6: Frame Rate Display
- **Given** a camera is in Scanning state
- **When** the 1-second frame-rate timer fires
- **Then** each slot's `FrameRateDisplay` shows "N fps" (frames decoded in the last second); shows "—" when not scanning

---

## Scope

### In Scope
- `HandleCameraErrorAsync` — sets Error state, fires `CameraStatusChanged`, triggers recovery
- `TriggerAutoRecovery` with `Dictionary<int, CancellationTokenSource> _recoveryCts`
- `CameraSlotState.CanRestart`, `StatusBrush`, `StatusText` computed properties
- `RestartCommand` on `CameraSlotState` wired to `MultiCameraManager.RestartCameraAsync`
- `ICameraWorker.ErrorOccurred` event wired to `HandleCameraErrorAsync`
- `CameraHeadlessWorker` (Mac Catalyst + Windows) — headless hardware capture, no native view
- Frame rate: `IncrementFrameCount()` on `CameraSlotState`, 1-s timer in `MainViewModel`

### Out of Scope
- CPU-load watchdog (future enhancement)
- Black-frame detection (future enhancement)
- USB 3.0 bandwidth warning on main page (shown in Setup only)

---

## Technical Notes

`CameraHeadlessWorker` (Mac Catalyst) uses `AVCaptureSession` + `AVCaptureMetadataOutput` with no `UIView` or `AVCaptureVideoPreviewLayer` — this was the root cause fix for the transparent Mac window issue. Multiple preview layers added to the MAUI view hierarchy at startup corrupted the Mac Catalyst window compositor. Camera 0 gets a single preview layer attached *after* cameras start via `AttachPreview(UIView)`.

Windows worker wraps the existing `WindowsCameraScanner` (already headless).

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Manual stop during recovery loop | `CancelRecovery(index)` cancels the loop; `IsEnabled=false` prevents re-trigger |
| Camera reconnects before retry delay | `StartCameraInternalAsync` succeeds on first retry; loop exits |
| All cameras fail simultaneously | Each camera has its own independent recovery loop |
| `RestartCameraAsync` called on already-scanning camera | `IsRunning` guard is no-op |
| Worker `ErrorOccurred` fires with null message | `HandleCameraErrorAsync` accepts null; stored in `cam.ErrorMessage` |

---

## Test Scenarios

- [x] Camera error → only that camera enters Error state; others remain Scanning
- [x] Auto-recovery: 3 attempts then Offline
- [x] `RestartCameraAsync` resets `ReconnectAttempts` to 0
- [x] `_recoveryCts` cancels previous loop before starting new one
- [x] `CanRestart` true when status is Error or Offline
- [x] `UpdateFrameRate()` shows "N fps" when Scanning, "—" when not

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0066 | Predecessor | `MultiCameraManager` lifecycle infrastructure | Done |
| US0068 | Companion | `CameraSlotState` status card display | Done |

---

## Estimation

**Story Points:** 5
**Complexity:** Medium-High

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-18 | EP0011 implementation | Created; marked Done (implemented in EP0011 session) |
