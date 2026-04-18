# US0066: Multi-Camera Manager Core

> **Status:** Done
> **Epic:** EP0011: Multi-Camera Scanning
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-04-18

## User Story

**As a** system
**I want** a `MultiCameraManager` service that orchestrates 1–8 simultaneous camera QR scanner instances
**So that** each camera runs independently with its own scan processing, errors in one camera do not affect others, and cross-camera duplicate scans are automatically suppressed

## Context

### Persona Reference
**System** — Internal service layer; no direct user interaction.

### Background
Prior to EP0011 the app supported a single camera via `CameraQrScannerService` directly. US0066 introduces `MultiCameraManager` as an orchestration layer: it creates N `CameraQrScannerService` instances (all sharing one `IScanDeduplicationService` singleton for cross-camera dedup), manages their lifecycle, and routes QR payloads to the correct instance by camera index.

---

## Inherited Constraints

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| EP0011 | Range | Maximum 8 cameras | `InitializeAsync` throws if count > 8 |
| PRD | Reliability | Camera failure must not cascade | Error isolation per camera |
| PRD | Correctness | Same student QR within 5 min = duplicate | Shared `IScanDeduplicationService` singleton |

---

## Acceptance Criteria

### AC1: Camera Lifecycle — Start / Stop / Restart
- **Given** cameras are initialized via `InitializeAsync`
- **When** `StartAllAsync` is called
- **Then** every enabled camera with a valid device ID transitions to `CameraStatus.Scanning`; cameras with no device ID are set to `CameraStatus.Offline` immediately

### AC2: Cross-Camera Deduplication
- **Given** two cameras detect the same student QR within 5 seconds
- **When** the second payload is processed via `ProcessQrCodeAsync`
- **Then** the shared `IScanDeduplicationService` suppresses the duplicate — result is `DebouncedLocally` or `Duplicate`

### AC3: Scan Routing by Camera Index
- **Given** multiple camera instances running
- **When** `ProcessQrCodeAsync(cameraIndex, payload)` is called
- **Then** the payload is routed to the `CameraQrScannerService` instance for that specific index; other instances are unaffected

### AC4: Max 8 Cameras Enforced
- **Given** `InitializeAsync` is called with more than 8 camera configs
- **When** the method executes
- **Then** an `ArgumentException` is thrown

### AC5: Manual Stop Prevents Auto-Recovery
- **Given** a camera is running
- **When** `StopCameraAsync(index)` is called
- **Then** `IsEnabled` is set to false and the camera does NOT auto-recover; `RestartCameraAsync` must be called explicitly to re-enable it

### AC6: Camera Index Attribution
- **Given** a scan is accepted from camera index 2
- **When** the result is submitted to the server
- **Then** `ScanApiService.SubmitScanAsync` is called with `cameraIndex: 2`

---

## Scope

### In Scope
- `IMultiCameraManager` interface
- `MultiCameraManager` implementation
- `CameraInstance` model
- `CameraStatus` enum
- `ICameraWorker` / `ICameraWorkerFactory` abstraction for headless platform capture
- `IPreferencesService` multi-camera keys (`GetCameraCount`, `GetCameraDeviceId`, etc.)
- `CameraQrScannerService.SetCameraIndex` and `SetScanTypeOverride`

### Out of Scope
- Platform camera capture hardware (handled by `ICameraWorker` implementations — US0068/US0070)
- UI status display (US0068)
- Frame rate display (US0070)

---

## Technical Notes

`MultiCameraManager` holds `Dictionary<int, CameraQrScannerService>` and `Dictionary<int, ICameraWorker>`. Workers fire `QrCodeDetected` events which route to `ProcessQrCodeAsync`. Cross-camera dedup is automatic because all `CameraQrScannerService` instances share the same `IScanDeduplicationService` singleton (DI-resolved).

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Camera count > 8 passed to `InitializeAsync` | `ArgumentException` thrown |
| Device ID empty/null at init | Camera marked `Offline` immediately; other cameras unaffected |
| `ProcessQrCodeAsync` called for unknown index | Warning logged; no-op |
| Exception thrown during QR processing | Caught; camera enters Error state; auto-recovery triggered |
| `StartAllAsync` called twice | `IsRunning` guard in worker; duplicate start is no-op |

---

## Test Scenarios

- [x] `InitializeAsync` with 9 cameras → throws `ArgumentException`
- [x] `StartAllAsync` → all enabled cameras with device IDs enter Scanning status
- [x] `StopCameraAsync(1)` → only camera 1 stops; others unaffected
- [x] `RestartCameraAsync` → resets error state, re-enables camera
- [x] `ProcessQrCodeAsync` routes to correct service instance
- [x] Cross-camera dedup: same student within 5 s → second result is Duplicate

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0004 | Predecessor | Setup page and DI infrastructure | Done |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| `IScanDeduplicationService` | Core singleton | Done |
| `ICameraWorkerFactory` (platform) | Platform service | Done |

---

## Estimation

**Story Points:** 8
**Complexity:** High

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-18 | EP0011 implementation | Created; marked Done (implemented in EP0011 session) |
