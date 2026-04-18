# US0071: Multi-Camera Setup Configuration

> **Status:** Done
> **Epic:** EP0011: Multi-Camera Scanning
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-04-18

## User Story

**As a** IT Admin (Ian)
**I want** to configure 1–8 cameras on the Setup page with per-camera device, name, scan type, and enable/disable controls
**So that** I can set up the correct number of gate cameras before the school day starts, assign each one to a physical device, and designate each as ENTRY or EXIT

## Context

### Persona Reference
**IT Admin Ian** — School IT administrator responsible for deploying and configuring scanner devices.
[Full persona details](../personas.md)

### Background
Prior to EP0011 the Setup page had a single camera picker. US0071 replaces it with a dynamic multi-camera configuration section: a camera count stepper (1–8), a per-camera config row for each slot (display name, device picker, scan type, enable switch, test button), and a USB 3.0 warning when 3+ cameras are configured.

---

## Inherited Constraints

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| EP0011 | Range | Maximum 8 cameras | Stepper capped at 8 |
| PRD | Performance | USB 3.0 for 3+ cameras | Warning shown when CameraCount ≥ 3 |
| PRD | Persistence | Settings survive app restart | All config saved to MAUI Preferences |

---

## Acceptance Criteria

### AC1: Camera Count Selector
- **Given** the Setup page is open in Camera mode
- **When** the admin adjusts the Stepper
- **Then** the value is clamped to 1–8, and per-camera rows are added or removed to match

### AC2: Per-Camera Configuration Row
- **Given** a camera slot row is visible
- **When** the admin sets display name, selects a device, chooses scan type, and toggles enabled
- **Then** all four values are held in the slot's observable state (TwoWay bindings)

### AC3: Camera Test Button
- **Given** a device is selected for a camera slot
- **When** the admin taps "Test"
- **Then** `ICameraEnumerationService.TestCameraAsync` is called, the result ("Camera OK" / error) is shown, and `IsConnected` reflects the outcome

### AC4: USB 3.0 Warning
- **Given** CameraCount ≥ 3
- **When** the warning banner is evaluated
- **Then** it is visible; hidden when CameraCount < 3

### AC5: Save and Restore
- **Given** the admin saves the Setup form
- **When** the app is restarted
- **Then** camera count, per-camera names, device IDs, scan types, and enabled states are restored from `IPreferencesService`

### AC6: Backward Compatibility
- **Given** an existing single-camera install with no multi-camera preferences set
- **When** the Setup page loads
- **Then** CameraCount defaults to 1 and camera 0 device falls back to the legacy `SelectedCameraId` preference

---

## Scope

### In Scope
- Camera count stepper (1–8) with clamping
- Per-camera rows: display name `Entry`, device `Picker`, scan type `Picker` (ENTRY/EXIT), enable `Switch`, Test `Button`
- USB 3.0 warning banner
- `CameraSlotViewModel` observable state class
- `SetupViewModel` multi-camera section (load/save/count change)
- Platform `ICameraEnumerationService.TestCameraAsync` (Mac Catalyst + Windows)

### Out of Scope
- Live camera preview on the Setup page
- USB scanner mode configuration (separate section)
- Camera reordering / drag-drop

---

## Technical Notes

`SetupViewModel` holds `ObservableCollection<CameraSlotViewModel> CameraSlots` and `int CameraCount`. `OnCameraCountChanged` adds/removes slots and re-populates `AvailableDevices` from the enumerated device list.

`CameraSlotViewModel` is defined in `SmartLog.Scanner.Core` (no platform dependencies) and receives a nullable `ICameraEnumerationService` so it is testable without a MAUI runtime.

Preferences keys: `MultiCamera.Count`, `MultiCamera.{n}.Name`, `MultiCamera.{n}.DeviceId`, `MultiCamera.{n}.ScanType`, `MultiCamera.{n}.Enabled`.

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| No USB cameras detected | Device pickers show empty list; Test shows "No device selected" |
| Camera count reduced below active slots | Excess `CameraSlotViewModel` items removed from end of collection |
| `TestCameraAsync` throws | Exception caught in `CameraSlotViewModel`; TestResult shows error message |
| Saved DeviceId no longer present on restart | `SelectedDevice` stays null; admin must re-select |
| Platform has no `ICameraEnumerationService` | Null-safe; Test button shows "not available on this platform" |

---

## Test Scenarios

- [x] Stepper clamped: set to 0 → stays 1; set to 9 → stays 8
- [x] Slot count matches CameraCount after increment and decrement
- [x] SaveMultiCameraConfig persists all per-slot values to preferences
- [x] LoadMultiCameraConfig restores saved values on re-open
- [x] TestCameraAsync success → IsConnected=true, TestResult="Camera OK"
- [x] TestCameraAsync failure → IsConnected=false, TestResult contains error
- [x] ShowUsb3Warning true when CameraCount=3, false when CameraCount=2

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0004 | Predecessor | Setup page scaffold and navigation | Done |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| `ICameraEnumerationService` platform impls | Platform service | Done |
| `IPreferencesService` multi-camera keys | Core service | Done |

---

## Estimation

**Story Points:** 5
**Complexity:** Medium

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-18 | EP0011 implementation | Created; marked Done (implemented in EP0011 session) |
