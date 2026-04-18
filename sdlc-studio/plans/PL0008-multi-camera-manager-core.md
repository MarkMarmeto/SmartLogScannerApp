# PL0008: Multi-Camera Manager Core — Implementation Plan

> **Status:** Complete
> **Story:** [US0066: Multi-Camera Manager Core](../stories/US0066-multi-camera-manager-core.md)
> **Epic:** EP0011: Multi-Camera Scanning
> **Created:** 2026-04-18
> **Language:** C# / .NET MAUI 8

## Overview

Introduces `MultiCameraManager` as an orchestration layer over N `CameraQrScannerService` instances, plus `ICameraWorker`/`ICameraWorkerFactory` for headless platform camera capture. Cross-camera dedup is automatic via shared `IScanDeduplicationService` singleton. All phases complete.

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | Camera Lifecycle | Start/stop/restart; offline cameras marked immediately |
| AC2 | Cross-Camera Dedup | Shared `IScanDeduplicationService` suppresses duplicates |
| AC3 | Scan Routing | `ProcessQrCodeAsync(index, payload)` routes to correct service |
| AC4 | Max 8 | `InitializeAsync` throws `ArgumentException` if count > 8 |
| AC5 | Manual Stop | `StopCameraAsync` sets `IsEnabled=false`; no auto-recovery |
| AC6 | Camera Attribution | `cameraIndex` passed to `SubmitScanAsync` |

---

## Technical Context

### Language & Framework
- **Primary Language:** C# 12 / .NET 8
- **Framework:** .NET MAUI + AVFoundation (Mac) / MediaCapture (Windows)
- **Test Framework:** xUnit + Moq (targets `net8.0`)

### Existing Patterns
- `IQrScannerService` event model (`ScanCompleted`, `ScanUpdated`)
- `ActivatorUtilities.CreateInstance` for DI-resolved service creation
- `Dictionary<int, T>` keyed by camera index for O(1) routing

---

## Implementation Phases

### Phase 1: Models ✅
- [x] `SmartLog.Scanner.Core/Models/CameraStatus.cs` — `Idle, Scanning, Error, Offline` enum
- [x] `SmartLog.Scanner.Core/Models/CameraInstance.cs` — runtime state per camera

### Phase 2: ICameraWorker Abstraction ✅
- [x] `SmartLog.Scanner.Core/Services/ICameraWorker.cs` — `StartAsync`, `StopAsync`, `QrCodeDetected`, `ErrorOccurred`
- [x] `SmartLog.Scanner.Core/Services/ICameraWorkerFactory.cs` — `Create()` factory

### Phase 3: IMultiCameraManager + MultiCameraManager ✅
- [x] `SmartLog.Scanner.Core/Services/IMultiCameraManager.cs`
- [x] `SmartLog.Scanner.Core/Services/MultiCameraManager.cs`
  - `InitializeAsync` — creates services + workers, enforces max 8
  - `StartAllAsync` / `StopAllAsync`
  - `ProcessQrCodeAsync` — routes to service by index
  - `UpdateThrottleValues`, `UpdateScanTypes`, `ConfigureCameraPreview`

### Phase 4: CameraQrScannerService Extensions ✅
- [x] `SetCameraIndex(int? index)` — stored, passed to `SubmitScanAsync`
- [x] `SetScanTypeOverride(string? scanType)` — per-camera type override

### Phase 5: Preferences Keys ✅
- [x] `IPreferencesService` — `GetCameraCount/SetCameraCount`, `GetCameraName/Set`, `GetCameraDeviceId/Set`, `GetCameraScanType/Set`, `GetCameraEnabled/Set`
- [x] `PreferencesService` — implemented with `MultiCamera.{n}.*` keys

### Phase 6: Platform Workers ✅
- [x] `Platforms/MacCatalyst/CameraHeadlessWorker.cs` — `AVCaptureSession` + `AVCaptureMetadataOutput`, no UIView
- [x] `Platforms/MacCatalyst/CameraWorkerFactory.cs`
- [x] `Platforms/Windows/CameraHeadlessWorker.cs` — wraps `WindowsCameraScanner`
- [x] `Platforms/Windows/CameraWorkerFactory.cs`

### Phase 7: DI Registration ✅
- [x] `MauiProgram.cs` — `ICameraWorkerFactory`, `IMultiCameraManager`, `AdaptiveDecodeThrottle` registered

---

## Edge Case Handling

| # | Edge Case | Handling Strategy | Phase |
|---|-----------|-------------------|-------|
| 1 | Count > 8 | `ArgumentException` in `InitializeAsync` | Phase 3 |
| 2 | Empty device ID | Camera marked `Offline`; warning logged | Phase 3 |
| 3 | Unknown camera index in `ProcessQrCodeAsync` | Warning logged; no-op | Phase 3 |
| 4 | Exception during QR processing | Caught; `HandleCameraErrorAsync` called | Phase 3 |
| 5 | `StartAllAsync` called twice | `worker.IsRunning` guard; no-op | Phase 6 |

**Coverage:** 5/5

---

## Definition of Done

- [x] All acceptance criteria implemented
- [x] Unit tests written (`MultiCameraManagerTests.cs`)
- [x] Edge cases handled
- [x] Build succeeds (0 errors)
