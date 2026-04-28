# PL0019: Concurrent Camera + USB Scanner Mode

> **Status:** Completed
> **Story:** [US0121: Concurrent Camera + USB Scanner Mode](../stories/US0121-concurrent-camera-usb-scanner-mode.md)
> **Epic:** [EP0012: Concurrent Multi-Modal Scanning](../epics/EP0012-concurrent-multi-modal-scanning.md)
> **Created:** 2026-04-28
> **Language:** C# 12 / .NET 8 MAUI (Windows + macOS)
> **Drafted by:** Claude (Opus 4.7)

---

## Overview

Make `MainViewModel` start and subscribe to **both** the multi-camera pipeline (`IMultiCameraManager`) and the USB pipeline (`UsbQrScannerService`) simultaneously when `Scanner.Mode = "Both"`. Today these paths are mutually exclusive (`if mode == Camera else USB`). The refactor introduces two helper predicates (`IsCameraMode`, `IsUsbMode`) and broadens the mode checks. Cross-source deduplication is automatic — `IScanDeduplicationService` is already registered as a singleton (verified `MauiProgram.cs:312`), so both pipelines share dedup state without code changes.

A new `ScanResult.Source` property (enum: `ScanSource.Camera | UsbScanner`) is added so `LogScanToHistoryAsync` can attribute each entry to its actual source rather than the app-level mode (avoids logging `"Both"` as `ScanMethod`). Both source services tag their emitted results.

`HeartbeatService` adds a top-level `usbScannerLastScanAge` field to its payload, separate from the camera health array (per epic Q3 decision).

This plan is the foundation US0122 (setup wizard checkbox) and US0123 (USB indicator card) build on. PL0019 must ship first.

---

## Acceptance Criteria Mapping

| AC (US0121) | Phase |
|-------------|-------|
| AC1: New `Both` mode value recognized | Phase 2 — MainViewModel mode helpers + dual subscription |
| AC2: `Camera` mode unchanged (regression) | Phase 2 — guarded by `IsCameraMode` helper |
| AC3: `USB` mode unchanged (regression) | Phase 2 — guarded by `IsUsbMode` helper |
| AC4: Camera scan flow in `Both` mode | Phase 2 — existing camera handlers attach when `IsCameraMode` |
| AC5: USB scan flow in `Both` mode | Phase 2 — existing USB handlers attach when `IsUsbMode` |
| AC6: Cross-source deduplication | No code change — singleton `ScanDeduplicationService` shared automatically |
| AC7: Scan log field parity | Phase 3 — `LogScanToHistoryAsync` reads `result.Source` |
| AC8: Heartbeat reports USB health | Phase 4 — heartbeat payload extension |
| AC9: Page lifecycle stops both pipelines | Phase 2 — `DisposeAsync` awaits both stops |
| AC10: Default unchanged for new installs | No code change — setup wizard default logic untouched (handled in US0122) |
| AC11: Explicit `Source` field on `ScanResult` | Phase 1 — model + source services |

---

## Technical Context (Verified)

### Confirmed via code read

- `ScanResult` is a `record` with `init` properties (`ScanResult.cs:7`) — adding a new `init` property is non-breaking.
- `ScanLogEntry.ScanMethod`, `CameraIndex`, `CameraName` exist (`ScanLogEntry.cs:76,81,86`).
- `IScanDeduplicationService` is registered as **singleton** (`MauiProgram.cs:312`) — cross-source dedup automatic.
- `UsbQrScannerService` is registered as **singleton** (`MauiProgram.cs:318`) — same instance available throughout app.
- `IMultiCameraManager` is **singleton** (`MauiProgram.cs:322`).
- `IHeartbeatService` is **singleton** (`MauiProgram.cs:345`) — already injected into `MainViewModel`.
- `MainViewModel` already injects both `IMultiCameraManager` and `UsbQrScannerService` regardless of mode — DI provides both, only event subscription differs.
- `MainPage.xaml.cs:33` reads `_scannerMode = Preferences.Get("Scanner.Mode", "Camera")` from preferences and gates the keyboard listener attach.
- `MainPage.xaml.cs:191` has a duplicate `_scannerMode == "USB"` check inside `OnHandlerChanged` that needs the same broadening.

### Files to touch

| File | Change |
|------|--------|
| `SmartLog.Scanner.Core/Models/ScanResult.cs` | Add `ScanSource` enum + `Source` property |
| `SmartLog.Scanner.Core/Models/ScanSourceExtensions.cs` (new) | `ToScanMethodString()` helper — testable in Core |
| `SmartLog.Scanner.Core/Services/CameraQrScannerService.cs` | Set `Source = Camera` on emitted `ScanResult` |
| `SmartLog.Scanner.Core/Services/UsbQrScannerService.cs` | Set `Source = UsbScanner` on emitted `ScanResult` |
| `SmartLog.Scanner/ViewModels/MainViewModel.cs` | `IsCameraMode`/`IsUsbMode` helpers, dual subscription, `LogScanToHistoryAsync` uses `Source.ToScanMethodString()`, broadened timer gate, dispose both, `StopCamerasAsync` stops both |
| `SmartLog.Scanner/Views/MainPage.xaml.cs` | Six `_scannerMode` checks routed through `_viewModel.IsCameraMode` / `IsUsbMode` (lines 33, 48, 51, 56, 184, 192) |
| `SmartLog.Scanner.Core/Services/HeartbeatService.cs` | Add `usbScannerLastScanAge` to payload + `OnUsbScan` subscriber |
| `SmartLog.Scanner.Tests/Models/ScanSourceExtensionsTests.cs` (new) | Lock the `Camera`/`USB` mapping |
| `SmartLog.Scanner.Tests/Models/ScanResultDefaultsTests.cs` (new or extend existing) | Default `Source = Camera` smoke test |
| `SmartLog.Scanner.Tests/Services/HeartbeatServiceTests.cs` | Verify `usbScannerLastScanAge` populated when USB pipeline active, null otherwise, ignored when camera scan |
| **No** `MainViewModelTests.cs` | Test project cannot reference MAUI project — `MainViewModel` behavior covered by Phase 6 manual verification |

---

## Implementation Phases

### Phase 1 — `ScanSource` enum + `ScanResult.Source` property

**File:** `SmartLog.Scanner.Core/Models/ScanResult.cs`

Add the enum (top of file, after namespace) and property (anywhere in the record):

```csharp
public enum ScanSource
{
    /// <summary>Scan captured via webcam (cameras pipeline).</summary>
    Camera,
    /// <summary>Scan captured via USB keyboard-wedge barcode scanner.</summary>
    UsbScanner
}

// Inside ScanResult record:
/// <summary>
/// EP0012/US0121: Identifies which input pipeline emitted this scan.
/// </summary>
public ScanSource Source { get; init; } = ScanSource.Camera;
```

Default `Camera` is the safer fallback — most existing call sites construct `ScanResult` from the camera path. USB call sites must set it explicitly (Phase 1.b).

**File:** `SmartLog.Scanner.Core/Services/CameraQrScannerService.cs`

Find every `new ScanResult { ... }` (or `result with { ... }`) construction and add `Source = ScanSource.Camera` if not already implicit via the default. Done — no behavioral change since default is `Camera`.

**File:** `SmartLog.Scanner.Core/Services/UsbQrScannerService.cs`

Find every `ScanResult` construction and explicitly set `Source = ScanSource.UsbScanner`. Audit `with` clauses carefully — they preserve unset fields, so a `result with { Status = ... }` after the initial construction won't lose the source.

**Verification:** `dotnet build SmartLog.Scanner.Core -c Release` — must compile with zero warnings introduced.

### Phase 2 — `MainViewModel` mode helpers + dual subscription

**File:** `SmartLog.Scanner/ViewModels/MainViewModel.cs`

**2.a — Replace `IsCameraMode` and add `IsUsbMode`:**

Current line 103: `public bool IsCameraMode => _scannerMode == "Camera";`

Replace with:

```csharp
/// <summary>True when the scanner is running cameras (mode is "Camera" or "Both").</summary>
public bool IsCameraMode => _scannerMode is "Camera" or "Both";

/// <summary>True when the scanner is listening to USB input (mode is "USB" or "Both").</summary>
public bool IsUsbMode => _scannerMode is "USB" or "Both";
```

Both must be public — XAML bindings (`IsVisible="{Binding IsCameraMode}"` exists at `MainPage.xaml:168`) and `MainPage.xaml.cs` need to read them.

**2.b — Constructor: dual event subscription**

Current logic (around line 154):

```csharp
if (_scannerMode == "Camera") {
    _multiCameraManager.ScanCompleted += OnMultiCameraScanCompleted;
    // ...
}
else { // USB
    _usbScanner.ScanCompleted += OnScanCompleted;
}
```

Replace with two independent guards:

```csharp
if (IsCameraMode)
{
    _multiCameraManager.ScanCompleted += OnMultiCameraScanCompleted;
    _multiCameraManager.ScanUpdated += OnMultiCameraScanUpdated;
    _multiCameraManager.CameraStatusChanged += OnMultiCameraStatusChanged;
}

if (IsUsbMode)
{
    _usbScanner.ScanCompleted += OnScanCompleted;
}

// Status icon: pick by precedence (camera takes precedence in Both mode for the header icon)
StatusIcon = IsCameraMode ? "📷" : "⌨️";
```

The `StatusIcon` initial value in `Both` mode is set to camera icon since the camera pipeline is the primary visual; the new USB indicator card from US0123 gives USB its own visual presence.

**2.c — `InitializeAsync`: start both pipelines + broaden the 1s timer gate**

Current (line 217+) gates camera startup on `_scannerMode == "Camera"` and USB startup on `else`. Replace with two independent blocks. **The `_frameRateTimer` creation is hoisted out of the camera block and gated on `IsCameraMode || IsUsbMode`** because PL0021 needs the same tick to drive the USB indicator's 60s warning heuristic — broadening it once here avoids two plans editing the same lines:

```csharp
if (IsCameraMode)
{
    var cameraCount = Math.Clamp(_preferences.GetCameraCount(), 1, 8);
    var cameraConfigs = BuildCameraConfigs(cameraCount);
    ApplyCameraConfigsToSlots(cameraConfigs, cameraCount);

    await _multiCameraManager.InitializeAsync(cameraConfigs);
    await _multiCameraManager.StartAllAsync();
}

if (IsUsbMode)
{
    await _usbScanner!.StartAsync();
}

// 1-second tick — drives camera fps display today; PL0021 will also call UsbScannerSlot.Tick()
// for the 60s no-scan warning heuristic. Created here once for both modes.
if (IsCameraMode || IsUsbMode)
{
    _frameRateTimer = Application.Current!.Dispatcher.CreateTimer();
    _frameRateTimer.Interval = TimeSpan.FromSeconds(1);
    _frameRateTimer.Tick += OnFrameRateTick;
    _frameRateTimer.Start();
}

// Status messages: prefer camera messaging in Both mode, fall back to USB
StatusMessage = IsCameraMode
    ? "Ready to scan QR codes"
    : "Ready for USB scanner input";
```

`UsbQrScannerService` is registered as singleton (verified `MauiProgram.cs:318`) so the `_usbScanner!` non-null assertion is safe — DI always provides it.

**2.d — `LogScanToHistoryAsync`: source-based attribution**

Current line ~1069: `ScanMethod = _scannerMode`.

Replace with:

```csharp
ScanMethod = result.Source switch
{
    ScanSource.Camera => "Camera",
    ScanSource.UsbScanner => "USB",
    _ => "Unknown"
},
```

This keeps `ScanMethod` honest — even in `Both` mode, the field reflects the actual source of the individual scan, not the app-level mode value.

**2.e — `DisposeAsync` + `StopCamerasAsync`: stop both pipelines**

Current `DisposeAsync` (around line 880) gates by mode. Replace with:

```csharp
if (IsCameraMode)
    await _multiCameraManager.StopAllAsync();

if (IsUsbMode)
    await _usbScanner!.StopAsync();
```

`StopCamerasAsync` is called from `MainPage.OnWindowDestroying` (window close path, distinct from `OnDisappearing` which calls `DisposeAsync`). In `Both` mode, this needs to stop the USB pipeline too — otherwise USB stays running until process termination. Method name kept for backward-compat, behavior expanded:

```csharp
public async Task StopCamerasAsync()
{
    if (IsCameraMode)
        await _multiCameraManager.StopAllAsync();
    if (IsUsbMode)
        await _usbScanner!.StopAsync();
}
```

**2.f — `ToggleScanType`, `TestValidQr`, `TestInvalidQr`**

These currently dispatch via `if (_scannerMode == "Camera") { camera path } else { usb path }`. In `Both` mode, both pipelines should receive the toggle (they already share the device-level scan type via `_preferences.SetDefaultScanType`, but the camera pipeline's `UpdateScanTypes` call must also fire). Concrete change:

```csharp
[RelayCommand]
private void ToggleScanType()
{
    CurrentScanType = CurrentScanType == "ENTRY" ? "EXIT" : "ENTRY";
    _preferences.SetDefaultScanType(CurrentScanType);

    if (IsCameraMode)
    {
        _multiCameraManager.UpdateScanTypes(CurrentScanType);
        foreach (var cam in _multiCameraManager.Cameras)
        {
            if (cam.Index >= 0 && cam.Index < CameraSlots.Count)
                CameraSlots[cam.Index].ScanType = cam.ScanType;
        }
    }

    // USB scanner reads scan type from preferences directly — no extra call needed beyond SetDefaultScanType.
    _logger.LogInformation("Scan type toggled to: {ScanType}", CurrentScanType);
}
```

For `TestValidQr` / `TestInvalidQr` — in `Both` mode, route through the camera test pipeline (`_multiCameraManager.ProcessQrCodeAsync(0, payload)`) since that exercises the more complex path and produces the camera-style scan flash. Test commands aren't a real user flow; this keeps coverage on the multi-camera path:

```csharp
if (IsCameraMode)
    await _multiCameraManager.ProcessQrCodeAsync(0, payload);
else
    await _usbScanner!.ProcessQrCodeAsync(payload);
```

### Phase 3 — `MainPage.xaml.cs` mode-check broadening

**File:** `SmartLog.Scanner/Views/MainPage.xaml.cs`

There are **six** sites checking `_scannerMode` directly. All routing decisions move to the new ViewModel helpers (per Q6 decision); the `_scannerMode` field itself stays for logging and the late-init fallback path. Critically, **two of the six are camera-preview attach checks that gate Camera 0's live preview** — without broadening these, `Both` mode would silently lose the camera preview while still running cameras headlessly.

**3.a — Line 33-37 (DI ctor — keyboard subscribe):**

```csharp
// Before:
if (_scannerMode == "USB")
    this.Focused += OnPageFocused;

// After:
if (_viewModel.IsUsbMode)
    this.Focused += OnPageFocused;
```

(`_viewModel` is non-null in this branch — DI injected it.)

**3.b — Lines 48 and 51 (`OnAppearing` — camera preview attach, both `#if MACCATALYST` and `#elif WINDOWS`):**

```csharp
// Before (both platform branches):
#if MACCATALYST
    if (_scannerMode == "Camera")
        AttachCameraPreview();
#elif WINDOWS
    if (_scannerMode == "Camera")
        AttachCameraPreview();
#endif

// After (both platform branches):
#if MACCATALYST
    if (_viewModel?.IsCameraMode == true)
        AttachCameraPreview();
#elif WINDOWS
    if (_viewModel?.IsCameraMode == true)
        AttachCameraPreview();
#endif
```

This is **the most consequential fix**: in `Both` mode, the camera grid still receives scan events because `IMultiCameraManager` runs its workers, but without this broadening, the live `CameraPreviewView` for Camera 0 never gets its handler attached → black screen at the top of the left column even though scans work. Verified visually during Phase 6.

**3.c — Line 56 (`OnAppearing` — focus call for USB capture):**

```csharp
// Before:
if (_scannerMode == "USB")
    this.Focus();

// After:
if (_viewModel?.IsUsbMode == true)
    this.Focus();
```

**3.d — Line 184 (`OnHandlerChanged` — late ctor keyboard subscribe):**

```csharp
// Before:
if (_scannerMode == "USB")
    this.Focused += OnPageFocused;

// After:
if (_viewModel.IsUsbMode)
    this.Focused += OnPageFocused;
```

(`_viewModel` is non-null inside this branch — just resolved on the line above.)

**3.e — Line 192 (`OnHandlerChanged` — Mac keyboard handler attach, `#if MACCATALYST`):**

```csharp
// Before:
if (_scannerMode == "USB" && Handler?.PlatformView is UIKit.UIView view)

// After:
if (_viewModel?.IsUsbMode == true && Handler?.PlatformView is UIKit.UIView view)
```

The `_scannerMode` field itself stays (referenced for logging, and as the read-once snapshot inside the late-init guard). All six routing decisions go through the helper.

### Phase 4 — Heartbeat payload extension

**File:** `SmartLog.Scanner.Core/Services/HeartbeatService.cs`

Per Q3 decision: dedicated `usbScannerLastScanAge` field at the top level of the heartbeat payload, separate from the cameras array.

**4.a — Track last USB scan timestamp**

Inject `UsbQrScannerService` (or hook into `IScanHistoryService` filter), and subscribe to its `ScanCompleted` event to capture a private `DateTime? _lastUsbScanAtUtc`. Subscription happens in `StartAsync`, unsubscription in `StopAsync` / `DisposeAsync`.

```csharp
private DateTime? _lastUsbScanAtUtc;

public async Task StartAsync(CancellationToken cancellationToken = default)
{
    // ... existing init ...
    _usbScanner.ScanCompleted += OnUsbScan;
}

public async Task StopAsync()
{
    _usbScanner.ScanCompleted -= OnUsbScan;
    // ... existing teardown ...
}

private void OnUsbScan(object? sender, ScanResult result)
{
    if (result.Source == ScanSource.UsbScanner)
        _lastUsbScanAtUtc = DateTime.UtcNow;
}
```

**Why subscribe vs query history:** The heartbeat already runs every 60 s. Querying `IScanHistoryService.GetRecentLogsAsync(1)` filtered by `ScanMethod = "USB"` would work but adds a DB hit per heartbeat. A direct event subscription is O(1) and uses memory the service already manages.

**4.b — Add `UsbScannerLastScanAge` to payload**

Extend `HeartbeatPayload` record (line 175 in PL0018 reference):

```csharp
private sealed record HeartbeatPayload(
    string? AppVersion,
    string? OsVersion,
    int? BatteryPercent,
    bool? IsCharging,
    string? NetworkType,
    DateTime? LastScanAt,
    int? QueuedScansCount,
    int? UsbScannerLastScanAgeSeconds,   // NEW — null when USB pipeline not active
    DateTime ClientTimestamp);
```

In `BuildPayloadAsync`, compute the age:

```csharp
int? UsbScannerLastScanAgeSeconds = null;
if (_lastUsbScanAtUtc.HasValue)
{
    UsbScannerLastScanAgeSeconds = (int)(DateTime.UtcNow - _lastUsbScanAtUtc.Value).TotalSeconds;
}
```

Field is null when `_lastUsbScanAtUtc` is null (no USB scan ever received in this session, or USB pipeline inactive). This is the signal the WebApp dashboard uses to render the "no scans yet" state.

**4.c — Server-side reception**

Out of scope here — that's a WebApp story. PL0019 just emits the field; the WebApp can ignore it until they add a column for it. JSON serialization is forgiving — extra fields are fine on the receiving side.

### Phase 5 — Tests

**Constraint:** `SmartLog.Scanner.Tests` only references `SmartLog.Scanner.Core` (verified — `SmartLog.Scanner.Tests.csproj` has no project reference to the MAUI project). Test project targets `net8.0`, MAUI project targets MAUI TFMs — bridging that gap is non-trivial and intentionally avoided per CLAUDE.md ("MAUI-specific types must be abstracted behind interfaces to be testable").

Therefore: **no `MainViewModelTests.cs`** — `MainViewModel` lives in `SmartLog.Scanner.ViewModels` (the MAUI project) and is unreachable from tests. Behavior coverage for mode routing falls back to manual verification in Phase 6, matching the project's existing test strategy for that file.

**5.a — Extract a testable Core helper for `ScanSource → ScanMethod` mapping**

Add a static helper in `SmartLog.Scanner.Core/Models/` (or alongside the `ScanSource` enum):

```csharp
public static class ScanSourceExtensions
{
    /// <summary>
    /// Maps a ScanSource enum value to the string used as ScanLogEntry.ScanMethod.
    /// Lives in Core so it's reachable from the test project.
    /// </summary>
    public static string ToScanMethodString(this ScanSource source) => source switch
    {
        ScanSource.Camera => "Camera",
        ScanSource.UsbScanner => "USB",
        _ => "Unknown"
    };
}
```

`MainViewModel.LogScanToHistoryAsync` then calls `result.Source.ToScanMethodString()` instead of inlining the switch.

**File:** `SmartLog.Scanner.Tests/Models/ScanSourceExtensionsTests.cs` (new)

```csharp
[Theory]
[InlineData(ScanSource.Camera, "Camera")]
[InlineData(ScanSource.UsbScanner, "USB")]
public void ToScanMethodString_Maps_Source_Correctly(ScanSource source, string expected)
    => Assert.Equal(expected, source.ToScanMethodString());
```

Tiny test, but locks the mapping so a future enum addition surfaces immediately.

**5.b — `HeartbeatServiceTests.cs` — extend with USB age field**

Project already has the Moq + `HttpMessageHandler` pattern (`ConnectionTestServiceTests`). Add cases (heartbeat lives in Core ✓):

- `Payload_UsbScannerLastScanAgeSeconds_Null_Before_Any_Usb_Scan` — fresh service, build payload, assert null
- `Payload_Includes_UsbScannerLastScanAgeSeconds_After_Usb_Scan` — raise `UsbQrScannerService.ScanCompleted` event with `result.Source = UsbScanner`, assert payload field populated and roughly equals elapsed seconds
- `Payload_UsbScannerLastScanAgeSeconds_Ignores_Camera_Source` — raise event with `Source = Camera`, assert field stays null
- `Subscription_Cleaned_Up_On_StopAsync` — verify event handler removed (raise event after stop, assert no state change)

**5.c — `ScanResult.Source` default verification**

In `SmartLog.Scanner.Tests/Models/` (or alongside existing model tests), add a smoke test asserting `new ScanResult().Source == ScanSource.Camera`. Documents the safer-fallback default decision and catches accidental enum reordering.

**5.d — Manual coverage for what isn't unit-testable**

The following are covered by Phase 6 manual verification, not unit tests:

- `IsCameraMode` / `IsUsbMode` predicate behavior across all three modes
- Constructor dual subscription (camera events + USB events both fire scan handlers)
- Camera-mode-only / USB-mode-only regressions (no cross-pipeline event firing)
- `OnFrameRateTick` runs in USB-only mode (gates the warning tick from PL0021)
- `DisposeAsync` and `StopCamerasAsync` stop both pipelines in `Both` mode
- `ToggleScanType` propagates to USB slot in `Both` mode

This matches existing test strategy — `MainViewModel` has no unit tests today; behavior is validated by running the app.

### Phase 6 — Manual verification

**6.a — macOS dev build:** `dotnet run --project SmartLog.Scanner -f net8.0-maccatalyst`

1. **Camera-only regression:** `Scanner.Mode = "Camera"`, scan QR via webcam, verify student card updates and slot card flashes. (No USB listener active.)
2. **USB-only regression:** `Scanner.Mode = "USB"`, scan QR via USB scanner, verify student card updates. (No cameras running.)
3. **`Both` mode happy path:** manually set `Scanner.Mode = "Both"` in preferences (US0122 not yet shipping), launch app:
   - **Camera preview attaches:** Camera 0's live feed is rendering at the top of the left column (this verifies the Phase 3.b fix).
   - Camera scan: webcam scan → student card + camera slot flash + scan log entry with `ScanMethod = "Camera"`
   - USB scan: trigger via real USB scanner (the in-app `TestValidQr` routes through cameras in `Both` mode per Phase 2.f, so it does not exercise the USB pipeline) → student card update + scan log entry with `ScanMethod = "USB"`
   - **Cross-source dedup:** scan same QR via webcam, then within 3 s send same payload via USB scanner — assert only one server submission in scan log (the second is `DebouncedLocally`).
4. **macOS keystroke vs camera preview verification (per Q4 decision):** in `Both` mode with a real USB scanner connected, scan a QR code. Verify keystrokes are captured while `CameraPreviewView` is rendering Camera 0's feed. **If keystrokes are dropped, escalate to NSEvent monitor fallback (planned in Q4 resolution).**
5. **Heartbeat payload:** check Serilog output for outbound heartbeat JSON; confirm `usbScannerLastScanAgeSeconds` is null before any USB scan, then populated after.
6. **Window destroy stops both:** in `Both` mode, close the app window (not navigate away). Verify Serilog shows both camera stop and USB stop messages — exercises the broadened `StopCamerasAsync` from Phase 2.e.
7. **Cross-build Windows TFM from macOS:** `dotnet build SmartLog.Scanner -f net8.0-windows10.0.19041.0` (clean compile). PL0019 only modifies code-behind + Core (no XAML changes), so cross-build is expected to succeed. **PL0020 and PL0021 will fail this step due to XAML compilation requiring a Windows host.**
8. **Test suite:** `dotnet test SmartLog.Scanner.Tests` — green.

**6.b — Windows hardware verification (in scope for PL0019 acceptance):**

Cannot be done from macOS; requires a Windows scanner PC with at least one webcam and one USB barcode scanner connected. Build path: `dotnet publish SmartLog.Scanner -f net8.0-windows10.0.19041.0 -c Release -o ./publish/win-x64` (per CLAUDE.md guidance) and copy to the target PC.

1. **`Camera`-only regression on Windows:** confirm webcam scanning still works as before (no regression from the mode-helper refactor).
2. **`USB`-only regression on Windows:** confirm USB scanner keystroke capture and submit pipeline unchanged. Standard MAUI `KeyDown` event flow on `MainPage` (Windows MAUI does not use the macOS-specific `AttachMacKeyboardHandler`; default page-level keyboard event delivery applies).
3. **`Both` mode on Windows — the new combination:**
   - Verify camera preview renders for Camera 0 (Phase 3.b fix on `WINDOWS` platform branch).
   - Verify USB keystroke capture works while `MediaCapture` sessions are running. **Specifically:** `WindowsCameraScanner` / `MediaCapture` runs headlessly and does not claim window focus, so USB keystrokes should still arrive at the focused `MainPage`. Confirm by scanning a known QR via USB while a webcam is actively decoding — assert both flows fire correctly with no dropped keystrokes.
   - Verify cross-source dedup behaves identically on Windows (same singleton `IScanDeduplicationService`).
4. **Concurrent CPU/memory load check on Windows:** With 1-3 webcams + USB active, observe Task Manager CPU and memory over a 5-minute scan session. Existing US0088 verified multi-camera Windows compatibility; the addition of the USB pipeline is event-driven (no decode work), so no new pressure expected. Document any anomaly.
5. **High-DPI rendering smoke check:** On a Windows machine with display scaling at 125%/150%/200%, confirm the camera preview, camera grid, and (after PL0021 ships) the USB indicator card render at expected sizes without clipping. Particularly relevant for the `Path` glyph in PL0021.
6. **Power management:** put the Windows PC to sleep, wake it, confirm both pipelines auto-recover (cameras via `MultiCameraManager`'s existing recovery loop; USB via OS-level HID re-enumeration). Document any failure mode.

---

## Risks & Considerations

- **macOS UITextField vs CameraPreviewLayer first-responder fight (Q4 risk).** The existing `AttachMacKeyboardHandler` workaround uses a hidden `UITextField` to capture keystrokes. With Camera 0's `AVCaptureVideoPreviewLayer` in the same view hierarchy in `Both` mode, this may lose first-responder. Verified during Phase 6.a step 4. If broken: swap to `NSEvent.AddLocalMonitorForEvents` (application-level keyboard hook) — adds ~half a day. Tracked as a fallback in the epic open questions.
- **Windows USB keystroke capture vs MediaCapture coexistence.** Windows uses standard MAUI `KeyDown` event delivery to `MainPage`; `WindowsCameraScanner` runs `MediaCapture` headlessly without claiming window focus, so theoretically this should work. Verified during Phase 6.b step 3. If keystrokes are dropped while cameras are active on Windows, the workaround would be platform-specific keyboard hook via `Window.AddInputHook` or similar — but no such workaround is specced upfront because the existing US0008 + US0088 baseline (USB scanner + multi-camera, separately) both work; combining them is a small step.
- **Default `ScanSource.Camera` could mask incomplete USB tagging.** If `UsbQrScannerService` ever forgets to set `Source` on a code path, a USB scan would log as `ScanMethod = "Camera"`. Mitigation: when implementing Phase 1, audit every `new ScanResult` and `result with` construction in `UsbQrScannerService.cs` and explicitly set `Source = UsbScanner`. The `ScanResultDefaultsTests` smoke test catches accidental enum reordering only; explicit USB tagging is the real safeguard.
- **`UpdateScanTypes` in `Both` mode race** — `_multiCameraManager.UpdateScanTypes(CurrentScanType)` writes the scan type to all cameras synchronously. USB scanner reads from `_preferences.GetDefaultScanType()` lazily. Toggle order matters: prefs write first, then propagate. Already correct in current code; just preserved in the refactor.
- **Status icon in `Both` mode header** — chose camera icon (`📷`) since cameras are the primary visual surface. Alternative: a stacked icon. Out of scope for v1; revisit if operator feedback shows confusion.
- **Heartbeat subscription leak if `StartAsync` called twice** — already guarded in `HeartbeatService.StartAsync` (returns early if `_cts != null`). The `_usbScanner.ScanCompleted += OnUsbScan` subscription must be inside that guard, otherwise double-start would double-subscribe. Plan covers this in Phase 4.a.
- **Preference rename risk** — sticking with `Scanner.Mode` key (string values `"Camera"`, `"USB"`, `"Both"`) keeps backward compatibility with existing installs. No migration needed since `"Both"` is opt-in via US0122; existing installs keep their current value.
- **Test coverage gap for `MainViewModel` behavior.** Per project test strategy (test project cannot reference MAUI project), `MainViewModel` mode-routing logic is validated by manual verification only. This is consistent with the project's existing approach but means a regression in mode handling could only be caught at run time. Mitigation: keep the helpers (`IsCameraMode`, `IsUsbMode`) trivially simple so behavior is obvious from inspection; cover the brittle parts (source mapping, heartbeat USB age) via Core extension tests.
- **Cross-build expectation:** PL0019 cross-builds cleanly from macOS to Windows TFM (no XAML changes — code-behind only). PL0020 and PL0021 will not cross-build from macOS due to XAML compilation requiring a Windows host.

---

## Out of Scope

- Setup wizard checkbox UI for `Both` mode (US0122 / PL0020).
- USB indicator slot card UI (US0123 / PL0021).
- Plug/unplug detection for USB scanners.
- Multiple simultaneous USB scanners.
- Per-source ENTRY/EXIT scan type override.
- Refactoring `MainPage.xaml.cs`'s mode handling into a richer state machine — current pattern is fine; `IsCameraMode`/`IsUsbMode` helpers are sufficient.
- Server-side WebApp reception of `usbScannerLastScanAge` (separate WebApp story).

---

## Estimated Effort

| Phase | Time |
|-------|------|
| 1 — `ScanSource` enum + `Source` property + extension helper + source services | ~45 min |
| 2 — `MainViewModel` mode helpers + dual subscription + broadened timer gate + dispose both | ~1.5 h |
| 3 — `MainPage.xaml.cs` six-site mode-check broadening | ~30 min |
| 4 — Heartbeat payload extension | ~45 min |
| 5 — Tests (Core only — extension mapping, default, heartbeat USB age) | ~1 h |
| 6.a — macOS dev manual verification | ~45 min |
| 6.b — Windows hardware verification (separate session on a Windows scanner PC) | ~1.5 h |
| **Total** | **~6.75 hours** |

Slightly higher than the original 5-pt estimate — the expanded Phase 3 (six sites instead of two), broader Phase 5 footprint, and dedicated Windows hardware session push it up. Still within reasonable bounds for a 5-pt story; flag if it slips further during execution.

---

## Rollout Plan

1. Phase 1 — `ScanSource` enum + property; build clean; commit on `dev`.
2. Phase 2 — `MainViewModel` refactor; unit tests for mode helpers and subscription guards; build clean.
3. Phase 3 — `MainPage.xaml.cs` updates.
4. Phase 4 — Heartbeat payload field + subscription; tests.
5. Phase 5 — full `dotnet test SmartLog.Scanner.Tests` green.
6. Phase 6 — manual verification on macOS dev build + Windows TFM cross-build. **If macOS keystroke capture breaks under camera previews, swap to NSEvent monitor before declaring done.**
7. Confirm with user before committing/PR.
8. Commit on `dev` branch; PR to `main` only after US0122 + US0123 also land (the three ship together as EP0012).

---

## Open Questions

> All resolved. Plan is ready for execution.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-28 | Claude (Opus 4.7) | Initial plan drafted; verified DI lifetimes and existing mode-check sites; locked Q4 verification + NSEvent fallback path |
| 2026-04-28 | Claude (Opus 4.7) | Code-plan review pass — five fixes applied: (1) Phase 3 expanded to six `_scannerMode` sites in `MainPage.xaml.cs` (added lines 48, 51, 56 — critical for Camera 0 preview attach in `Both` mode); (2) dropped `MainViewModelTests.cs` (test project cannot reference MAUI project — verified via csproj); replaced with Core-side `ScanSourceExtensions.ToScanMethodString()` helper + tests; (3) `StopCamerasAsync` broadened to also stop USB on window destroy; (4) timer-creation gate hoisted into PL0019 with `IsCameraMode || IsUsbMode` to avoid PL0021 needing to re-edit; (5) Windows hardware verification expanded into a dedicated Phase 6.b — confirms keystroke capture coexists with MediaCapture, high-DPI rendering, sleep/wake recovery. Effort revised 5 h → ~6.75 h. |
