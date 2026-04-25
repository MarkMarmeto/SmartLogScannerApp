# PL0006: EP0011 Multi-Camera Scanning — Implementation Plan

> **Status:** Complete
> **Epic:** EP0011: Multi-Camera Scanning
> **Stories:** US0066, US0067, US0068, US0069, US0070, US0071
> **Created:** 2026-04-18
> **Language:** C# / .NET MAUI 8
> **Project:** SmartLogScannerApp

---

## Overview

Add configurable 1–8 simultaneous USB camera scanning to the SmartLog Scanner App. Currently the app supports a single camera. The implementation adds a `MultiCameraManager` service, adaptive decode throttle, a grid-based main page UI, per-camera scan type, error isolation, and a redesigned setup page.

Also adds **camera-level scan attribution**: a nullable `CameraIndex` field is added to the `Scan` entity (WebApp) and the `POST /api/v1/scans` request body, so each scan record knows which physical camera gate captured it.

**Backward-compatible:** Camera count = 1 must behave identically to the current single-camera mode. Single-camera scans submit `cameraIndex: null`.

---

## Acceptance Criteria Summary

| Story | AC | Description |
|-------|----|-------------|
| US0066 | AC1–AC6 | Camera lifecycle (start/stop/restart), cross-camera dedup, scan routing, max 8 limit |
| US0067 | AC1–AC6 | Adaptive throttle by camera count, dynamic recalculation, 320×240 decode resolution |
| US0068 | AC1–AC6 | Responsive grid (1→full, 2→2×1, 3-4→2×2, 5-6→3×2, 7-8→4×2), shared result panel |
| US0069 | AC1–AC5 | Per-camera ENTRY/EXIT toggle, persisted, correct scan type submitted |
| US0070 | AC1–AC6 | Error isolation, auto-recovery (3×10s), frame rate indicator, USB 3.0 warning, manual restart |
| US0071 | AC1–AC6 | Camera count selector (1-8), per-camera config rows, save/restore, backward compat |

---

## Technical Context

### Language & Framework
- **Language:** C# 12 / .NET 8
- **Framework:** .NET MAUI (Windows + macOS)
- **MVVM:** CommunityToolkit.Mvvm (`ObservableObject`, `RelayCommand`, `ObservableProperty`)
- **Test Framework:** xUnit + Moq (targets `net8.0`, not MAUI TFM)

### Key Existing Patterns

| Pattern | Where Used | How to Follow |
|---------|-----------|---------------|
| `ObservableObject` + `[ObservableProperty]` | All ViewModels | Source-generated properties only |
| `IQrScannerService` event model | `CameraQrScannerService` | `ScanCompleted` + `ScanUpdated` events |
| `IScanDeduplicationService` (shared singleton) | `CameraQrScannerService` | Re-use same instance across cameras → automatic cross-camera dedup |
| `IPreferencesService` key/value | `PreferencesService` | Add new keys following naming convention |
| `CameraQrView` control | `MainPage.xaml` | `SelectedCameraId` + `BarcodeDetected` event + `IsDetecting` flag |
| Platform handler pattern | `Platforms/Windows/CameraQrViewHandler.cs` | No changes needed; handler already routes to service |

### Critical Design Decision: How `MultiCameraManager` Relates to `CameraQrScannerService`

The existing `CameraQrScannerService.ProcessQrCodeAsync(payload)` already handles:
- Raw debounce
- HMAC validation
- Student-level deduplication (via shared `IScanDeduplicationService` singleton)
- Optimistic acceptance + server submission
- Offline queuing fallback

**The manager does NOT duplicate this logic.** Instead it:
1. Creates N instances of `CameraQrScannerService`, all sharing the same `IScanDeduplicationService` singleton — this gives cross-camera dedup for free
2. Each instance is wired to one `CameraQrView` in the UI
3. Manages camera health, throttle, and routing

### Adaptive Throttle Implementation

The `CameraQrView` → platform handler already receives all frames. Throttle is implemented in a new `AdaptiveDecodeThrottle` class that returns a frame-skip count. The platform handlers check a frame counter and only pass the payload to `BarcodeDetected` for every N-th frame.

**However:** modifying platform handlers (`CameraQrViewHandler.cs` on Windows/Mac) requires adding a `FrameThrottle` bindable property to `CameraQrView`. The `CameraQrScannerService` 500ms raw debounce already handles accidental double-fires, so throttle is a performance optimisation, not a correctness requirement.

> **Simplification:** For Phase 1, implement throttle as a property on `CameraQrView` that the platform handler reads before forwarding barcode events. The `AdaptiveDecodeThrottle` service calculates the skip value; `MultiCameraManager` pushes the value to each view.

### Multi-Camera Config Persistence

New keys in `IPreferencesService`:
```
MultiCamera.Count           → int (1-8), default 1
MultiCamera.{n}.Name        → string, e.g., "Gate A"
MultiCamera.{n}.DeviceId    → string
MultiCamera.{n}.ScanType    → "ENTRY" | "EXIT"
MultiCamera.{n}.Enabled     → bool, default true
```

### Camera Attribution: How `CameraIndex` Flows End-to-End

```
CameraQrView (index=2)
    └─ BarcodeDetected event
           └─ MainPage.xaml.cs: OnBarcodeDetected → VM.OnBarcodeFromCamera(cameraIndex=2, payload)
                  └─ MultiCameraManager.ProcessQrCodeAsync(cameraIndex=2, payload)
                         └─ CameraQrScannerService[2].ProcessQrCodeAsync(payload)
                                └─ ScanApiService.SubmitScanAsync(payload, scannedAt, scanType, cameraIndex=2)
                                       └─ POST /api/v1/scans { ..., cameraIndex: 2 }
                                              └─ ScansApiController → Scan.CameraIndex = 2 → saved to DB
```

**Single-camera mode** (index=0): `cameraIndex=0` is submitted; server stores 0. Old scanner builds that don't send `cameraIndex` at all receive `null` on the server (nullable field, no migration data loss).

### Duplicate Detection Summary

| Layer | Mechanism | Window | Scope |
|-------|-----------|--------|-------|
| Scanner raw debounce | `CameraQrScannerService._lastPayload` | 500ms | Per camera instance |
| Scanner post-scan lockout | `CameraQrScannerService._lastProcessedPayload` | 3s | Per camera instance |
| Scanner student-level dedup | `ScanDeduplicationService` (shared singleton) | 0–2s suppress / 2–5s warn | **Cross-camera** (all cameras share same instance) |
| Server duplicate check | `CheckDuplicateScanAsync` in `ScansApiController` | 5 min (configurable) | Per device + student + scan type |

Cross-camera dedup is automatic because all `CameraQrScannerService` instances share the same `IScanDeduplicationService` singleton — keyed on `{studentId}:{scanType}`, not camera index.

---

## Implementation Phases

### Phase 0: WebApp — Camera Attribution (cross-cutting, both repos)
**Goal:** Server stores which camera captured each scan. Must be done before scanner changes so the API is ready.

#### 0.1 — Add `CameraIndex` to `Scan` entity
**File:** `SmartLogWebApp/src/SmartLog.Web/Data/Entities/Scan.cs`

```csharp
/// <summary>
/// Index of the camera that captured this scan (0-based).
/// Null for scans from single-camera devices or older scanner versions.
/// </summary>
public int? CameraIndex { get; set; }
```

#### 0.2 — Add `CameraIndex` to `ScanSubmissionRequest`
**File:** `SmartLogWebApp/src/SmartLog.Web/Controllers/Api/ScansApiController.cs`

```csharp
public class ScanSubmissionRequest
{
    // ... existing fields ...
    public int? CameraIndex { get; set; }   // null = single-camera device
}
```

Save it in `SubmitScan`:
```csharp
var scan = new Scan
{
    // ... existing fields ...
    CameraIndex = request.CameraIndex
};
```

#### 0.3 — Create EF Core migration
```
dotnet ef migrations add AddCameraIndexToScan -p src/SmartLog.Web
```

Migration: `AddColumn<int?>(name: "CameraIndex", table: "Scans", nullable: true)`

#### 0.4 — Update `IScanApiService` interface and `ScanApiService` implementation
**Files:**
- `SmartLogScannerApp/SmartLog.Scanner.Core/Services/IScanApiService.cs` ← **must be updated first**
- `SmartLogScannerApp/SmartLog.Scanner.Core/Services/ScanApiService.cs`

Update the interface signature:
```csharp
Task<ScanResult> SubmitScanAsync(
    string qrPayload,
    DateTimeOffset scannedAt,
    string scanType,
    int? cameraIndex = null,       // NEW
    CancellationToken cancellationToken = default);
```

Then update the implementation to match and include in request body:
```csharp
var requestBody = new { qrPayload, scannedAt = ..., scanType, cameraIndex };
```

> **Why interface first:** `CameraQrScannerService` calls `_scanApi.SubmitScanAsync()` through the `IScanApiService` abstraction. Updating only the concrete class won't compile.

#### 0.5 — Thread `cameraIndex` through `CameraQrScannerService`
**File:** `SmartLogScannerApp/SmartLog.Scanner.Core/Services/CameraQrScannerService.cs`

- `ProcessQrCodeAsync` already exists; needs `cameraIndex` passed to `_scanApi.SubmitScanAsync()`
- The `MultiCameraManager` sets each service's index via a property or constructor param (same step as 1.5)

---

### Phase 1: Core Models & Services (US0066 + US0067)
**Goal:** `MultiCameraManager` + `AdaptiveDecodeThrottle` fully testable without UI.

#### 1.1 — New Model: `CameraStatus` enum
**File:** `SmartLog.Scanner.Core/Models/CameraStatus.cs`
```csharp
public enum CameraStatus { Idle, Scanning, Error, Offline }
```

#### 1.2 — New Model: `CameraInstance`
**File:** `SmartLog.Scanner.Core/Models/CameraInstance.cs`
```csharp
public class CameraInstance
{
    public int Index { get; set; }           // 0-based
    public string CameraDeviceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ScanType { get; set; } = "ENTRY";
    public bool IsEnabled { get; set; } = true;
    public CameraStatus Status { get; set; } = CameraStatus.Idle;
    public string? ErrorMessage { get; set; }
    public int DecodeThrottleFrames { get; set; } = 5;  // every N-th frame
    public double FrameRate { get; set; }
    public DateTime? LastDecodeAt { get; set; }
    public int ReconnectAttempts { get; set; }
}
```

#### 1.3 — New Service: `AdaptiveDecodeThrottle`
**File:** `SmartLog.Scanner.Core/Services/AdaptiveDecodeThrottle.cs`

Stateless static/instance class. `Calculate(int activeCameraCount) → int frameSkip`:
```
1 camera   → 5
2 cameras  → 5
3-4        → 8
5 cameras  → 10
6 cameras  → 12
7 cameras  → 13
8 cameras  → 15
```

Returns minimum 3 for slow cameras.

#### 1.4 — New Interface + Service: `IMultiCameraManager` / `MultiCameraManager`
**Files:**
- `SmartLog.Scanner.Core/Services/IMultiCameraManager.cs`
- `SmartLog.Scanner.Core/Services/MultiCameraManager.cs`

```csharp
public interface IMultiCameraManager
{
    IReadOnlyList<CameraInstance> Cameras { get; }
    event EventHandler<(int CameraIndex, ScanResult Result)>? ScanCompleted;
    event EventHandler<(int CameraIndex, ScanResult Result)>? ScanUpdated;
    event EventHandler<(int CameraIndex, CameraStatus Status)>? CameraStatusChanged;

    Task InitializeAsync(IReadOnlyList<CameraInstance> cameras);
    Task StartAllAsync();
    Task StopAllAsync();
    Task StopCameraAsync(int cameraIndex);
    Task RestartCameraAsync(int cameraIndex);
    Task ProcessQrCodeAsync(int cameraIndex, string payload);
    void UpdateThrottleValues();
    void UpdateScanTypes();     // reads per-camera prefs, calls SetScanTypeOverride on each running service
}
```

**`MultiCameraManager` implementation:**
- Holds `Dictionary<int, CameraQrScannerService>` — one per camera index
- Each service is constructed with the shared `IScanDeduplicationService`
- `ProcessQrCodeAsync(index, payload)` delegates to `_services[index].ProcessQrCodeAsync(payload)` — cross-camera dedup is automatic since all services share the same dedup instance
- `StopCameraAsync(index)` → `_services[index].StopAsync()`, sets `Cameras[index].Status = CameraStatus.Idle`, sets `Cameras[index].IsEnabled = false` (manual stop — blocks auto-recovery)
- `RestartCameraAsync(index)` → re-enable (`IsEnabled = true`), reset `ReconnectAttempts = 0`, reset status, call `StartAsync()`
- `UpdateThrottleValues()` → recalculate with `AdaptiveDecodeThrottle.Calculate(activeCameraCount)`, set on each `CameraInstance.DecodeThrottleFrames`
- Max 8 cameras enforced in `InitializeAsync()`

**`InitializeAsync` behavior when configured count exceeds physical devices:**
- For each configured `CameraInstance`, attempt to enumerate the device by `CameraDeviceId`
- If device not found at startup: set `Status = CameraStatus.Offline` immediately (do not throw)
- Log warning: "Camera {n} device '{id}' not found at startup — marked Offline"
- Other cameras still initialize normally
- UI shows offline cameras with "Device not found" message and a Restart button

#### 1.5 — Extend `CameraQrScannerService` with scan type override
**File:** `SmartLog.Scanner.Core/Services/CameraQrScannerService.cs`

Add method: `SetScanTypeOverride(string? scanType)`. When set, uses the override instead of `_preferences.GetDefaultScanType()`. The `MultiCameraManager` calls this after creating each instance.

**Propagating scan type changes mid-session:**
- `SetupViewModel.SaveAsync()` must call `IMultiCameraManager.UpdateScanTypes()` after persisting preferences
- Add `UpdateScanTypes()` to `IMultiCameraManager` interface:
  ```csharp
  void UpdateScanTypes();  // reads per-camera prefs and calls SetScanTypeOverride on each running service
  ```
- Changes take effect on the next scan (no restart required)
- If `MultiCameraManager` is not yet initialized (save called from Setup before scanning starts), `UpdateScanTypes()` is a no-op — scan types are loaded from preferences at `InitializeAsync` time anyway

#### 1.6 — Extend `IPreferencesService` + `PreferencesService`
**Files:**
- `SmartLog.Scanner.Core/Services/IPreferencesService.cs`
- `SmartLog.Scanner.Core/Services/PreferencesService.cs`

Add multi-camera config methods:
```csharp
int GetCameraCount();                        // default 1
void SetCameraCount(int count);
string GetCameraName(int index);             // default "Camera {n+1}"
void SetCameraName(int index, string name);
string GetCameraDeviceId(int index);         // default ""
void SetCameraDeviceId(int index, string id);
string GetCameraScanType(int index);         // default "ENTRY"
void SetCameraScanType(int index, string scanType);
bool GetCameraEnabled(int index);            // default true
void SetCameraEnabled(int index, bool enabled);
```

Keys: `$"MultiCamera.{index}.Name"`, etc.

---

### Phase 2: Setup Page (US0069 + US0071)
**Goal:** Admin can configure 1-8 cameras, assign devices, set scan types. Persists to preferences.

#### 2.0 — Add `TestCameraAsync` command to `SetupViewModel`
**File:** `SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs`

The "Test" button per camera row needs an implementation:
```csharp
[RelayCommand]
private async Task TestCameraAsync(int cameraIndex)
{
    var slot = CameraSlots[cameraIndex];
    slot.IsTestRunning = true;
    slot.TestResult = null;

    // Attempt to open the camera briefly via IDeviceDetectionService
    var result = await _deviceDetection.TestCameraAsync(slot.SelectedDevice?.DeviceId);
    slot.TestResult = result.Success ? "Camera OK" : $"Test failed: {result.Error}";
    slot.IsConnected = result.Success;
    slot.IsTestRunning = false;
}
```

Add to `CameraSlotViewModel`:
```csharp
[ObservableProperty] bool _isTestRunning;
[ObservableProperty] string? _testResult;
```

`IDeviceDetectionService.TestCameraAsync(string? deviceId)` — new method that opens the camera for ~1 second and returns success/failure. The platform implementation attempts to open the device and immediately releases it.

#### 2.1 — New ViewModel: `CameraSlotViewModel`
**File:** `SmartLog.Scanner.Core/ViewModels/CameraSlotViewModel.cs`

```csharp
public partial class CameraSlotViewModel : ObservableObject
{
    public int Index { get; }             // 0-based
    public int DisplayNumber => Index + 1;
    [ObservableProperty] string _displayName;
    [ObservableProperty] CameraDeviceInfo? _selectedDevice;
    [ObservableProperty] string _scanType = "ENTRY";
    [ObservableProperty] bool _isEnabled = true;
    [ObservableProperty] bool _isConnected;
    public ObservableCollection<CameraDeviceInfo> AvailableDevices { get; }
    public List<string> ScanTypeOptions { get; } = new() { "ENTRY", "EXIT" };
}
```

#### 2.2 — Update `SetupViewModel`
**File:** `SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs`

- Add `[ObservableProperty] int _cameraCount = 1;`
- Add `ObservableCollection<CameraSlotViewModel> CameraSlots`
- `OnCameraCountChanged(int value)` → add/remove slots, show confirmation if removing active cameras
- `LoadCamerasAsync()` populates each slot's `AvailableDevices`
- `SaveAsync()` persists camera count + per-slot config
- USB 3.0 warning: `bool ShowUsb3Warning => CameraCount >= 3;`

#### 2.3 — Update `SetupPage.xaml`
**File:** `SmartLog.Scanner/Views/SetupPage.xaml`

- Replace single camera picker with:
  - `Stepper` or `Picker` for camera count (1-8)
  - `CollectionView` bound to `CameraSlots` with a `CameraSlotTemplate`:
    - Camera number label
    - Display name `Entry`
    - Device `Picker` bound to `AvailableDevices`
    - Scan type `Picker` ("ENTRY" / "EXIT")
    - Enable `Switch`
  - USB 3.0 warning `Label` (visible when `ShowUsb3Warning`)

---

### Phase 3: Main Page Grid UI (US0068 + US0070)
**Goal:** Camera grid with live previews, per-cell status, shared result panel, error isolation.

#### 3.1 — New ViewModel class: `CameraSlotState`
**File:** `SmartLog.Scanner/ViewModels/CameraSlotState.cs`

```csharp
public partial class CameraSlotState : ObservableObject
{
    public int Index { get; }
    [ObservableProperty] string _displayName;
    [ObservableProperty] string _scanType = "ENTRY";
    [ObservableProperty] CameraStatus _status = CameraStatus.Idle;
    [ObservableProperty] string? _errorMessage;
    [ObservableProperty] bool _showFlash;
    [ObservableProperty] string? _flashStudentName;
    [ObservableProperty] string _frameRateDisplay = "—";
    [ObservableProperty] string _cameraDeviceId = string.Empty;
    [ObservableProperty] int _throttleFrames = 5;
}
```

#### 3.2 — Update `MainViewModel`
**File:** `SmartLog.Scanner/ViewModels/MainViewModel.cs`

Replace single `CameraQrScannerService` usage with `IMultiCameraManager`:

- `ObservableCollection<CameraSlotState> CameraSlots`
- `int GridColumns` — computed from camera count: 1→1, 2→2, 3-4→2, 5-6→3, 7-8→4
- `int GridRows` — computed similarly
- Shared result panel properties (already exist as `LastStudentName`, `LastGrade`, etc.) — add `LastScanCameraName`
- Subscribe to `MultiCameraManager.ScanCompleted` + `ScanUpdated` + `CameraStatusChanged`

**Thread safety — ALL event handlers must dispatch to the main thread:**
```csharp
_manager.ScanCompleted += (_, e) => MainThread.BeginInvokeOnMainThread(() => OnScanCompleted(e));
_manager.CameraStatusChanged += (_, e) => MainThread.BeginInvokeOnMainThread(() => OnCameraStatusChanged(e));
_manager.ScanUpdated += (_, e) => MainThread.BeginInvokeOnMainThread(() => OnScanUpdated(e));
```
Events from `MultiCameraManager` fire on camera decode threads. All `ObservableCollection` mutations and `[ObservableProperty]` writes bound to the UI must happen on the main thread.

**`ScanCompleted` handler — flash timer with leak prevention:**
```csharp
private readonly Dictionary<int, CancellationTokenSource> _flashTimers = new();

private async void OnScanCompleted((int CameraIndex, ScanResult Result) e)
{
    var slot = CameraSlots[e.CameraIndex];
    slot.ShowFlash = true;
    slot.FlashStudentName = e.Result.StudentName;
    // Update shared result panel
    LastStudentName = e.Result.StudentName;
    LastScanCameraName = slot.DisplayName;

    // Cancel previous flash timer for this camera slot (prevents premature hide)
    if (_flashTimers.TryGetValue(e.CameraIndex, out var prev)) prev.Cancel();
    var cts = new CancellationTokenSource();
    _flashTimers[e.CameraIndex] = cts;

    try
    {
        await Task.Delay(3000, cts.Token);
        MainThread.BeginInvokeOnMainThread(() => slot.ShowFlash = false);
    }
    catch (OperationCanceledException) { /* newer scan fired — new timer takes over */ }
}
```

**`CameraStatusChanged` handler:**
```csharp
private void OnCameraStatusChanged((int CameraIndex, CameraStatus Status) e)
{
    CameraSlots[e.CameraIndex].Status = e.Status;
}
```

**Frame rate display** — `CameraSlotState` has a `FrameRateDisplay` string. Update it from a rolling frame counter:
- Add `int _frameCount` and `DateTime _frameWindowStart` to `CameraInstance` (or `CameraSlotState`)
- Platform handler increments a counter on every frame it processes
- A 1-second `DispatcherTimer` in `MainViewModel` reads the counter, computes fps, resets, updates `FrameRateDisplay` string (e.g., `"6 fps"`)
- Timer only runs while cameras are active; stopped in `StopAllAsync` / app close

**Camera count = 1:** single slot, full-width grid — identical to existing behavior

#### 3.3 — Update `MainPage.xaml` + `MainPage.xaml.cs`
**File:** `SmartLog.Scanner/Views/MainPage.xaml`

Replace single `<controls:CameraQrView>` with a `Grid` driven by `GridColumns`/`GridRows`:

```xml
<CollectionView ItemsSource="{Binding CameraSlots}"
                ItemsLayout="...GridItemsLayout bound to GridColumns...">
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="vm:CameraSlotState">
            <!-- Camera cell: CameraQrView + name overlay + scan type badge + status dot -->
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

**Shared result panel** stays at bottom, same as current design.

**`MainPage.xaml.cs`:** Wire `BarcodeDetected` events from each `CameraQrView` to `MultiCameraManager.ProcessQrCodeAsync(index, payload)`. This requires dynamically creating `CameraQrView` controls or binding them via the `CollectionView` — see note below.

> **Note on CameraQrView in CollectionView:** MAUI `CollectionView` DataTemplates cannot wire `BarcodeDetected` events declaratively. Use a `BindableProperty` on `CameraQrView` named `CameraIndex` (int) and handle `BarcodeDetected` in the shared code-behind handler: `void OnBarcodeDetected(object s, string payload) => VM.OnBarcodeFromCamera(((CameraQrView)s).CameraIndex, payload)`.

#### 3.4 — Add `CameraIndex` bindable to `CameraQrView`
**File:** `SmartLog.Scanner/Controls/CameraQrView.cs`

Add:
```csharp
public static readonly BindableProperty CameraIndexProperty =
    BindableProperty.Create(nameof(CameraIndex), typeof(int), typeof(CameraQrView), 0);
public int CameraIndex { ... }
```

#### 3.5 — Error Isolation + Auto-Recovery
**File:** `SmartLog.Scanner.Core/Services/MultiCameraManager.cs`

- Each camera's scan processing is wrapped in try/catch
- On exception: `Cameras[index].Status = CameraStatus.Error`, fire `CameraStatusChanged`

**Auto-recovery — race condition guard:**
Use a `Dictionary<int, CancellationTokenSource>` (`_recoveryCts`) to ensure at most one recovery loop runs per camera:
```csharp
private async Task TriggerAutoRecoveryAsync(int cameraIndex)
{
    // Cancel any existing recovery loop for this camera
    if (_recoveryCts.TryGetValue(cameraIndex, out var existing)) existing.Cancel();
    var cts = new CancellationTokenSource();
    _recoveryCts[cameraIndex] = cts;

    try
    {
        while (Cameras[cameraIndex].ReconnectAttempts < 3 && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(10_000, cts.Token);
            await RestartCameraAsync(cameraIndex);
            Cameras[cameraIndex].ReconnectAttempts++;
        }
        if (!cts.Token.IsCancellationRequested)
            Cameras[cameraIndex].Status = CameraStatus.Offline; // all 3 retries exhausted
    }
    catch (OperationCanceledException) { /* manual stop or new error — cancelled cleanly */ }
}
```

**Manual stop does NOT trigger auto-recovery:**
`StopCameraAsync` sets `Cameras[index].IsEnabled = false` before stopping. `TriggerAutoRecoveryAsync` checks `IsEnabled` at the start — if false, returns immediately. `RestartCameraAsync` re-enables (`IsEnabled = true`) before starting, so the user-initiated restart re-arms recovery.

**"No Signal" / watchdog detection:**
- `CameraInstance.LastFrameAt (DateTime?)` — platform handler writes `DateTime.UtcNow` on each frame it processes (not just decoded frames — any frame received from the camera pipeline)
- `MultiCameraManager` runs a single periodic watchdog task (30s interval) via `Task.Delay` in a background loop
- If `LastFrameAt` is older than 10s for a camera currently in `Scanning` status → set status to "No Signal" (`CameraStatus.Error` with `ErrorMessage = "No Signal"`) and trigger auto-recovery
- **Note:** "Decode thread hangs" from US0070 is handled by this same watchdog. The platform handler's native camera pipeline runs on its own thread — if it hangs, `LastFrameAt` stops updating and the watchdog catches it. `RestartCameraAsync` stops and reinitializes the `CameraQrView`, which reinitializes the platform handler. No direct thread-kill needed.
- "Camera returns black frames" (US0070 edge case) requires image analysis in the platform handler. **Deferred** — mark as future enhancement; current watchdog only checks for absence of frames, not frame content.

---

### Phase 4: MauiProgram Registration + App Lifecycle
**Files:**
- `SmartLog.Scanner/MauiProgram.cs`
- `SmartLog.Scanner/Views/MainPage.xaml.cs`
- `SmartLog.Scanner/App.xaml.cs`

#### 4.1 — DI Registration
- Register `MultiCameraManager` as singleton: `.AddSingleton<IMultiCameraManager, MultiCameraManager>()`
- Register `AdaptiveDecodeThrottle` as singleton
- Keep existing `CameraQrScannerService` registration (used internally by manager)
- Update `MainViewModel` DI to receive `IMultiCameraManager`

#### 4.2 — App lifecycle: `StopAllAsync` on close/navigate-away
Camera decode threads must be stopped when the app closes or navigates away from `MainPage`. Without this, threads keep running and may crash on Windows when the window is destroyed.

**`MainPage.xaml.cs`:**
```csharp
protected override async void OnDisappearing()
{
    base.OnDisappearing();
    await ViewModel.StopCamerasAsync();
}
```

**`MainViewModel`:**
```csharp
public async Task StopCamerasAsync() => await _manager.StopAllAsync();
```

**`App.xaml.cs`** — hook window destroy event:
```csharp
protected override Window CreateWindow(IActivationState? activationState)
{
    var window = base.CreateWindow(activationState);
    window.Destroying += async (_, _) =>
    {
        var manager = Handler?.MauiContext?.Services.GetService<IMultiCameraManager>();
        if (manager != null) await manager.StopAllAsync();
    };
    return window;
}
```

The `Window.Destroying` hook ensures cameras stop even if the user closes the window directly without navigating (common on desktop).

---

### Phase 5: Tests
**File:** `SmartLog.Scanner.Tests/Services/MultiCameraManagerTests.cs`

Test cases (unit, no MAUI runtime):
- `StartAllAsync` starts all configured cameras
- `StopCameraAsync(1)` stops only camera 1; others running
- `RestartCameraAsync` resets error state
- `ProcessQrCodeAsync` routes to correct camera's scanner service
- Cross-camera dedup: same student ID within 5s → `DebouncedLocally` on second
- Max 8 cameras enforced (`InitializeAsync` with 9 → throws)
- Throttle calculation: 1→5, 4→8, 8→15

**File:** `SmartLog.Scanner.Tests/Services/AdaptiveDecodeThrottleTests.cs`

- All count breakpoints verified
- Recalculation when active count changes

---

## Edge Case Handling Plan

| # | Edge Case | Handling Strategy | Phase |
|---|-----------|-------------------|-------|
| 1 | Camera disconnected mid-scan | Exception caught in `ProcessQrCodeAsync`; status→Error; auto-recovery loop | Phase 1 (MultiCameraManager) |
| 2 | All cameras fail | `CameraStatus.Offline` for all; UI shows "No Active Cameras" message | Phase 3 (MainViewModel) |
| 3 | Camera index out of range | Bounds check in `ProcessQrCodeAsync`; log warning, return | Phase 1 |
| 4 | Same device assigned to two cameras | Allowed (user choice); log warning only | Phase 2 (SetupViewModel) |
| 5 | USB hub disconnect | Multiple cameras enter Error state independently; others unaffected | Phase 1 (error isolation) |
| 6 | `StartAllAsync` called twice | `IsScanning` guard in each `CameraQrScannerService`; no-op for running cameras | Phase 1 |
| 7 | CPU usage exceeds 80% | `AdaptiveDecodeThrottle` increases skip count dynamically (future enhancement; for now static table) | Phase 1 (noted) |
| 8 | Camera with <15fps native | Minimum throttle = 3 frames enforced | Phase 1 |
| 9 | No USB cameras detected (Setup) | "No cameras found. Connect a USB camera." message; save disabled | Phase 2 |
| 10 | Settings file corrupted | `PreferencesService` returns defaults; graceful fallback to 1 camera | Phase 2 |
| 11 | Reduce camera count (confirms) | `OnCameraCountChanged` in SetupVM checks active slots; shows confirmation | Phase 2 |
| 12 | Camera preview freezes (no frames 10s) | Watchdog (30s) checks `LastFrameAt`; sets "No Signal" + triggers auto-recovery | Phase 3 |
| 13 | Window too small for grid | `CollectionView` scroll enabled; minimum cell size via `MinimumHeightRequest` | Phase 3 |
| 14 | Rapid sequential scans same camera | `CameraQrScannerService` 500ms debounce + 3s lockout handles this already | Phase 1 (existing) |
| 15 | Decode thread hangs | Same watchdog as #12 — `LastFrameAt` stops updating; `RestartCameraAsync` reinitializes `CameraQrView` + handler | Phase 3 |
| 16 | Old scanner version (no `cameraIndex` field) | Field is nullable; server stores `null`; no schema or API breakage | Phase 0 |
| 17 | `cameraIndex` out of range on server (e.g., 99) | Server stores whatever value is sent; no validation needed — it's informational only | Phase 0 |
| 18 | UI update from background decode thread | All event handlers in `MainViewModel` dispatch via `MainThread.BeginInvokeOnMainThread` | Phase 3 |
| 19 | Multiple auto-recovery loops for same camera | `_recoveryCts` dictionary cancels previous loop before spawning new one | Phase 3.5 |
| 20 | App closed while cameras running | `Window.Destroying` + `OnDisappearing` both call `StopAllAsync` | Phase 4 |
| 21 | Manual stop triggers auto-recovery | `StopCameraAsync` sets `IsEnabled = false`; recovery loop checks `IsEnabled` before retrying | Phase 3.5 |
| 22 | Scan type changed mid-session | `IMultiCameraManager.UpdateScanTypes()` called from `SetupViewModel.SaveAsync`; takes effect on next scan | Phase 1.5 |
| 23 | Configured cameras > physically available at startup | `InitializeAsync` sets missing devices to `Offline` immediately; other cameras still start | Phase 1.4 |
| 24 | Flash timer overwritten by rapid scans | Per-slot `CancellationTokenSource` cancelled before new timer starts | Phase 3.2 |
| 25 | Camera returns black frames | **Deferred** — requires image analysis in platform handler; noted as future enhancement | Future |

**Coverage:** 24/25 edge cases handled (black frame detection deferred)

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `CollectionView` can't wire `BarcodeDetected` events declaratively | High — grid won't receive scan events | Use `CameraIndex` bindable property + shared code-behind handler |
| Multiple `CameraQrScannerService` instances share dedup service (correct) but may have state conflicts | Medium | Each service has its own `_lastPayload` / `_lastProcessedPayload` state — no conflict |
| Windows platform handler doesn't support multiple camera instances simultaneously | High | Only needed change: handler reads `CameraIndex` for routing; one handler per view instance |
| MAUI `CollectionView` with `CameraQrView` inside DataTemplate may not work (platform controls in templates) | High | Fallback: manually layout 1-8 fixed `CameraQrView` controls with `IsVisible` driven by camera count |
| USB 3.0 bandwidth limitation with 3+ cameras | Medium | Warning shown in UI; not a software mitigation |

> **Fallback plan for grid**: If `CameraQrView` inside `CollectionView` DataTemplate fails (common MAUI limitation with native views), use a fixed `Grid` with 8 `CameraQrView` controls, all created upfront in XAML, with `IsVisible` driven by `CameraSlots.Count`. This is less elegant but reliable.

---

## Implementation Order (story dependencies)

```
Phase 0: WebApp Scan entity + migration + ScanApiService update (cross-cutting)
Phase 1: US0066 (MultiCameraManager) → US0067 (AdaptiveThrottle baked in)
Phase 2: US0069 (per-camera scan type) + US0071 (setup page) [parallel]
Phase 3: US0068 (grid UI) + US0070 (error isolation) [parallel, depend on Phase 1]
Phase 4: MauiProgram wiring
Phase 5: Tests
```

Phase 0 touches both repos. Commit WebApp changes first so the server accepts `cameraIndex` before scanner starts sending it.

---

## Definition of Done

- [ ] `Scan.CameraIndex` (nullable int) added to WebApp entity + migration applied
- [ ] `POST /api/v1/scans` accepts and stores `cameraIndex`
- [ ] `MultiCameraManager` manages 1-8 cameras with independent lifecycle
- [ ] Each scan submitted with correct `cameraIndex` matching its physical camera
- [ ] Single-camera scans submit `cameraIndex: 0`; old scanner clients (no field) stored as `null`
- [ ] Adaptive throttle values correct for all camera counts
- [ ] Setup page shows camera count selector + per-camera config rows
- [ ] Main page shows responsive camera grid
- [ ] Per-camera scan type submitted correctly to server
- [ ] Camera failure isolated; other cameras continue
- [ ] Auto-recovery (3 attempts × 10s)
- [ ] USB 3.0 warning for 3+ cameras
- [ ] Single-camera mode identical to existing behavior
- [ ] Unit tests pass for `MultiCameraManager` and `AdaptiveDecodeThrottle`
- [ ] Build succeeds on both `net8.0-maccatalyst` and `net8.0-windows10.0.19041.0`

---

## Notes

- **MAUI native view in CollectionView**: Test the `CameraQrView`-in-template approach first. If it fails, use the fixed-8-view fallback. Document the chosen approach in the PR.
- **Decode resolution (320×240)**: Not currently enforced by platform handlers. Add `DecodeResolution` bindable property to `CameraQrView` in Phase 3 if platform handler supports it; otherwise note as future work.
- **ScannerApp sdlc-studio plans** live in `/Users/markmarmeto/Projects/SmartLogScannerApp/sdlc-studio/plans/`.
- **Stories live** in the WebApp sdlc-studio: `/Users/markmarmeto/Projects/SmartLogWebApp/sdlc-studio/stories/US0066-US0071`.
