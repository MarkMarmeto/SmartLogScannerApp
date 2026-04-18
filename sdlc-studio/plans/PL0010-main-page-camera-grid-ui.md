# PL0010: Main Page Camera Grid UI — Implementation Plan

> **Status:** Complete
> **Story:** [US0068: Main Page Camera Grid UI](../stories/US0068-main-page-camera-grid-ui.md)
> **Epic:** EP0011: Multi-Camera Scanning
> **Created:** 2026-04-18
> **Language:** C# / .NET MAUI 8

## Overview

Replaces the single `CameraQrView` with a two-column layout: one `CameraPreviewView` (camera 0 live preview) on the left, and a `FlexLayout` of pure-XAML status cards on the right. Eliminates multiple native preview layers from the MAUI view hierarchy — the root cause of Mac Catalyst window transparency. All phases complete.

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | Camera 0 Preview | Single `AVCaptureVideoPreviewLayer` attached post-init |
| AC2 | Status Card Grid | N cards in `FlexLayout`; name, badge, status, frame rate |
| AC3 | Shared Result Panel | Bottom panel updates from any camera scan |
| AC4 | Flash Animation | 1.5 s flash per slot; per-slot `CancellationTokenSource` |
| AC5 | USB Mode Hidden | Camera row `IsVisible=false` when `IsCameraMode=false` |
| AC6 | Hidden Unused Slots | Slots beyond camera count have `IsVisible=false` |

---

## Implementation Phases

### Phase 1: CameraSlotState ✅
- [x] `SmartLog.Scanner/ViewModels/CameraSlotState.cs`
  - Observable: `DisplayName`, `ScanType`, `Status`, `ErrorMessage`, `ShowFlash`, `FlashStudentName`, `FrameRateDisplay`, `CameraDeviceId`, `IsVisible`
  - Computed: `StatusText`, `ScanTypeBadgeColor`, `CanRestart`, `StatusBrush`
  - `IncrementFrameCount()`, `UpdateFrameRate()`
  - `RestartCommand` with `Func<int, Task>` callback

### Phase 2: CameraPreviewView + Handler ✅
- [x] `SmartLog.Scanner/Controls/CameraPreviewView.cs` — plain `View` subclass
- [x] `SmartLog.Scanner/Platforms/MacCatalyst/CameraPreviewHandler.cs` — creates `UIView`, exposes `AttachWorkerPreview(CameraHeadlessWorker)`
- [x] Handler registered in `MauiProgram.cs`

### Phase 3: MainViewModel Updates ✅
- [x] `CameraSlots` initialised with 8 `CameraSlotState` instances (constructor)
- [x] `IsCameraMode` computed from `_scannerMode`
- [x] `OnMultiCameraScanCompleted` → flash + shared result panel update
- [x] `OnMultiCameraStatusChanged` → `CameraSlots[n].Status` update (main thread)
- [x] `TriggerSlotFlash` with per-slot `CancellationTokenSource`
- [x] `_frameRateTimer` 1-s timer calling `UpdateFrameRate()` on all slots
- [x] `ConfigureCameraPreview(int, Action<ICameraWorker>)` passthrough

### Phase 4: MainPage.xaml ✅
- [x] Row 1: `Grid` with `ColumnDefinitions="320,*"`
  - Col 0: `Border` + `CameraPreviewView x:Name="CameraPreview0"`
  - Col 1: `FlexLayout` + `BindableLayout` over `CameraSlots`
- [x] DataTemplate: name/badge, StatusText, FlashStudentName, RestartCommand button

### Phase 5: MainPage.xaml.cs ✅
- [x] `OnAppearing` calls `AttachCameraPreview()` after `InitializeAsync`
- [x] `#if MACCATALYST AttachCameraPreview()` — resolves handler, calls `AttachWorkerPreview`

---

## Edge Case Handling

| # | Edge Case | Handling Strategy | Phase |
|---|-----------|-------------------|-------|
| 1 | Camera 0 no device | Black preview background; Offline card | Phase 3 |
| 2 | USB mode | `IsCameraMode=false` hides camera row | Phase 3 |
| 3 | Preview handler not ready | Worker starts headless; preview attached after handler ready | Phase 5 |
| 4 | Rapid flash calls | Previous `CancellationTokenSource` cancelled | Phase 3 |
| 5 | Status event on background thread | `MainThread.BeginInvokeOnMainThread` in all handlers | Phase 3 |

**Coverage:** 5/5

---

## Definition of Done

- [x] All acceptance criteria implemented
- [x] Build succeeds (0 errors)
- [x] Mac window no longer transparent (root cause fixed)
