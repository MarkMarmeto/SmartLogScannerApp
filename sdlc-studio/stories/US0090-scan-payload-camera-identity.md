# US0090: Scan Payload — Include Camera Index and Camera Name

> **Status:** Done
> **Epic:** EP0011: Multi-Camera Scanning (cross-project)
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-04-24

## User Story

**As a** Admin Amy (reviewing scan logs centrally)
**I want** every scan submitted from the scanner to identify which camera produced it — both by slot index and by the user-assigned camera name
**So that** we can audit gate activity per camera (e.g. which lane is busiest), diagnose hardware issues, and attribute scans correctly in multi-camera deployments.

## Context

### Persona Reference
**Admin Amy** — Reviews scans centrally.
**Tony (IT Admin)** — Troubleshoots camera hardware using camera-level logs.

### Background
Today the scan submission payload does not identify which camera produced the scan. In single-camera installs that was fine. With EP0011 supporting up to 8 cameras per device, every scan needs to carry camera provenance. The WebApp-side receiving change is covered in US0093; this story covers the Scanner-side: collect camera identity and send it.

Camera identity has two parts:
- `cameraIndex` — stable 1..N slot index used by `MultiCameraManager`
- `cameraName` — user-assigned friendly label set on the Setup page (e.g. "Main Gate Left", "Back Entrance")

---

## Inherited Constraints

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| EP0011 | Architecture | `MultiCameraManager` already tracks per-camera slot index and label | Reuse, don't invent |
| PRD | Integration | Scan API is versioned (`/api/v1/scans`); backward compat matters | Server must accept payloads with or without camera fields |
| US0089 | Related | Scan type is unified to device-level | Camera identity and scan type are independent concerns |

---

## Acceptance Criteria

### AC1: Setup Captures Camera Name
- **Given** I am on the multi-camera Setup page
- **Then** each camera row has a "Name" text input alongside the camera picker
- **And** the name defaults to the enumerated camera's friendly name (e.g. "Logitech C270")
- **And** I can override it (e.g. "Main Gate Left")
- **And** saving persists the name to `MultiCamera.{n}.Name` preference

### AC2: Scan Payload Fields
- **Given** a camera-driven scan is submitted
- **Then** the request body to `POST /api/v1/scans` includes:
  ```json
  {
    "qrPayload": "...",
    "scannedAt": "...",
    "scanType": "ENTRY",
    "cameraIndex": 1,
    "cameraName": "Main Gate Left"
  }
  ```

### AC3: Single-Camera and USB Barcode Fallbacks
- **Given** the device runs in single-camera mode (legacy / single camera slot)
- **Then** `cameraIndex = 1` and `cameraName` is the name set for slot 1 (or `"Default"` if unset)
- **Given** a scan came from a USB keyboard-wedge barcode scanner (no camera)
- **Then** `cameraIndex = null` and `cameraName = "USB Scanner"`

### AC4: Name Changes Propagate Immediately
- **Given** cameras are running
- **When** I rename a camera on Setup and save
- **Then** the next scan from that camera carries the new name
- **And** no app restart is required

### AC5: Camera Index Stable Across Reorders
- **Given** my cameras are in slots 1, 2, 3
- **When** I re-select a different physical camera for slot 2
- **Then** `cameraIndex = 2` continues to identify that slot (the name changes to reflect the new physical camera)

### AC6: Offline Queue Carries Fields Through
- **Given** a scan is queued offline via `OfflineQueueService`
- **When** it is later flushed by `BackgroundSyncService`
- **Then** `cameraIndex` and `cameraName` are submitted with the original values captured at scan time

### AC7: Scan Logs View Reflects Camera
- **Given** I open the in-app Scan Logs page on the scanner
- **Then** each logged scan row shows "Camera {index} — {name}"

---

## Scope

### In Scope
- Setup page: per-camera name input
- Preferences: `MultiCamera.{n}.Name` persistence
- `ScanApiService.SubmitScanAsync` and its request DTO — add `cameraIndex`, `cameraName`
- Camera identity passed through the scan event → submission pipeline
- Offline queue carries the fields
- In-app Scan Logs UI shows camera identity

### Out of Scope
- WebApp-side `Scan` entity, API ingestion, and Scan Logs column (covered in US0093)
- Camera-level statistics / dashboard on the scanner

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| User leaves name blank | Fall back to enumerated camera's friendly name; if unavailable, `"Camera {index}"` |
| Duplicate names across slots | Allowed — server relies on index for uniqueness; name is human-readable only |
| Payload rejected by older WebApp version | Client logs a warning but does not retry indefinitely (existing retry policy applies) |
| Camera removed between scan decode and submit | Captured values (index + name at decode time) are submitted; transient UI error is separate |

---

## Test Scenarios

- [ ] Setup saves per-camera name
- [ ] Scan payload includes `cameraIndex` and `cameraName`
- [ ] Rename mid-session → next scan uses new name
- [ ] Index stays stable when physical camera changes in a slot
- [ ] USB scanner path sets `cameraIndex = null`, `cameraName = "USB Scanner"`
- [ ] Offline queue preserves both fields
- [ ] Single-camera fallback produces `cameraIndex = 1`, `cameraName = "Default"` (or configured)
- [ ] In-app Scan Logs page displays camera identity

---

## Technical Notes

### Files to Modify
- **Modify:** `SmartLog.Scanner.Core/Services/ScanApiService.cs` — extend request DTO
- **Modify:** `SmartLog.Scanner.Core/Models/ScanSubmission.cs` (or equivalent DTO) — add `CameraIndex`, `CameraName`
- **Modify:** `SmartLog.Scanner.Core/Services/MultiCameraManager.cs` — include camera identity in scan event
- **Modify:** `SmartLog.Scanner.Core/Services/CameraQrScannerService.cs` — carry slot + name through scan event
- **Modify:** `SmartLog.Scanner.Core/Services/OfflineQueueService.cs` — persist `CameraIndex`, `CameraName` in `QueuedScans` table
- **Migration:** EF Core migration adding two nullable columns to `QueuedScans`
- **Modify:** `SmartLog.Scanner/Pages/SetupPage.xaml(.cs)` — per-camera name input
- **Modify:** `SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs` — per-camera name binding
- **Modify:** `SmartLog.Scanner.Core/ViewModels/ScanLogsViewModel.cs` + Scan Logs page — display camera identity

### Wire Format Contract
Nullable semantics: server treats missing/null fields as "single-camera legacy". Scanner populates both fields for camera-driven scans.

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| [US0066](US0066-multi-camera-manager-core.md) | Foundation | MultiCameraManager tracks slots | Done |
| [US0071](US0071-multi-camera-setup-configuration.md) | Foundation | Setup page edit flow | Done |
| US0093 (WebApp) | Paired | Server-side ingestion + schema | Draft |

---

## Estimation

**Story Points:** 3
**Complexity:** Medium — touches DTO, services, offline queue (schema change), Setup UI, and in-app logs

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-24 | Claude | Initial story drafted from V2 planning session |
