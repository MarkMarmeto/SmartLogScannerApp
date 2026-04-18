# US0068: Main Page Camera Grid UI

> **Status:** Done
> **Epic:** EP0011: Multi-Camera Scanning
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-04-18

## User Story

**As a** Guard (Gary)
**I want** the main scan page to show a live camera preview and status cards for each configured camera
**So that** I can see which cameras are scanning, confirm a student was scanned by the correct gate, and quickly identify any camera that has gone offline

## Context

### Persona Reference
**Guard Gary** — School gate guard who operates the scanner all day.
[Full persona details](../personas.md)

### Background
Prior to EP0011 the main page showed a single `CameraQrView` control with a live preview. Adding 8 native preview views simultaneously broke the Mac Catalyst window compositor (window became transparent). The new design uses one `CameraPreviewView` (camera 0 only) plus pure-XAML status cards for all cameras — status cards show name, scan type badge, status text, flash animation on scan, and a restart button when a camera is in error.

---

## Inherited Constraints

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| EP0011 | Mac Catalyst | Multiple AVCaptureVideoPreviewLayers crash window compositor | Only ONE preview layer; all others headless |
| PRD | UX | Guard must see most-recent scan result regardless of which camera fired | Shared result panel below status cards |

---

## Acceptance Criteria

### AC1: Camera 0 Live Preview
- **Given** the app is in Camera mode and cameras are started
- **When** the main page appears
- **Then** a single live camera preview is shown for camera 0 on the left side of the camera row; camera 0's `AVCaptureVideoPreviewLayer` is attached post-init (not at view creation time)

### AC2: Status Card Grid
- **Given** N cameras are configured (1–8)
- **When** the main page is displayed
- **Then** N status cards are shown in a wrapping `FlexLayout`; each card shows: display name, scan type badge (teal ENTRY / red EXIT), status text (● Scanning / ⊘ Offline / ⚠ Error / ○ Idle), frame rate

### AC3: Shared Result Panel
- **Given** any camera detects a valid QR code
- **When** the scan result is processed
- **Then** the shared student ID card area at the bottom updates with student name, grade, section, and which camera produced the scan

### AC4: Flash Animation on Accepted Scan
- **Given** a scan is accepted from camera index N
- **When** `TriggerSlotFlash(N, studentName)` is called
- **Then** the status card for camera N shows the student name for 1.5 s; rapid subsequent scans cancel the previous flash timer (no stale clear)

### AC5: USB Mode Hides Camera UI
- **Given** the scanner is configured in USB mode
- **When** the main page is shown
- **Then** the camera preview and all status cards are hidden (`IsVisible=false` via `IsCameraMode`)

### AC6: Hidden Slots for Unconfigured Cameras
- **Given** camera count is 3
- **When** the status card grid is rendered
- **Then** only 3 cards are visible; the remaining 5 slots have `IsVisible=false`

---

## Scope

### In Scope
- `CameraPreviewView` MAUI control + `CameraPreviewHandler` (Mac Catalyst)
- `CameraSlotState` observable per-camera UI state
- `FlexLayout` + `BindableLayout` status card grid in `MainPage.xaml`
- `CameraHeadlessWorker.AttachPreview(UIView)` / `DetachPreview()`
- `MainViewModel.CameraSlots` observable collection
- Flash animation with per-slot `CancellationTokenSource` leak prevention
- `MainPage.xaml.cs` preview attachment after `InitializeAsync`

### Out of Scope
- Per-camera live preview (only camera 0 has preview)
- Camera preview on Windows (headless only on Windows)
- Responsive grid column count changes (FlexLayout wraps automatically)

---

## Technical Notes

`CameraPreviewView` is a plain MAUI `View` subclass. `CameraPreviewHandler` creates a `UIView` on Mac Catalyst. After `InitializeAsync` completes, `MainPage.xaml.cs` calls `_viewModel.ConfigureCameraPreview(0, worker => handler.AttachWorkerPreview(worker))` to wire the `AVCaptureVideoPreviewLayer` from the headless worker to the preview container view.

`CameraSlots` is an `ObservableCollection<CameraSlotState>` initialised with 8 slots in the constructor. Slots beyond the configured count have `IsVisible=false`.

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Camera 0 has no device assigned | Preview container shows black background; slot card shows Offline |
| All cameras offline | Status cards all show ⊘ Offline; shared result panel shows last result |
| Preview handler not yet attached when camera starts | Worker starts headless; `AttachPreview` called after handler ready |
| USB mode selected | Entire camera row `IsVisible=false`; preview not attached |
| Rapid scans on same camera | Previous flash `CancellationTokenSource` cancelled before new one starts |

---

## Test Scenarios

- [x] `IsCameraMode` returns false when scanner mode is USB
- [x] `CameraSlots` initialised with 8 items; slots beyond count have `IsVisible=false`
- [x] `TriggerSlotFlash` sets `ShowFlash=true`, then clears after 1.5 s
- [x] Rapid flash calls cancel previous timer (no double-clear)
- [x] `OnMultiCameraStatusChanged` updates correct slot's `Status` property

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0066 | Predecessor | `MultiCameraManager` events | Done |
| US0070 | Companion | `CameraHeadlessWorker` with `AttachPreview` | Done |

---

## Estimation

**Story Points:** 5
**Complexity:** Medium

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-18 | EP0011 implementation | Created; marked Done (implemented in EP0011 session) |
