# PL0007: Multi-Camera Setup Configuration — Implementation Plan

> **Status:** Complete
> **Story:** [US0071: Multi-Camera Setup Configuration](../stories/US0071-multi-camera-setup-configuration.md)
> **Epic:** EP0011: Multi-Camera Scanning
> **Created:** 2026-04-18
> **Language:** C# / .NET MAUI 8

## Overview

Replaces the single camera picker on the Setup page with a full multi-camera configuration section. Admin can set camera count (1–8), configure each slot (display name, device, scan type, enable), test connectivity, and save. All configuration persists to MAUI Preferences and is restored on re-open.

**All phases are complete** — implemented during EP0011 session (2026-04-18).

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | Camera Count Selector | Stepper 1–8, adds/removes per-camera rows dynamically |
| AC2 | Per-Camera Config Row | Name, device picker, scan type picker, enable switch per slot |
| AC3 | Camera Test Button | Calls `TestCameraAsync`, shows result, sets `IsConnected` |
| AC4 | USB 3.0 Warning | Banner visible when CameraCount ≥ 3 |
| AC5 | Save and Restore | All values persisted and restored from `IPreferencesService` |
| AC6 | Backward Compatibility | Defaults to 1 camera; camera 0 falls back to legacy `SelectedCameraId` |

---

## Technical Context

### Language & Framework
- **Primary Language:** C# 12 / .NET 8
- **Framework:** .NET MAUI (Windows + macOS)
- **MVVM:** CommunityToolkit.Mvvm
- **Test Framework:** xUnit + Moq (targets `net8.0`)

### Existing Patterns
- `ObservableObject` + `[ObservableProperty]` for all VM state
- `IPreferencesService` for key/value persistence
- `ICameraEnumerationService` (platform-specific) for device listing and testing
- `CollectionView` with `DataTemplate` for per-camera rows
- Null-safe optional service injection (`ICameraEnumerationService?`)

---

## Recommended Approach

**Strategy:** Test-After
**Rationale:** UI-heavy story with platform-specific device enumeration. Logic is straightforward (collection management, preference reads/writes); Test-After is appropriate.

---

## Implementation Phases

### Phase 1: Core ViewModel — CameraSlotViewModel ✅
**Goal:** Observable state for one camera config row; testable without MAUI runtime.

- [x] Create `SmartLog.Scanner.Core/ViewModels/CameraSlotViewModel.cs`
- [x] Properties: `Index`, `DisplayNumber`, `DisplayName`, `SelectedDevice`, `ScanType`, `IsEnabled`, `IsConnected`, `IsTestRunning`, `TestResult`
- [x] `AvailableDevices` observable collection + `PopulateDevices()` helper
- [x] `TestCameraCommand` → calls `ICameraEnumerationService.TestCameraAsync`, handles null service gracefully

**Files:**
- `SmartLog.Scanner.Core/ViewModels/CameraSlotViewModel.cs` — new

### Phase 2: SetupViewModel — Multi-Camera Section ✅
**Goal:** Integrate camera count + slot collection into existing SetupViewModel.

- [x] Add `[ObservableProperty] int _cameraCount = 1` with `[NotifyPropertyChangedFor(ShowUsb3Warning)]`
- [x] Add `ObservableCollection<CameraSlotViewModel> CameraSlots`
- [x] `ShowUsb3Warning` computed property (CameraCount ≥ 3)
- [x] `OnCameraCountChanged` — adds/removes slots, re-populates devices
- [x] `LoadMultiCameraConfig()` — restores from preferences on Initialize
- [x] `SaveMultiCameraConfig()` — persists on Save
- [x] `_allAvailableCameras` cache for device population across slot add events

**Files:**
- `SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs` — modified

### Phase 3: Platform Service — TestCameraAsync ✅
**Goal:** Implement `TestCameraAsync` on both platforms.

- [x] Mac Catalyst: `AVCaptureDevice.DeviceWithUniqueID(deviceId)` — returns `!device.Suspended`
- [x] Windows: `MediaCapture` open attempt with timeout
- [x] Add `TestCameraAsync(string deviceId)` to `ICameraEnumerationService` interface

**Files:**
- `SmartLog.Scanner.Core/Services/ICameraEnumerationService.cs` — `TestCameraAsync` added
- `SmartLog.Scanner/Platforms/MacCatalyst/CameraEnumerationService.cs` — implemented
- `SmartLog.Scanner/Platforms/Windows/CameraEnumerationService.cs` — implemented

### Phase 4: SetupPage XAML ✅
**Goal:** Multi-camera card replaces single camera picker.

- [x] Camera Configuration card section (below scanner mode)
- [x] `Stepper` bound to `CameraCount` + count label
- [x] USB 3.0 warning `Border` bound to `ShowUsb3Warning`
- [x] `CollectionView` bound to `CameraSlots` with `CameraSlotViewModel` DataTemplate:
  - Camera header row (number label + enable `Switch`)
  - Display name `Entry`
  - Device `Picker` bound to `AvailableDevices`
  - Scan type `Picker` + Test `Button` in `HorizontalStackLayout`
  - Test result `Label` (visible when non-null)

**Files:**
- `SmartLog.Scanner/Views/SetupPage.xaml` — multi-camera section added

### Phase 5: Verification ✅
**Goal:** Confirm all AC met.

| AC | Verification | File Evidence |
|----|-------------|---------------|
| AC1 | `OnCameraCountChanged` clamps and manages slot collection | `SetupViewModel.cs:361` |
| AC2 | `CameraSlotViewModel` properties TwoWay-bound in DataTemplate | `SetupPage.xaml:401` |
| AC3 | `TestCameraCommand` with null-safe service handling | `CameraSlotViewModel.cs:54` |
| AC4 | `ShowUsb3Warning` computed from `CameraCount` | `SetupViewModel.cs:359` |
| AC5 | `LoadMultiCameraConfig` / `SaveMultiCameraConfig` | `SetupViewModel.cs:407,416` |
| AC6 | Legacy `SelectedCameraId` fallback in `BuildCameraConfigs` | `MainViewModel.cs:243` |

---

## Edge Case Handling

| # | Edge Case | Handling Strategy | Phase |
|---|-----------|-------------------|-------|
| 1 | No USB cameras detected | Empty `AvailableDevices`; Test shows "No device selected" | Phase 1 |
| 2 | Count reduced below active slots | Slots trimmed from end of collection | Phase 2 |
| 3 | `TestCameraAsync` throws | Caught in `CameraSlotViewModel`; TestResult shows ex.Message | Phase 1 |
| 4 | Saved DeviceId missing at restart | `SelectedDevice` stays null; admin re-selects | Phase 2 |
| 5 | No platform `ICameraEnumerationService` | Null-safe; shows "not available on this platform" | Phase 1 |

**Coverage:** 5/5 edge cases handled

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `CollectionView` in MAUI may have binding quirks with `Picker` inside DataTemplate | Medium | Tested on Mac Catalyst; bindings work with `x:DataType` |

---

## Definition of Done

- [x] All acceptance criteria implemented
- [x] Edge cases handled
- [x] Code follows existing MVVM patterns
- [x] No build errors

---

## Notes

PL0006 covers the full EP0011 epic plan. PL0007 is the story-level plan for US0071 specifically, created retrospectively after implementation to complete the SDLC trail.
