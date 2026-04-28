# US0121: Concurrent Camera + USB Scanner Mode

> **Status:** Done
> **Epic:** [EP0012: Concurrent Multi-Modal Scanning](../epics/EP0012-concurrent-multi-modal-scanning.md)
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-04-28

## User Story

**As** Guard Gary
**I want** the app to accept scans from both webcams and a USB barcode scanner at the same time
**So that** I can use whichever input is most convenient (passive webcam capture for students walking past, handheld scanner for visitor passes or close-up reads) without ever needing to switch modes or restart the app

## Context

### Persona Reference

**Guard Gary** — School security guard, novice technical proficiency. He doesn't think about "modes" — he just expects scans to register regardless of which device they came from.
[Full persona details](../personas.md#guard-gary)

### Background

Today, `MainViewModel` reads `Scanner.Mode` from preferences once at startup and chooses **one** event subscription path: either it subscribes to `IMultiCameraManager` events (camera mode) or to `UsbQrScannerService.ScanCompleted` (USB mode). The two pipelines never run together. This story makes them peer subscribers when `Scanner.Mode = "Both"`.

The two services are already architecturally independent — they don't reference each other and their events feed the same `OnScanCompleted` handler. The cross-source deduplication is automatic because `IScanDeduplicationService` is registered as a singleton in `MauiProgram.cs`. The work is largely a refactor of mode-handling branches in `MainViewModel`, `MainPage.xaml.cs`, and the heartbeat reporter to permit both pipelines to run.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| EP0012 | Architecture | `MainViewModel` orchestrates both pipelines simultaneously | Mode check `if (mode == Camera)` must become `if (mode is Camera or Both)`; same for USB |
| EP0012 | Compatibility | Existing `Camera` and `USB` exclusive modes unchanged | New mode is additive; no migration logic alters existing pref values |
| TRD | Architecture | DI singleton lifetime for `IScanDeduplicationService` | Verified — both pipelines automatically share dedup state |
| US0008 | Behaviour | USB keystroke listener attached on `MainPage.OnAppearing` | Listener attachment condition broadens from `mode == "USB"` to `mode is "USB" or "Both"` |

---

## Acceptance Criteria

### AC1: New `Both` Mode Value Recognized

- **Given** `Scanner.Mode` preference is set to `"Both"`
- **When** the app launches and navigates to `MainPage`
- **Then** `MainViewModel.InitializeAsync` starts the multi-camera pipeline (`_multiCameraManager.InitializeAsync` + `StartAllAsync`)
- **And** the USB keystroke listener is attached on `MainPage`
- **And** both subscribe to their respective scan events without exception

### AC2: Existing `Camera` Mode Unchanged

- **Given** `Scanner.Mode` preference is `"Camera"` (existing install)
- **When** the app launches
- **Then** behavior is identical to before EP0012 — only cameras run, no USB listener attached, no USB indicator card visible

### AC3: Existing `USB` Mode Unchanged

- **Given** `Scanner.Mode` preference is `"USB"` (existing install)
- **When** the app launches
- **Then** behavior is identical to before EP0012 — USB listener attached, no cameras started, "Ready to Scan" indicator shown
- **And** the USB indicator card from US0123 is visible (USB-only mode also benefits from the indicator)

### AC4: Camera Scan Flow in `Both` Mode

- **Given** `Scanner.Mode = "Both"` and a camera decodes a valid QR
- **When** the camera fires `ScanCompleted`
- **Then** the existing `OnScanCompleted` handler runs unchanged — student card updates, camera slot card flashes, audio plays, server submission occurs, `ScanLogEntry` persists with `ScanMethod = "Camera"` and `CameraIndex` populated

### AC5: USB Scan Flow in `Both` Mode

- **Given** `Scanner.Mode = "Both"` and the USB scanner sends a valid payload
- **When** `UsbQrScannerService.ScanCompleted` fires
- **Then** the same `OnScanCompleted` handler runs — student card updates, USB indicator card flashes (per US0123), audio plays, server submission occurs, `ScanLogEntry` persists with `ScanMethod = "USB"` and `CameraIndex = null`

### AC6: Cross-Source Deduplication

- **Given** `Scanner.Mode = "Both"` and the same QR payload is decoded via webcam at time T
- **When** the same payload arrives via USB scanner at time T + 2 seconds
- **Then** `ScanDeduplicationService` recognizes the duplicate within its 3-second tier
- **And** only one server submission is made
- **And** the second arrival is logged with `Status = DebouncedLocally` and does not update the student card

### AC7: Scan Log Field Parity

- **Given** any scan (camera or USB) completes in `Both` mode
- **When** `LogScanToHistoryAsync` is called
- **Then** the resulting `ScanLogEntry` contains the same set of populated fields as today's camera scan logs (`Timestamp`, `RawPayload`, `StudentId`, `StudentName`, `ScanType`, `Status`, `Message`, `ScanId`, `NetworkAvailable`, `ProcessingTimeMs`, `GradeSection`, `ErrorDetails`)
- **And** `ScanMethod` reflects the actual source of *that* scan (`"Camera"` or `"USB"`), not the app-level mode (`"Both"`)
- **And** `CameraIndex` and `CameraName` are populated for camera scans and null for USB scans

### AC11: Explicit Source Tag on `ScanResult`

- **Given** the domain model
- **When** EP0012 is built
- **Then** `ScanResult` exposes a new `Source` property of type `ScanSource` (enum: `Camera`, `UsbScanner`)
- **And** `CameraQrScannerService` sets `Source = ScanSource.Camera` on every result it emits
- **And** `UsbQrScannerService` sets `Source = ScanSource.UsbScanner` on every result it emits
- **And** `LogScanToHistoryAsync` derives `ScanMethod` from `result.Source` directly (no inference from `CameraIndex`)

### AC8: Heartbeat Reports USB Health

- **Given** `Scanner.Mode = "Both"` and the heartbeat service is running
- **When** the heartbeat tick fires
- **Then** the heartbeat payload includes a USB scanner health entry alongside camera health entries
- **And** the USB entry includes the timestamp of the last scan received (or null if never)

### AC9: Page Lifecycle Stops Both Pipelines

- **Given** `Scanner.Mode = "Both"` and both pipelines are running
- **When** `MainPage.OnDisappearing` fires (or window closes)
- **Then** `_multiCameraManager.StopAllAsync()` is awaited
- **And** `_usbScanner.StopAsync()` is awaited
- **And** no background timers or event subscriptions leak

### AC10: Mode Default Unchanged for New Installs

- **Given** a fresh install with no `Scanner.Mode` preference set
- **When** the setup wizard runs and detects available devices
- **Then** the chosen default remains `"Camera"` or `"USB"` based on device detection (not `"Both"`) — concurrent mode is opt-in via the setup wizard checkbox (US0122)

---

## Scope

### In Scope

- Refactor `MainViewModel` constructor to subscribe to both `_multiCameraManager` and `_usbScanner` events when mode includes camera/USB
- Helper predicates: `IsCameraMode`, `IsUsbMode` (both true in `Both` mode)
- `MainPage.xaml.cs` keyboard listener attachment when mode includes USB
- `MainViewModel.InitializeAsync` starts both pipelines when in `Both` mode
- `MainViewModel.DisposeAsync` stops both pipelines when in `Both` mode
- `LogScanToHistoryAsync` uses the actual scan source for `ScanMethod` (not the app mode)
- Heartbeat service includes USB scanner health when USB pipeline is active
- Unit tests for mode-routing logic in `MainViewModel`
- Manual test plan for cross-source dedup and concurrent operation

### Out of Scope

- Setup wizard UI changes (US0122)
- USB indicator card UI (US0123)
- Plug/unplug detection
- Multiple USB scanners
- Per-source ENTRY/EXIT scan type override

---

## Technical Notes

### Mode Helper

Replace direct string compares with helpers:

```csharp
public bool IsCameraMode => _scannerMode is "Camera" or "Both";
public bool IsUsbMode => _scannerMode is "USB" or "Both";
```

`MainPage.xaml`'s existing `IsVisible="{Binding IsCameraMode}"` binding continues to work. New `IsVisible="{Binding IsUsbMode}"` will gate the USB indicator card from US0123.

### Event Subscription

Today (mutually exclusive):
```csharp
if (_scannerMode == "Camera") { _multiCameraManager.ScanCompleted += OnMultiCameraScanCompleted; ... }
else { _usbScanner.ScanCompleted += OnScanCompleted; }
```

After:
```csharp
if (IsCameraMode) {
    _multiCameraManager.ScanCompleted += OnMultiCameraScanCompleted;
    _multiCameraManager.ScanUpdated += OnMultiCameraScanUpdated;
    _multiCameraManager.CameraStatusChanged += OnMultiCameraStatusChanged;
}
if (IsUsbMode) {
    _usbScanner.ScanCompleted += OnScanCompleted;
}
```

### Scan Source Identification

`ScanResult` gains a new property:

```csharp
public enum ScanSource { Camera, UsbScanner }

public class ScanResult
{
    // ... existing fields ...
    public ScanSource Source { get; set; }
}
```

Set at the source services:
- `CameraQrScannerService` → `Source = ScanSource.Camera`
- `UsbQrScannerService` → `Source = ScanSource.UsbScanner`

`LogScanToHistoryAsync` reads it directly:
```csharp
ScanMethod = result.Source switch
{
    ScanSource.Camera => "Camera",
    ScanSource.UsbScanner => "USB",
    _ => "Unknown"
}
```

This avoids the brittle `CameraIndex`-null inference and makes future sources (e.g., Bluetooth) trivial to add.

### `MainPage.xaml.cs` Mode Check

Replace direct string comparisons (`_scannerMode == "USB"`) with the new `IsUsbMode` helper exposed on `MainViewModel`. The code-behind reads `_viewModel.IsUsbMode` for the keyboard handler attach decision in both `OnAppearing` and `OnHandlerChanged`. This keeps mode-set knowledge in exactly one place (the ViewModel helper) and avoids the kind of drift where one branch is updated but another isn't.

### Files Likely Touched

- `SmartLog.Scanner.Core/Models/ScanResult.cs` — add `ScanSource` enum and `Source` property
- `SmartLog.Scanner.Core/Services/CameraQrScannerService.cs` — set `Source = ScanSource.Camera` on emitted results
- `SmartLog.Scanner.Core/Services/UsbQrScannerService.cs` — set `Source = ScanSource.UsbScanner` on emitted results
- `SmartLog.Scanner/ViewModels/MainViewModel.cs` — mode helpers (`IsCameraMode`, `IsUsbMode`), dual subscription, `LogScanToHistoryAsync` uses `result.Source`
- `SmartLog.Scanner/Views/MainPage.xaml.cs` — keyboard listener attachment uses `_viewModel.IsUsbMode`
- `SmartLog.Scanner.Core/Services/HeartbeatService.cs` — add `usbScannerLastScanAge` field to heartbeat payload
- `SmartLog.Scanner.Tests/ViewModels/MainViewModelTests.cs` — new tests for `Both` mode routing
- `SmartLog.Scanner.Tests/Services/HeartbeatServiceTests.cs` — assert USB field present when USB pipeline active

### Service Lifetime Verification

Confirm in `MauiProgram.cs` that:
- `IScanDeduplicationService` is registered as singleton (required for cross-source dedup)
- `UsbQrScannerService` and `IMultiCameraManager` can both be active without resource conflict

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| `Both` mode but no cameras enumerated | Camera pipeline starts with zero slots; USB pipeline still works; no error shown |
| `Both` mode but USB scanner not plugged in | USB listener attaches but receives no events; no error; 60s heuristic (US0123) shows warning state |
| Camera scan fires while USB keystroke buffer is mid-payload | Two independent code paths — the USB buffer is unaffected; camera scan processed normally |
| Same payload arrives via USB then camera within 3s | Cross-source dedup via singleton dedup service — only one server submit |
| App in `Both` mode is downgraded to `Camera` after re-running setup | New mode value persisted; on next launch, USB listener does not attach |
| `OnDisappearing` fires while a scan is in flight | Both pipelines stopped via existing await pattern; in-flight scan completes normally before stop |
| Window loses focus (alert dialog) | USB keystroke listener silently drops keystrokes during focus loss; cameras continue; on focus regain, listener resumes |
| `Scanner.Mode` set to unknown value (corrupted preference) | Fallback to `"Camera"` (current default); log warning |

---

## Test Scenarios

- [ ] `MainViewModel` in `Both` mode subscribes to both `_multiCameraManager.ScanCompleted` and `_usbScanner.ScanCompleted`
- [ ] `MainViewModel` in `Camera` mode subscribes only to camera events (no regression)
- [ ] `MainViewModel` in `USB` mode subscribes only to USB events (no regression)
- [ ] `MainPage.OnAppearing` attaches keyboard handler in `Both` mode
- [ ] `MainPage.OnAppearing` attaches keyboard handler in `USB` mode (no regression)
- [ ] `MainPage.OnAppearing` does NOT attach keyboard handler in `Camera` mode (no regression)
- [ ] Camera scan in `Both` mode produces `ScanLogEntry` with `ScanMethod = "Camera"`
- [ ] USB scan in `Both` mode produces `ScanLogEntry` with `ScanMethod = "USB"`
- [ ] Cross-source dedup: same payload via camera then USB within 3s → single server submission
- [ ] `MainViewModel.DisposeAsync` in `Both` mode awaits both `StopAllAsync` (camera) and `_usbScanner.StopAsync`
- [ ] Heartbeat payload in `Both` mode includes USB last-scan-age field
- [ ] Camera scan flow visual feedback (student card, camera slot flash) unchanged in `Both` mode
- [ ] USB scan flow visual feedback (student card update + audio) functions in `Both` mode

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0007 | Blocked By | Camera-Based QR Scanning pipeline | Done |
| US0008 | Blocked By | USB Barcode Scanner Input pipeline | Done |
| US0066 | Blocked By | Multi-Camera Manager Core | Done |
| US0089 | Blocked By | Device-level scan type unification | Done |
| US0120 | Related | Heartbeat service (extend payload) | Draft |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| MAUI Preferences for `Scanner.Mode` | Platform API | Available |
| Singleton `IScanDeduplicationService` registration | Internal DI | Verified |

---

## Estimation

**Story Points:** 5
**Complexity:** Medium

**Rationale:** The bulk of the work is straightforward refactoring of mode-check branches across `MainViewModel`, `MainPage.xaml.cs`, and `HeartbeatService`. The dedup story is automatic (singleton). The risk lies in unexpected interactions between camera UI elements (focus, dialog dismissal) and the USB keyboard listener — needs hardware verification on Windows once code is in place.

---

## Open Questions

- [x] **Resolved 2026-04-28:** Add an explicit `ScanResult.Source` property of type `ScanSource` (enum: `Camera`, `UsbScanner`). Both source services tag their emitted results; `LogScanToHistoryAsync` reads `result.Source` directly. Avoids brittle `CameraIndex`-null inference and is extensible to future sources. (See AC11 and Technical Notes.)
- [x] **Resolved 2026-04-28:** `MainPage.xaml.cs` uses the new `_viewModel.IsUsbMode` helper for keyboard handler attachment in both `OnAppearing` and `OnHandlerChanged` — keeps mode-set knowledge centralized in `MainViewModel`.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-28 | SDLC Studio | Initial story created under EP0012 |
| 2026-04-28 | SDLC Studio | Open questions resolved — added AC11 (explicit `ScanResult.Source` property), Technical Notes updated for `IsUsbMode` helper usage in code-behind |
