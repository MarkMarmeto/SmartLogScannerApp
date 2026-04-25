# US0069: Per-Camera Scan Type (ENTRY/EXIT)

> **Status:** Done
> **Epic:** EP0011: Multi-Camera Scanning
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-04-18

## User Story

**As a** Guard (Gary)
**I want** each camera to have its own ENTRY or EXIT scan type, with a toolbar toggle that updates all cameras at once
**So that** dedicated entry and exit gates submit the correct scan type without manual switching, and I can override all cameras simultaneously for non-standard situations

## Context

### Persona Reference
**Guard Gary** — School gate guard who operates the scanner all day.

### Background
Previously a single global ENTRY/EXIT toggle controlled the one camera. With multiple cameras, each gate camera is typically dedicated to one direction — but the guard also needs to flip all cameras at once for morning/afternoon transitions. US0069 implements per-camera scan types stored in preferences, with `SetScanTypeOverride` on `CameraQrScannerService` allowing live updates without restarting cameras.

---

## Inherited Constraints

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| EP0011 | Correctness | Scan type must be submitted correctly to the server | `SetScanTypeOverride` must propagate before next scan |
| PRD | Persistence | Setting survives app restart | Per-camera scan type stored in `MultiCamera.{n}.ScanType` |

---

## Acceptance Criteria

### AC1: Per-Camera Scan Type Stored
- **Given** the Setup page has per-camera scan type pickers
- **When** the admin saves the configuration
- **Then** each camera's scan type is stored in `MultiCamera.{n}.ScanType` preference

### AC2: Scan Type Loaded at Init
- **Given** per-camera scan types are saved
- **When** `MultiCameraManager.InitializeAsync` is called
- **Then** each `CameraQrScannerService` instance is initialised with its stored scan type via `SetScanTypeOverride`

### AC3: Toolbar Toggle Updates All Cameras
- **Given** cameras are running
- **When** the ENTRY/EXIT toolbar button is tapped
- **Then** all per-camera preferences are written to `CurrentScanType`, `UpdateScanTypes()` is called, and all `CameraSlots[n].ScanType` badge values update immediately on the UI

### AC4: Scan Type Badge Visible Per Card
- **Given** cameras have different scan types
- **When** the status card grid is rendered
- **Then** each card shows a teal "ENTRY" or red "EXIT" badge reflecting its current scan type

### AC5: Correct Type Submitted to Server
- **Given** camera 1 is configured as EXIT
- **When** it detects a QR code
- **Then** `ScanApiService.SubmitScanAsync` is called with `scanType: "EXIT"` for that scan

---

## Scope

### In Scope
- `CameraQrScannerService.SetScanTypeOverride(string? scanType)`
- `MultiCameraManager.UpdateScanTypes()` — reads per-camera prefs, calls `SetScanTypeOverride`
- `ToggleScanType` in `MainViewModel` — writes all per-camera prefs then syncs `CameraSlots`
- `CameraSlotState.ScanType` and `ScanTypeBadgeColor` computed property
- Per-camera scan type in `SetupPage.xaml` (US0071)

### Out of Scope
- Per-camera independent toggle from the main page (toolbar toggle is global-override only)
- Scan type history / audit log

---

## Technical Notes

**Bug fixed in this session:** `ToggleScanType` was writing only to the global `Scanner.ScanType` preference, but `UpdateScanTypes()` reads from `MultiCamera.{n}.ScanType`. The fix writes to all per-camera keys before calling `UpdateScanTypes()`, then syncs `CameraSlots` from `_multiCameraManager.Cameras`.

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Toggle called before cameras initialised | Per-camera prefs still written; picked up at next `InitializeAsync` |
| Camera with no scan type preference | `GetCameraScanType(n)` defaults to "ENTRY" |
| `UpdateScanTypes` called on empty `_cameras` | No-op; no exception |

---

## Test Scenarios

- [x] `SetScanTypeOverride("EXIT")` → next scan submitted with EXIT type
- [x] `UpdateScanTypes()` reads per-camera prefs and calls `SetScanTypeOverride` on each service
- [x] Toggle writes per-camera prefs for all configured cameras
- [x] `CameraSlotState.ScanTypeBadgeColor` returns red for EXIT, teal for ENTRY

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0066 | Predecessor | `MultiCameraManager.UpdateScanTypes()` | Done |
| US0071 | Companion | Per-camera scan type in Setup page | Done |

---

## Estimation

**Story Points:** 3
**Complexity:** Low

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-18 | EP0011 implementation | Created; marked Done (bug fix + implementation in EP0011 session) |
