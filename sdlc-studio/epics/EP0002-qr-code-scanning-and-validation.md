# EP0002: QR Code Scanning and Validation

> **Status:** Done
> **Completed:** 2026-02-13
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-02-13
> **Target Release:** 1.0.0

## Summary

Implement the QR code input pipeline — both camera-based scanning via ZXing.Net.Maui and USB barcode scanner keyboard wedge input — plus local HMAC-SHA256 signature validation. This epic delivers the core input mechanism that Guard Gary uses to scan student QR codes at the gate.

## Inherited Constraints

> See PRD and TRD for full constraint details. Key constraints for this epic:

| Source | Type | Constraint | Impact |
|--------|------|------------|--------|
| PRD | Performance | Camera QR decode at >= 15 fps; USB input processing < 50ms | Decoding must be non-blocking; keyboard buffer needs fast processing |
| PRD | Security | HMAC comparison via CryptographicOperations.FixedTimeEquals() | Constant-time comparison to prevent timing attacks |
| TRD | Architecture | MVVM — scanning services injected into MainViewModel | IQrScannerService interface abstracts camera vs USB mode |
| TRD | Tech Stack | ZXing.Net.Maui for camera; raw key input for USB wedge | Platform permissions required (NSCameraUsageDescription, appxmanifest) |

---

## Business Context

### Problem Statement
Guards need to scan student QR codes quickly and reliably using either a webcam or USB barcode scanner. Before any scan reaches the server, the QR code's HMAC signature must be verified locally to reject forged or tampered codes instantly — saving network bandwidth and preventing invalid submissions.

**PRD Reference:** [Feature Inventory](../prd.md#3-feature-inventory)

### Value Proposition
Guard Gary points the scanner at a QR code (or uses a USB scanner) and gets an instant local validation result. Invalid codes are rejected in under 100ms without touching the network. This speed and reliability during the 30-minute peak windows is critical for processing hundreds of students without delays.

### Success Metrics

| Metric | Current | Target | Measurement |
|--------|---------|--------|-------------|
| Camera decode rate | N/A | >= 15 fps continuous | Performance profiling on target hardware |
| USB scan processing time | N/A | < 50ms after complete payload | Stopwatch measurement from last keystroke to validation |
| Invalid QR rejection time | N/A | < 100ms (local only) | Measure HMAC validation duration |
| False rejection rate | N/A | 0% for valid QR codes | Test with valid HMAC-signed payloads |

---

## Scope

### In Scope
- Camera preview display in main scan area (ZXing.Net.Maui)
- Continuous QR frame decoding with 2-second same-payload debounce
- Camera permission handling (macOS NSCameraUsageDescription, Windows appxmanifest webcam capability)
- USB keyboard wedge input listener with ~100ms inter-keystroke timeout
- QR pattern matching for SMARTLOG:{studentId}:{timestamp}:{hmacBase64} format
- HMAC-SHA256 local validation using shared secret from SecureStorage
- Constant-time signature comparison via CryptographicOperations.FixedTimeEquals()
- QR payload parsing: split by ":" expecting exactly 4 parts, prefix "SMARTLOG"
- IQrScannerService interface abstracting camera and USB modes
- Scan mode switching (camera ↔ USB) based on configuration

### Out of Scope
- Bluetooth barcode scanners
- QR code generation (handled by admin web app)
- Image-from-file QR scanning
- Multi-QR batch scanning
- QR code timestamp expiry validation (pending open question)

### Affected Personas
- **Guard Gary:** Primary user — scans QR codes at the gate, needs instant feedback on invalid codes
- **IT Admin Ian:** Selects scan mode (camera vs USB) during device setup

---

## Acceptance Criteria (Epic Level)

- [ ] Camera mode: live preview with continuous QR decoding at >= 15 fps
- [ ] Camera mode: duplicate reads of same payload debounced within 2 seconds
- [ ] Camera permissions requested and handled on both macOS and Windows
- [ ] USB mode: keyboard wedge input captured with ~100ms inter-keystroke timeout
- [ ] USB mode: complete QR payload auto-processed through validation pipeline
- [ ] USB mode: "Ready to Scan" displayed when waiting for input
- [ ] QR payload parsed correctly: SMARTLOG:{studentId}:{unixTimestamp}:{hmacBase64}
- [ ] Malformed QR payloads (wrong prefix, wrong part count) rejected immediately
- [ ] Valid HMAC signature → payload forwarded to scan submission pipeline
- [ ] Invalid HMAC signature → immediate rejection with red feedback, NOT sent to server
- [ ] HMAC comparison uses CryptographicOperations.FixedTimeEquals() (constant-time)
- [ ] Scan mode (camera/USB) determined by configuration set during setup

---

## Dependencies

### Blocked By

| Dependency | Type | Status | Owner |
|------------|------|--------|-------|
| EP0001: Device Setup and Configuration | Epic | Draft | Unassigned |
| F12: Secure Config Storage | Feature | Not Started | — |

### Blocking

| Item | Type | Impact |
|------|------|--------|
| EP0003: Scan Processing & Feedback | Epic | Needs validated QR payload as input to scan submission |

---

## Risks & Assumptions

### Assumptions
- ZXing.Net.Maui supports desktop (non-mobile) camera access on both macOS and Windows
- USB barcode scanners in keyboard wedge mode send keystrokes fast enough to distinguish from manual typing
- HMAC secret key is available in SecureStorage before scanning begins (setup completed)
- Camera hardware is available on gate machines (may be built-in or external USB webcam)

### Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| ZXing.Net.Maui desktop camera support limited or buggy | Medium | High | Test early on both platforms; fallback to ZXing.Net with manual camera integration |
| USB keyboard wedge timing varies by scanner model | Medium | Medium | Make inter-keystroke timeout configurable; test with multiple scanner models |
| Camera permission denied by OS security settings | Low | High | Clear error message guiding user to system preferences |
| HMAC secret key format mismatch with admin panel | Low | High | Document expected format; validate during setup test connection |

---

## Technical Considerations

### Architecture Impact
- Introduces IQrScannerService interface with CameraQrScannerService and UsbQrScannerService implementations
- Adds HmacValidator (or HmacHelper) service for local QR signature verification
- Camera mode requires XAML CameraBarcodeReaderView binding in MainPage
- USB mode requires keyboard event interception at the page level
- Both modes feed into a common IScanProcessor pipeline

### Integration Points
- MAUI SecureStorage — retrieve HMAC secret key for validation
- ZXing.Net.Maui — camera access and QR decoding
- Platform permissions — NSCameraUsageDescription (macOS), webcam capability (Windows appxmanifest)
- Downstream: validated payloads forwarded to F05 Scan Submission or F07 Offline Queue

---

## Sizing

**Story Points:** 21
**Estimated Story Count:** 3

**Complexity Factors:**
- Cross-platform camera access via ZXing.Net.Maui (macOS + Windows)
- USB keyboard wedge timing logic with configurable timeout
- Constant-time HMAC comparison security requirement
- Two distinct input modes behind a common interface

---

## Story Breakdown

- [ ] [US0006: Implement Local HMAC-SHA256 QR Validation](../stories/US0006-local-hmac-qr-validation.md)
- [ ] [US0007: Implement Camera-Based QR Scanning](../stories/US0007-camera-based-qr-scanning.md)
- [ ] [US0008: Implement USB Barcode Scanner Keyboard Wedge Input](../stories/US0008-usb-barcode-scanner-input.md)

---

## Test Plan

> Test spec to be generated via `/sdlc-studio test-spec --epic EP0002`

---

## Open Questions

- [ ] Which specific ZXing.Net.Maui package variant works best on MAUI desktop (non-mobile)? - Owner: Tech Lead
- [ ] What is the HMAC secret key format from the admin panel (raw bytes, hex, base64)? - Owner: Backend Team
- [ ] Should QR code timestamp expiry be validated locally? - Owner: Product

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial epic created from PRD features F02, F03, F04 |
