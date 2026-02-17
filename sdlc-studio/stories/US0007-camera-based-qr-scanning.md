# US0007: Implement Camera-Based QR Scanning

> **Status:** Draft
> **Epic:** [EP0002: QR Code Scanning and Validation](../epics/EP0002-qr-code-scanning-and-validation.md)
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-02-13

## User Story

**As** Guard Gary
**I want** the app to continuously scan QR codes using the device camera and validate them automatically
**So that** I can simply point the camera at a student's QR code and get an instant result without pressing any buttons, keeping the gate queue moving during peak hours

## Context

### Persona Reference
**Guard Gary** - School security guard, novice technical proficiency, needs instant visual feedback (green = good, red = bad) with zero decision-making during scanning.
[Full persona details](../personas.md#guard-gary)

### Background
Camera-based QR scanning is one of two input modes for the SmartLog Scanner (the other being USB barcode scanner in US0008). When the device is configured with Scanner.Mode = "Camera" (set during EP0001 device setup by IT Admin Ian), the main scan screen displays a live camera preview using ZXing.Net.Maui. The library continuously captures frames and decodes QR codes. To prevent duplicate processing when the same QR code stays in frame for several seconds, a 2-second debounce is applied for identical payloads. Every decoded payload is passed through IHmacValidator (US0006) before being forwarded downstream to the scan processing pipeline (EP0003). Camera permissions must be handled on both macOS (NSCameraUsageDescription in Info.plist) and Windows (webcam capability in Package.appxmanifest).

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | Performance | Camera QR decode at >= 15 fps | ZXing.Net.Maui frame processing must not be throttled below 15 fps |
| PRD | UX | Zero decision-making during scanning | Continuous auto-decode; no "scan" button press required |
| TRD | Architecture | IQrScannerService interface abstracts camera vs USB mode | Camera scanning implemented as CameraQrScannerService behind IQrScannerService |
| TRD | Tech Stack | ZXing.Net.Maui for camera | Platform permissions: NSCameraUsageDescription (macOS), webcam capability (Windows) |
| Epic | Behaviour | Duplicate reads of same payload debounced within 2 seconds | Debounce timer resets on each duplicate read; different payload resets tracking |

---

## Acceptance Criteria

### AC1: Camera Preview Display
- **Given** the scanner device is configured with Scanner.Mode = "Camera" in Preferences
- **When** the MainPage is displayed and camera mode is active
- **Then** a live camera preview is rendered in the main scan area using ZXing.Net.Maui CameraBarcodeReaderView
- **And** the preview fills the designated scan area of the layout
- **And** the camera starts automatically without requiring a manual start action from the user

### AC2: Continuous QR Decoding
- **Given** the camera preview is active and displaying frames
- **When** a QR code enters the camera's field of view
- **Then** ZXing.Net.Maui continuously decodes frames and fires a BarcodesDetected event
- **And** the decoded payload string is extracted from the first detected barcode result
- **And** only QR code format barcodes are processed (other barcode formats are ignored)

### AC3: Two-Second Same-Payload Debounce
- **Given** a QR code payload has just been decoded and processed
- **When** the same payload is decoded again within 2 seconds
- **Then** the duplicate decode is silently ignored (no re-validation, no downstream processing)
- **And** if a different payload is decoded within the 2-second window, it is processed immediately
- **And** after 2 seconds have elapsed since the last decode of a given payload, that same payload can be processed again

### AC4: Payload Forwarded to HMAC Validation
- **Given** a QR code payload has been decoded and passes the debounce check
- **When** the payload is ready for processing
- **Then** it is passed to IHmacValidator.ValidateAsync() (US0006) for HMAC-SHA256 signature verification
- **And** the validation result (success or rejection) is forwarded to the scan processing pipeline via an event or callback
- **And** invalid payloads are not sent to any server endpoint

### AC5: Camera Permission - macOS
- **Given** the app is running on macOS (MacCatalyst)
- **When** camera mode is activated for the first time
- **Then** the OS camera permission dialog is triggered (governed by NSCameraUsageDescription in Info.plist)
- **And** if the user grants permission, camera preview starts normally
- **And** if the user denies permission, a clear error message is displayed: "Camera access denied. Please enable camera access in System Settings > Privacy & Security > Camera"
- **And** scanning does not proceed until permission is granted

### AC6: Camera Permission - Windows
- **Given** the app is running on Windows (WinUI 3)
- **When** camera mode is activated
- **Then** the webcam capability is declared in Package.appxmanifest
- **And** if the webcam is accessible, camera preview starts normally
- **And** if camera access is blocked, a clear error message is displayed directing the user to Windows Settings > Privacy > Camera

### AC7: Camera Mode Conditional Activation
- **Given** the Preferences contain Scanner.Mode setting
- **When** Scanner.Mode = "Camera"
- **Then** camera preview and QR decoding are active
- **And** when Scanner.Mode != "Camera" (e.g., "USB"), camera resources are not initialized and the camera preview is not shown

### AC8: Scanning Status Indicator
- **Given** camera mode is active and the camera is running
- **When** the camera is actively capturing frames
- **Then** a "Scanning..." indicator is displayed on the UI to confirm the camera is operational
- **And** the indicator is hidden when the camera is stopped or inactive

---

## Scope

### In Scope
- CameraBarcodeReaderView integration in MainPage XAML
- CameraQrScannerService implementing IQrScannerService
- Debounce logic: 2-second same-payload suppression
- Camera permission handling on macOS (Info.plist NSCameraUsageDescription)
- Camera capability declaration on Windows (Package.appxmanifest)
- Permission denied error messaging for both platforms
- Conditional camera activation based on Scanner.Mode preference
- "Scanning..." indicator in UI
- Integration with IHmacValidator for payload validation
- Unit tests for debounce logic and service behavior
- Integration/manual test plan for camera hardware

### Out of Scope
- USB barcode scanner input (US0008)
- QR code generation
- Image file scanning (pick a photo and scan)
- Multi-camera selection (uses default/first available camera)
- Camera resolution or focus settings
- Night vision or torch/flashlight control
- Barcode formats other than QR (Code128, EAN, etc.)
- Scan result display and feedback UI (EP0003)
- Audio feedback on scan (EP0003)

---

## Technical Notes

### ZXing.Net.Maui Integration

```xml
<!-- MainPage.xaml -->
<zxing:CameraBarcodeReaderView
    x:Name="CameraView"
    IsDetecting="{Binding IsCameraActive}"
    BarcodesDetected="OnBarcodesDetected"
    Options="{Binding CameraBarcodeReaderOptions}" />
```

### Service Interface

```csharp
public interface IQrScannerService
{
    event EventHandler<QrScanEventArgs> QrCodeScanned;
    Task StartAsync();
    Task StopAsync();
    bool IsActive { get; }
}
```

### Debounce Implementation
- Track `lastProcessedPayload` (string) and `lastProcessedTime` (DateTime)
- On decode: if payload == lastProcessedPayload AND (now - lastProcessedTime) < 2 seconds, skip
- Otherwise: process, update tracking values
- Thread-safe implementation required (camera decode callback may fire from background thread)

### Platform Permissions

**macOS (Info.plist):**
```xml
<key>NSCameraUsageDescription</key>
<string>SmartLog Scanner needs camera access to scan student QR codes at the gate.</string>
```

**Windows (Package.appxmanifest):**
```xml
<Capabilities>
    <DeviceCapability Name="webcam" />
</Capabilities>
```

### Key Classes
- `IQrScannerService` - scanner abstraction interface in `lib/core/services/`
- `CameraQrScannerService` - camera implementation in `lib/features/scanning/`
- `QrScanEventArgs` - event args model in `lib/core/models/`
- MainPage XAML bindings for CameraBarcodeReaderView
- Platform-specific permission configs (Info.plist, Package.appxmanifest)

### Data Requirements
- **Input:** Raw camera frames (handled internally by ZXing.Net.Maui)
- **Output:** Decoded QR payload string, forwarded to IHmacValidator
- **Configuration:** Scanner.Mode preference from IPreferencesService (set during EP0001 setup)

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Camera permission denied by user | Display clear error message with instructions to enable in OS settings; camera preview area shows the error message instead of a black/empty view; do not crash |
| No camera hardware detected | Display error: "No camera detected. Please connect a camera or switch to USB scanner mode in Settings." Log warning via Serilog |
| Camera in use by another application | Display error: "Camera is in use by another application. Please close other apps using the camera and try again." Retry on returning to foreground |
| Low-light conditions causing decode failures | No special handling; ZXing.Net.Maui will simply not fire BarcodesDetected events; "Scanning..." indicator remains visible to show camera is still active |
| Partial QR code in frame (edge/corner only) | ZXing will not decode it; no event fired; normal behaviour |
| Multiple QR codes visible in frame simultaneously | Process only the first detected barcode result from the BarcodesDetected event; ignore additional codes in same frame |
| Camera disconnected mid-scan (USB webcam unplugged) | Handle ZXing exception/error event; display error: "Camera disconnected. Please reconnect the camera." Attempt to re-initialize when camera is reconnected |
| Rapid succession of different QR codes (< 2s apart) | Each unique payload is processed independently; debounce only suppresses same-payload repeats |
| Same QR scanned exactly at 2-second boundary | Use `>=` comparison (elapsed >= 2 seconds allows re-processing); borderline timing handled by >= not > |
| App goes to background and returns to foreground | Stop camera on background; restart camera on foreground resume to release/reacquire hardware resources |
| Scanner.Mode changed from "Camera" to "USB" while camera is active | Stop camera, release resources, hide preview; do not leave camera running in background |

---

## Test Scenarios

- [ ] Camera preview is displayed when Scanner.Mode = "Camera"
- [ ] Camera preview is NOT displayed when Scanner.Mode = "USB"
- [ ] QR code decoded from camera frame triggers QrCodeScanned event with correct payload
- [ ] Same QR payload decoded within 2 seconds is suppressed (debounce)
- [ ] Same QR payload decoded after 2 seconds is processed again
- [ ] Different QR payload decoded within 2 seconds of previous is processed immediately
- [ ] Decoded payload is forwarded to IHmacValidator.ValidateAsync()
- [ ] Invalid HMAC result from validator is not forwarded to scan submission pipeline
- [ ] "Scanning..." indicator is visible when camera is active
- [ ] "Scanning..." indicator is hidden when camera is stopped
- [ ] Camera permission denied: error message displayed with OS settings instructions
- [ ] No camera hardware: error message displayed suggesting USB mode
- [ ] Only QR barcode format is processed (non-QR barcodes are ignored)
- [ ] Multiple QR codes in frame: only first result is processed
- [ ] Debounce state resets when a different payload is scanned (no cross-contamination)
- [ ] Camera resources are released when mode switches to USB
- [ ] Camera restarts when app returns to foreground from background

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0006 | Blocked By | IHmacValidator interface and ValidateAsync() method for QR payload signature verification | Draft |
| US0001 | Blocked By | IPreferencesService for reading Scanner.Mode preference to determine if camera mode is active | Draft |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| ZXing.Net.Maui NuGet package | Library | Available (verify desktop camera support) |
| macOS camera hardware on gate machines | Hardware | Assumed available (built-in or USB webcam) |
| Windows camera hardware on gate machines | Hardware | Assumed available |
| NSCameraUsageDescription in Info.plist | Platform config | Must be added |
| Webcam DeviceCapability in Package.appxmanifest | Platform config | Must be added |

---

## Estimation

**Story Points:** 8
**Complexity:** High

**Rationale:** This story involves cross-platform camera hardware integration (macOS + Windows), third-party library integration (ZXing.Net.Maui with potential desktop compatibility concerns), platform-specific permission handling, real-time frame decoding, thread-safe debounce logic, and UI integration with the camera preview. Hardware-dependent testing on both platforms adds verification complexity. The ZXing.Net.Maui desktop support maturity is a noted risk.

---

## Open Questions

- [ ] Which ZXing.Net.Maui package variant provides the best desktop camera support? (ZXing.Net.Maui vs ZXing.Net.Maui.Controls) - Owner: Tech Lead
- [ ] Should there be a fallback if ZXing.Net.Maui does not work on desktop? (e.g., ZXing.Net with manual camera capture via platform APIs) - Owner: Tech Lead
- [ ] Should the debounce window (2 seconds) be configurable via Preferences, or hardcoded? Current decision: hardcoded constant, configurable later if needed. - Owner: Product
- [ ] What camera resolution should be targeted for optimal QR decoding performance? Default to ZXing.Net.Maui defaults unless testing shows issues. - Owner: Tech Lead

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
