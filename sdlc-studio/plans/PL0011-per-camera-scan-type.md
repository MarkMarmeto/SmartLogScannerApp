# PL0011: Per-Camera Scan Type — Implementation Plan

> **Status:** Complete
> **Story:** [US0069: Per-Camera Scan Type](../stories/US0069-per-camera-scan-type.md)
> **Epic:** EP0011: Multi-Camera Scanning
> **Created:** 2026-04-18
> **Language:** C# / .NET MAUI 8

## Overview

Per-camera ENTRY/EXIT scan type stored in preferences, applied via `SetScanTypeOverride` on each `CameraQrScannerService`. Toolbar toggle writes all per-camera preferences then syncs the observable `CameraSlots`. Bug fixed: toggle was writing to global preference but `UpdateScanTypes` reads per-camera keys. All phases complete.

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | Stored Per Camera | `MultiCamera.{n}.ScanType` preference |
| AC2 | Loaded at Init | `SetScanTypeOverride` called per service in `InitializeAsync` |
| AC3 | Toolbar Toggle All | Writes all per-camera prefs + `UpdateScanTypes` + syncs `CameraSlots` |
| AC4 | Badge Per Card | Teal ENTRY / red EXIT badge on each status card |
| AC5 | Correct Submission | Scan type submitted to server matches camera's override |

---

## Implementation Phases

### Phase 1: CameraQrScannerService Extension ✅
- [x] `SetScanTypeOverride(string? scanType)` — stored in `_scanTypeOverride`; used in `ProcessQrCodeAsync` instead of `_preferences.GetDefaultScanType()` when set

### Phase 2: MultiCameraManager.UpdateScanTypes ✅
- [x] Reads `_preferences.GetCameraScanType(cam.Index)` for each camera
- [x] Calls `service.SetScanTypeOverride(scanType)`
- [x] Called at `InitializeAsync` time and on toolbar toggle

### Phase 3: ToggleScanType Bug Fix ✅
- [x] `MainViewModel.ToggleScanType` — writes `_preferences.SetCameraScanType(i, CurrentScanType)` for all cameras before `UpdateScanTypes()`
- [x] Syncs `CameraSlots[cam.Index].ScanType` from `_multiCameraManager.Cameras` after update

### Phase 4: CameraSlotState Display ✅
- [x] `ScanTypeBadgeColor` — `#F44336` for EXIT, `#4D9B91` for ENTRY
- [x] `OnScanTypeChanged` partial — `OnPropertyChanged(nameof(ScanTypeBadgeColor))`

---

## Edge Case Handling

| # | Edge Case | Handling Strategy | Phase |
|---|-----------|-------------------|-------|
| 1 | Toggle before cameras initialised | Per-camera prefs written; picked up at `InitializeAsync` | Phase 3 |
| 2 | No scan type preference for camera | `GetCameraScanType` defaults to "ENTRY" | Phase 2 |
| 3 | `UpdateScanTypes` on empty `_cameras` | `foreach` no-op | Phase 2 |

**Coverage:** 3/3

---

## Definition of Done

- [x] All acceptance criteria implemented
- [x] Scan type bug fixed and verified
- [x] Build succeeds (0 errors)
