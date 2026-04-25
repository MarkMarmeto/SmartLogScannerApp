# US0089: Unify Scan Type to Device-Level (Deprecate Per-Camera Scan Type)

> **Status:** Done
> **Epic:** EP0011: Multi-Camera Scanning (cross-project)
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-04-24

## User Story

**As a** Guard Gary (gate guard)
**I want** a single ENTRY/EXIT toggle on the device that applies to every camera at once
**So that** I don't have to remember which camera is which scan type, and I can flip from entry-heavy (morning) to exit-heavy (dismissal) with one tap — as the simpler model that fits how we actually run the gate.

## Context

### Persona Reference
**Guard Gary** — Runs the gate.
**Tony (IT Admin)** — Deploys the configuration.

### Background
US0069 shipped per-camera scan type: each camera has its own ENTRY or EXIT setting, with a "master toggle" that flips them all. In practice, schools report this is more complexity than value — at a single gate, every camera of that gate is pointed the same direction, and mixed-direction gates are handled by deploying separate scanner devices. The per-camera config invites mis-configuration without a matching operational benefit.

This story **deprecates US0069**: remove per-camera scan type storage, collapse to a single device-level `Scanner.ScanType` preference, and migrate any existing installs cleanly.

---

## Inherited Constraints

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| EP0011 | Correctness | Scans must submit with the correct type | Device-level value threaded to every `CameraQrScannerService` at init + on toggle |
| EP0011 | Data | Existing installs may have `MultiCamera.{n}.ScanType` entries set | Migration converts existing per-camera values to a single device value |
| PRD | UX | Gary uses ONE toggle during the day | Main page shows exactly one ENTRY/EXIT control |

---

## Acceptance Criteria

### AC1: Device-Level Scan Type is the Only Source of Truth
- **Given** a fresh install
- **Then** only `Scanner.ScanType` preference exists (ENTRY or EXIT)
- **And** no `MultiCamera.{n}.ScanType` preferences are read or written

### AC2: Single Toggle Updates All Cameras
- **Given** cameras are running
- **When** I tap the ENTRY/EXIT toggle on the main page
- **Then** `Scanner.ScanType` is updated
- **And** every active `CameraQrScannerService` immediately picks up the new value via a single `SetScanTypeOverride` broadcast
- **And** every camera tile shows the same scan type badge

### AC3: Setup Page — No Per-Camera Picker
- **Given** I open the Setup page
- **Then** there is no per-camera "Scan Type" picker in each camera row
- **And** a single "Scan Type" picker appears in the device-level / gateway section instead

### AC4: Migration of Existing Per-Camera Values
- **Given** the app was previously installed and has `MultiCamera.0.ScanType`, `MultiCamera.1.ScanType`, etc.
- **When** the app starts for the first time on the new version
- **Then** a migration runs:
  - If all per-camera values are identical → use that value as `Scanner.ScanType`
  - If values differ → use `Scanner.ScanType` if already set; else default to `"ENTRY"` with a one-time notification "Scan Type unified to device-level — verify in Setup"
- **And** all `MultiCamera.{n}.ScanType` keys are deleted from Preferences

### AC5: Scan Payload Carries Device-Level Type
- **Given** the device is set to EXIT
- **When** any camera detects a QR code
- **Then** `ScanApiService.SubmitScanAsync` is called with `scanType: "EXIT"` regardless of which camera slot produced the scan

### AC6: Toolbar Toggle Persists Across Restarts
- **Given** I flip the toggle to EXIT
- **When** I restart the app
- **Then** the device opens in EXIT mode (persisted via `Scanner.ScanType`)

### AC7: Deprecate US0069 Code Paths
- **Given** code previously supporting per-camera scan type
- **Then** `CameraQrScannerService.SetScanTypeOverride` continues to exist for runtime propagation
- **But** all code paths that *read or write* per-camera Preferences keys are removed
- **And** `MultiCameraManager.UpdateScanTypes()` is simplified to propagate the single device value

---

## Scope

### In Scope
- Remove per-camera Scan Type pickers from Setup page
- Remove per-camera Preferences reads/writes
- Single device-level `Scanner.ScanType` preference
- One-time migration at app startup
- Simplify `MultiCameraManager.UpdateScanTypes()` to one device value
- Update unit tests

### Out of Scope
- Removing US0069's `SetScanTypeOverride` primitive — still used as the runtime mechanism to update cameras
- Adding a per-scan override (power-user scenario — not requested)
- Adding Scan Type schedule/automation

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Fresh install, no existing preferences | Default `Scanner.ScanType = "ENTRY"` |
| Legacy install with mixed per-camera values | Migration notifies user; default to ENTRY unless device-level was set |
| User opens Setup after migration | Sees single scan type picker; no mention of per-camera values |
| Scan in flight during toggle | Scan submits with the type that was current when the QR was decoded (whichever ran first); acceptable |

---

## Test Scenarios

- [ ] Toggle from ENTRY to EXIT updates all camera badges
- [ ] Toggle persists across restarts
- [ ] Scan submission uses device-level value
- [ ] Migration from mixed per-camera values lands on a sane default + notification
- [ ] Migration from uniform per-camera values carries the value forward
- [ ] Setup page no longer renders per-camera Scan Type picker
- [ ] `MultiCamera.{n}.ScanType` keys removed post-migration

---

## Technical Notes

### Files to Modify
- **Modify:** `SmartLog.Scanner/ViewModels/MainViewModel.cs` — `ToggleScanType` writes only `Scanner.ScanType`
- **Modify:** `SmartLog.Scanner.Core/Services/MultiCameraManager.cs` — `UpdateScanTypes()` reads single pref, calls `SetScanTypeOverride` on each worker
- **Modify:** `SmartLog.Scanner/Pages/SetupPage.xaml(.cs)` — remove per-camera Scan Type pickers; add single device-level picker
- **Modify:** `SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs` — drop per-camera `ScanType` properties
- **Modify:** `SmartLog.Scanner.Core/ViewModels/CameraSlotViewModel.cs` (a.k.a. `CameraSlotState`) — scan type derives from single device value
- **New:** `SmartLog.Scanner.Core/Services/ScanTypeMigrationService.cs` — one-shot migration at app startup (idempotent)
- **Modify:** `App.xaml.cs` — invoke migration before `MultiCameraManager.InitializeAsync`
- **Tests:** drop per-camera scan type tests; add migration tests + device-level propagation tests

### Deprecation Note for US0069
US0069's AC1, AC2, AC3 (per-camera storage + load + toolbar-writes-all-per-camera-keys) are superseded. Its `SetScanTypeOverride` runtime mechanism and the card-badge rendering (AC4) remain valid — just now all cards show the same value.

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| [US0069](US0069-per-camera-scan-type.md) | Supersedes | Being deprecated — `SetScanTypeOverride` primitive reused | Done |
| [US0066](US0066-multi-camera-manager-core.md) | Foundation | MultiCameraManager + workers | Done |
| [US0071](US0071-multi-camera-setup-configuration.md) | UI | Setup page exists | Done |

---

## Estimation

**Story Points:** 3
**Complexity:** Medium — UI + code simplification + migration + test cleanup

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-24 | Claude | Initial story drafted from V2 planning session |
