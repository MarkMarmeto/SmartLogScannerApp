# Windows Multi-Camera Deployment Notes

EP0011 was developed and heavily tested on macOS. This document captures Windows-specific notes for the multi-camera pipeline (`ICameraEnumerationService` / `ICameraWorker` / `MultiCameraManager`) and provides the hardware verification checklist for on-site testing.

---

## Build for Windows (must be done on a Windows machine)

The MAUI XAML compiler (`XamlCompiler.exe`) is a Windows-only binary and cannot run on macOS. Cross-compiling the full app from macOS is **not supported** — build on the target Windows machine or a Windows CI agent.

```powershell
# On Windows — install the MAUI workload if not already present
dotnet workload install maui-windows

# Build release
dotnet build SmartLog.Scanner -f net8.0-windows10.0.19041.0 -c Release

# Publish self-contained
dotnet publish SmartLog.Scanner -f net8.0-windows10.0.19041.0 -c Release -o publish/win-x64
```

> The Core library (`SmartLog.Scanner.Core`) and all unit tests CAN be compiled and run from macOS.

---

## Windows Platform Implementation

| File | Role |
|------|------|
| `Platforms/Windows/CameraEnumerationService.cs` | WinRT `DeviceInformation.FindAllAsync(VideoCapture)` — lists all USB webcams |
| `Platforms/Windows/WindowsCameraScanner.cs` | MediaCapture + MediaFrameReader — per-camera decode loop |
| `Platforms/Windows/CameraHeadlessWorker.cs` | Thin ICameraWorker wrapper around WindowsCameraScanner |
| `Platforms/Windows/CameraWorkerFactory.cs` | Creates `CameraHeadlessWorker` instances for `MultiCameraManager` |

---

## Hardware Requirements

- Windows 10 build 19041 (20H1) or Windows 11
- 1–8 USB webcams with UVC (USB Video Class) driver support
  - Most modern USB webcams use UVC natively (no extra driver install)
  - Logitech C920/C922/C270, Microsoft LifeCam, and similar prosumer models are known-good
- Each webcam must support 640×480 or higher resolution (needed for reliable QR decode)
- For 4+ cameras: a USB 3.0 hub or multiple USB controllers recommended to avoid bandwidth contention

---

## Windows Camera Privacy Permission

Windows requires explicit permission for each app to access the camera.

On first launch, a Windows Camera Privacy consent dialog appears. If it does not:
1. Go to **Settings → Privacy & Security → Camera**
2. Ensure "Camera access" is On, and "Let apps access your camera" is On
3. Scroll down to find `SmartLog.Scanner.exe` and toggle it On

The permission persists across restarts once granted.

---

## Camera Identifier Stability

`CameraEnumerationService` uses `DeviceInformation.Id` (the WinRT device path, e.g. `\\?\USB#...`) as the stable identifier. These IDs:
- Persist across reboots for USB cameras plugged into the **same physical port**
- **Change** if the camera is moved to a different USB port

> If Setup shows cameras with new device IDs after plugging into a different port, reassign them in the Setup wizard.

---

## Identical-Model Camera Disambiguation

If two identical webcam models (same make/model) are attached:
- Windows gives them the same `Name` (friendly name), e.g. "Logitech C920"
- But each has a distinct `DeviceInformation.Id`
- The Setup picker shows names from `DeviceInformation.Name` — duplicate names will appear if both cameras are the same model
- Workaround: use different USB ports and label the physical cameras; the device path uniquely identifies them regardless of name collision

This is documented here as a known caveat. A future enhancement could show the USB port or device path suffix in the Setup picker to help distinguish identical models.

---

## Sleep / Resume Behaviour

When Windows sleeps while cameras are running, the MediaCapture session is invalidated on resume. The `CameraHeadlessWorker` raises an `ErrorOccurred` event, which triggers `MultiCameraManager`'s auto-recovery loop (3 attempts × 10-second intervals). In most cases cameras recover automatically within 30 seconds.

If cameras do not recover after resume: stop all cameras from the main page and restart them — this forces a fresh `MediaCapture.InitializeAsync` call.

---

## Hardware Verification Checklist

Run these scenarios on a physical Windows gate PC (not a VM — webcam pass-through is unreliable in VMs). Use `Logging.MinimumLevel=Debug` in `appsettings.json` to capture MediaFoundation diagnostics.

### AC1 — Camera Enumeration
- [ ] Attach 2+ USB webcams
- [ ] Open Setup page → all cameras appear by their Windows Device Manager names
- [ ] Note any camera that does not appear (driver issue)
- [ ] Restart app → same camera identifiers appear in the same order

### AC2 — Concurrent Decode (2 cameras)
- [ ] Configure 2 cameras in Setup
- [ ] Start from main page → both tiles show live video
- [ ] Scan a QR code from Camera 1 → submitted with correct `cameraIndex: 1` in server log
- [ ] Scan a QR code from Camera 2 → submitted with correct `cameraIndex: 2` in server log
- [ ] Leave running 5 minutes → no freezes, no stalls

### AC3 — Concurrent Decode (4 cameras, 10-minute soak)
- [ ] Configure 4 cameras
- [ ] Start all → all 4 tiles show live video
- [ ] Monitor Task Manager CPU % — should stay below 30% during idle scanning
- [ ] After 10 minutes: all 4 tiles still live, no camera worker silently stopped

### AC4 — Stop / Restart Cycle
- [ ] Stop all cameras from main page
- [ ] Restart cameras → all resume without app restart
- [ ] Logs show no `Access Denied` or device-busy errors

### AC5 — Disconnect Handling
- [ ] Unplug one camera mid-session → that tile enters Error state
- [ ] Other cameras continue running unaffected
- [ ] Re-plug → auto-recovery kicks in (3 attempts × 10 s) or a clear "restart required" message

### AC6 — Setup Page UX
- [ ] Camera picker dropdowns work (all cameras listed)
- [ ] Preview displays live video for the selected camera
- [ ] Save works and main page reflects configuration
- [ ] Windows Camera Privacy prompt acknowledged on first launch
- [ ] Permission persists after app restart

---

## Test Evidence Template

Record findings for each scenario above:

```
Date: ___________
Host: ___________  (manufacturer, model)
Windows version: ___________  (e.g. Windows 11 23H2, build 22631)
Camera models tested: ___________
USB topology: ___________  (e.g. 4 cameras on USB 3.0 hub + 2 on built-in ports)

AC1: PASS / FAIL / N/A — notes:
AC2: PASS / FAIL / N/A — notes:
AC3: PASS / FAIL / N/A — notes:
AC4: PASS / FAIL / N/A — notes:
AC5: PASS / FAIL / N/A — notes:
AC6: PASS / FAIL / N/A — notes:

Fixes applied (if any):
```

---

## Known Issues (to be updated after hardware verification)

_None confirmed yet — this section will be updated after AC1–AC6 are run on Windows hardware._
