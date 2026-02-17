# US0008: Implement USB Barcode Scanner Keyboard Wedge Input

> **Status:** Draft
> **Epic:** [EP0002: QR Code Scanning and Validation](../epics/EP0002-qr-code-scanning-and-validation.md)
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-02-13

## User Story

**As** Guard Gary
**I want** the app to automatically capture QR codes scanned with a USB barcode scanner and validate them instantly
**So that** I can use a dedicated handheld scanner for fast, reliable scanning without needing to aim a webcam, especially in bright outdoor conditions at the gate

## Context

### Persona Reference
**Guard Gary** - School security guard, novice technical proficiency, needs instant visual feedback (green = good, red = bad) with zero decision-making during scanning.
[Full persona details](../personas.md#guard-gary)

### Background
USB barcode scanners operating in keyboard wedge mode are a common alternative to camera-based scanning. These devices read a barcode and rapidly inject the decoded characters as simulated keystrokes, typically followed by an Enter/Return key. The challenge is distinguishing this rapid machine input from normal human keyboard typing. The app uses an inter-keystroke timing threshold (~100ms) to detect scanner input: if characters arrive faster than a human can type, they are buffered as a scanner payload. When a complete payload is received (terminated by Enter key or by timeout with a valid SMARTLOG pattern), it is forwarded through the IHmacValidator (US0006) pipeline. This mode is active only when Scanner.Mode = "USB" is set in Preferences (configured during EP0001 setup by IT Admin Ian). The USB scanner mode works on both macOS (MacCatalyst) and Windows (WinUI 3).

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | Performance | USB input processing < 50ms after complete payload | From last keystroke/Enter to validation result must be < 50ms |
| PRD | UX | Zero decision-making during scanning for Guard Gary | Auto-detect scanner input; no manual submit button required |
| TRD | Architecture | IQrScannerService interface abstracts camera vs USB mode | USB scanning implemented as UsbQrScannerService behind IQrScannerService |
| TRD | Tech Stack | Raw key input handler for USB wedge | Platform-level key event interception on MainPage |
| Epic | Behaviour | USB mode: "Ready to Scan" displayed when waiting | Visual indicator showing system is ready for scanner input |

---

## Acceptance Criteria

### AC1: Keyboard Event Interception
- **Given** Scanner.Mode = "USB" in Preferences and MainPage is displayed
- **When** keyboard key events occur on the MainPage
- **Then** the app intercepts all key-down events at the page level
- **And** each keystroke character is captured for potential scanner input processing
- **And** keyboard interception works on both macOS (MacCatalyst) and Windows (WinUI 3)

### AC2: Rapid Keystroke Buffering with Inter-Keystroke Timeout
- **Given** USB scanner mode is active and key events are being captured
- **When** keystrokes arrive with less than ~100ms between consecutive keys
- **Then** the characters are accumulated in an input buffer as scanner input
- **And** if no new keystroke arrives within ~100ms after the last character, the buffer timeout fires
- **And** the timeout duration is approximately 100ms (exact value may be tuned but should be between 80-150ms)

### AC3: Payload Completion via Enter Key
- **Given** characters are being accumulated in the scanner input buffer
- **When** an Enter or Return key is received
- **Then** the buffered payload is immediately considered complete
- **And** the complete payload string is forwarded to the validation pipeline
- **And** the input buffer is cleared for the next scan

### AC4: Payload Completion via Timeout with Valid Pattern
- **Given** characters are being accumulated in the scanner input buffer
- **When** the inter-keystroke timeout (~100ms) fires without an Enter key
- **Then** the buffered content is checked for the SMARTLOG: prefix pattern
- **And** if the buffer starts with "SMARTLOG:", it is treated as a complete payload and forwarded to validation
- **And** if the buffer does not start with "SMARTLOG:", it is discarded as non-scanner input (e.g., accidental keystrokes)
- **And** the input buffer is cleared after processing or discarding

### AC5: Payload Forwarded to HMAC Validation
- **Given** a complete QR payload has been captured from the USB scanner
- **When** the payload is ready for processing
- **Then** it is passed to IHmacValidator.ValidateAsync() (US0006) for HMAC-SHA256 signature verification
- **And** the validation result (success or rejection) is forwarded to the scan processing pipeline via an event or callback
- **And** invalid payloads (per HMAC validation) are not sent to any server endpoint

### AC6: Manual Typing Filtered Out
- **Given** USB scanner mode is active
- **When** a user types slowly on the keyboard (> 100ms between keystrokes)
- **Then** each slow keystroke triggers the inter-keystroke timeout
- **And** individual characters or short slow-typed fragments are discarded (they won't match SMARTLOG: pattern)
- **And** the scanner buffer does not accumulate slow manual typing into a false payload

### AC7: Ready to Scan Indicator
- **Given** USB scanner mode is active and waiting for input
- **When** no scan is currently being processed
- **Then** a "Ready to Scan" message is displayed prominently on the MainPage
- **And** the message is visible to Guard Gary to confirm the system is ready to receive scanner input
- **And** the message is replaced by scan results when a scan is processed, then returns after the result display clears

### AC8: USB Mode Conditional Activation
- **Given** the Preferences contain Scanner.Mode setting
- **When** Scanner.Mode = "USB"
- **Then** USB keyboard wedge input listening is active
- **And** when Scanner.Mode != "USB" (e.g., "Camera"), the keyboard event listener is not attached and no keystroke buffering occurs

---

## Scope

### In Scope
- UsbQrScannerService implementing IQrScannerService
- Page-level keyboard event interception on MainPage (both platforms)
- Input buffer with configurable inter-keystroke timeout (~100ms)
- Payload completion detection (Enter key or timeout with SMARTLOG: pattern)
- Manual typing rejection (slow keystrokes discarded)
- "Ready to Scan" UI indicator
- Conditional activation based on Scanner.Mode preference
- Integration with IHmacValidator for payload validation
- Cross-platform key event handling (macOS + Windows)
- Unit tests for buffer logic, timeout behavior, and filtering
- Integration test plan for physical USB scanner hardware

### Out of Scope
- Camera-based scanning (US0007)
- Bluetooth barcode scanners
- USB scanner device detection or enumeration (the scanner just sends keystrokes)
- Scanner configuration (baud rate, symbology settings - these are set on the scanner hardware itself)
- HID-mode USB scanners (non-keyboard-wedge protocols)
- Custom keystroke mapping or character encoding beyond standard ASCII/UTF-8
- Scan result display and feedback UI (EP0003)
- Audio feedback on scan (EP0003)

---

## Technical Notes

### Keyboard Wedge Concept
USB barcode scanners in keyboard wedge mode appear to the OS as a standard USB keyboard. When the scanner reads a barcode, it "types" the barcode content character by character at high speed (typically 2-10ms between characters), optionally followed by an Enter keystroke. The app must intercept these keystrokes before any other text input handler processes them.

### Platform Key Event Handling

**macOS (MacCatalyst):**
- Use UIKeyCommand or override key event handling at the UIViewController level
- May require platform-specific code via conditional compilation or handler

**Windows (WinUI 3):**
- Use KeyDown event on the Page or Window level
- CoreWindow.KeyDown or XAML Page.KeyDown

### Input Buffer State Machine

```
States: IDLE -> BUFFERING -> PROCESSING -> IDLE

IDLE:
  - Key received with < 100ms since last key (or first key) -> Start buffer, transition to BUFFERING
  - Display "Ready to Scan"

BUFFERING:
  - Key received within 100ms -> Append to buffer, reset timer
  - Enter key received -> Transition to PROCESSING
  - Timer expires (100ms no input) -> Check buffer for SMARTLOG: prefix
    - Has prefix -> Transition to PROCESSING
    - No prefix -> Discard buffer, transition to IDLE

PROCESSING:
  - Forward payload to IHmacValidator.ValidateAsync()
  - Clear buffer
  - Fire QrCodeScanned event
  - Transition to IDLE
```

### Service Interface
Reuses the same IQrScannerService interface as US0007:

```csharp
public interface IQrScannerService
{
    event EventHandler<QrScanEventArgs> QrCodeScanned;
    Task StartAsync();
    Task StopAsync();
    bool IsActive { get; }
}
```

### Key Classes
- `UsbQrScannerService` - USB keyboard wedge implementation in `lib/features/scanning/`
- `KeystrokeBuffer` - internal buffer class with timing logic in `lib/features/scanning/`
- MainPage key event wiring (platform-specific handlers)
- Tests in `test/features/scanning/UsbQrScannerServiceTests.cs` and `test/features/scanning/KeystrokeBufferTests.cs`

### Data Requirements
- **Input:** Raw keystroke events (character + timestamp)
- **Output:** Assembled QR payload string, forwarded to IHmacValidator
- **Configuration:** Scanner.Mode preference from IPreferencesService (set during EP0001 setup)
- **Internal State:** Character buffer (StringBuilder), last keystroke timestamp, timer handle

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Manual typing: slow keystrokes (> 100ms apart) | Each keystroke triggers timeout; single characters or short fragments without SMARTLOG: prefix are discarded silently; no false payload assembled |
| Partial scan: scanner disconnected mid-payload | Incomplete buffer will timeout after ~100ms; if buffer starts with "SMARTLOG:" but is incomplete, it is forwarded to IHmacValidator which will reject it as malformed; if no SMARTLOG: prefix, buffer is discarded |
| Scanner sends Enter key at end of payload | Enter key terminates input immediately; payload forwarded to validation without waiting for timeout |
| Scanner does NOT send Enter key at end | Timeout fires after ~100ms of no input; buffer checked for SMARTLOG: prefix; if present, forwarded to validation |
| Very fast scanner (< 10ms between keystrokes) | All characters accumulated normally; 100ms timeout still applies; no issues with rapid input |
| Special characters in payload (e.g., +, /, = from base64) | Characters captured as-is; base64 characters in HMAC field are valid keystroke characters |
| Backspace key during input | Ignored in buffer (do not delete previously buffered characters); USB scanners should not send backspace, but if they do, treat it as a non-printable and skip |
| Focus lost from app window during scan | Keystrokes are not received while app is not focused; partial buffer discarded on timeout; "Ready to Scan" remains visible; no error displayed (focus loss is silent) |
| Multiple USB scanners connected simultaneously | Both scanners inject keystrokes into the same keyboard event stream; keystrokes are interleaved; this may produce garbled payloads that will fail HMAC validation; log a warning if interleaved input is detected (rapid different-character sequences) |
| Payload exceeding reasonable buffer limit (> 2KB) | Discard buffer and log warning: "Scanner input exceeded maximum buffer size"; reset to IDLE state; prevents memory issues from runaway input |
| Tab key or other control characters in input | Non-printable control characters (except Enter) are ignored and not added to buffer |
| Rapid sequential scans (two QR codes scanned back-to-back) | First payload completed by Enter key or timeout, forwarded to validation; buffer cleared; second scan starts fresh buffering |

---

## Test Scenarios

- [ ] Rapid keystrokes (< 100ms apart) are accumulated into the buffer
- [ ] Enter key terminates input and triggers payload processing
- [ ] Timeout (~100ms) with SMARTLOG: prefix triggers payload processing
- [ ] Timeout (~100ms) without SMARTLOG: prefix discards buffer silently
- [ ] Slow keystrokes (> 100ms apart) are not accumulated into a single payload
- [ ] Complete valid SMARTLOG payload forwarded to IHmacValidator.ValidateAsync()
- [ ] Invalid HMAC result from validator is not forwarded to scan submission pipeline
- [ ] "Ready to Scan" indicator is visible when USB mode is active and idle
- [ ] "Ready to Scan" indicator is NOT visible when Scanner.Mode = "Camera"
- [ ] USB keyboard listener is NOT active when Scanner.Mode = "Camera"
- [ ] Backspace key in input stream is ignored (not processed as delete)
- [ ] Non-printable control characters (Tab, Escape) are excluded from buffer
- [ ] Buffer is cleared after each completed scan (no carryover between scans)
- [ ] Buffer exceeding 2KB is discarded with a warning log
- [ ] Two sequential scans are processed independently (buffer resets between them)
- [ ] Partial scan (no Enter, incomplete payload without SMARTLOG: prefix) is discarded after timeout
- [ ] Partial scan with SMARTLOG: prefix but incomplete payload is forwarded to validator (which rejects it)

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0006 | Blocked By | IHmacValidator interface and ValidateAsync() method for QR payload signature verification | Draft |
| US0001 | Blocked By | IPreferencesService for reading Scanner.Mode preference to determine if USB mode is active | Draft |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| USB barcode scanner hardware (keyboard wedge mode) | Hardware | Must be available for integration testing |
| macOS key event handling API (UIKit/AppKit via MacCatalyst) | Platform API | Available |
| Windows key event handling API (WinUI 3 KeyDown) | Platform API | Available |

---

## Estimation

**Story Points:** 8
**Complexity:** High

**Rationale:** This story requires cross-platform keyboard event interception with platform-specific implementations (macOS MacCatalyst vs Windows WinUI 3), precise timing-based input classification (distinguishing scanner from keyboard), an input buffer state machine with timeout logic, and thread-safety considerations for keystroke handling. Testing requires physical USB scanner hardware on both platforms. The timing sensitivity (~100ms threshold) needs tuning and may vary across scanner models.

---

## Open Questions

- [ ] What is the exact inter-keystroke timeout value? The ~100ms default may need tuning per scanner model. Should this be configurable in Preferences? - Owner: Tech Lead
- [ ] Do all target USB scanners send an Enter key suffix, or do some models omit it? This affects whether timeout-based completion is the primary or fallback path. - Owner: IT Admin Ian (hardware procurement)
- [ ] Should the app provide visual feedback that it is receiving keystrokes during buffering (e.g., brief "Scanning..." flash), or should it remain on "Ready to Scan" until the complete payload is assembled? Current decision: remain on "Ready to Scan" since buffering completes in < 200ms. - Owner: Product
- [ ] How should interleaved input from two simultaneous USB scanners be handled? Current decision: accept the garbled payload and let HMAC validation reject it. - Owner: Tech Lead
- [ ] On macOS MacCatalyst, what is the best API for intercepting keyboard events at the page/window level? (UIKeyCommand vs pressesBegan vs other) - Owner: Tech Lead

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
