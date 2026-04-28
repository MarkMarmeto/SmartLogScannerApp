# EP0012: Concurrent Multi-Modal Scanning

> **Status:** Draft
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-04-28
> **Target Release:** 2.2.0

## Summary

Allow a single scanner device to operate cameras and a USB keyboard-wedge barcode scanner **simultaneously** rather than mutually exclusively. Today the app runs in either `Camera` mode OR `USB` mode, set at setup time. This epic introduces a `Both` mode where webcams and a USB scanner emit scans into a unified pipeline, share deduplication, and are surfaced as peer "input slots" on the main page — the USB scanner appearing as its own indicator card alongside the camera slot cards.

## Inherited Constraints

> See PRD and TRD for full constraint details. Key constraints for this epic:

| Source | Type | Constraint | Impact |
|--------|------|------------|--------|
| PRD | Performance | Per-scan total processing < 50ms after capture | USB and camera pipelines must remain independent — neither blocks the other |
| PRD | UX | Zero decision-making during scanning for Guard Gary | Operator should not need to think about which input "owns" the scan; visual feedback is identical regardless of source |
| TRD | Architecture | MVVM with layered services, DI via MAUI container | `MainViewModel` orchestrates both `IMultiCameraManager` and `UsbQrScannerService` simultaneously |
| TRD | Tech Stack | Cross-platform (macOS MacCatalyst + Windows) | USB keystroke capture works on both platforms; concurrent camera + USB tested on Windows hardware |
| EP0011 | Architecture | Cross-camera dedup via singleton `ScanDeduplicationService` | USB scans participate in the same dedup window as camera scans (3s/60s/300s tiers) |

---

## Business Context

### Problem Statement

Schools deploying SmartLog Scanner often have heterogeneous hardware at a single gate: 1–3 webcams aimed at student lanes plus a handheld USB barcode scanner used by Guard Gary for ID cards held up close, in bright sunlight, or for visitor passes. Today the app forces a choice between Camera and USB at setup time — meaning the handheld scanner is unusable on a multi-webcam gate, and IT Admin Ian must pick one input modality per device.

This forces compromises: gates that need both modalities buy a second scanner PC, or operators carry the visitor-pass workflow on a separate device.

**PRD Reference:** [Feature Inventory](../prd.md#3-feature-inventory) — extension of F02 (Camera Scanning) and F03 (USB Scanning)

### Value Proposition

A single scanner device can serve every input scenario at the gate. Guard Gary can scan student IDs from cameras passively while still using the handheld scanner for visitor passes or close-up reads — without needing to switch modes, restart the app, or move to a different machine. Cross-source deduplication ensures a student walking past a camera and then having their ID re-scanned by a guard with the handheld doesn't double-count.

### Success Metrics

| Metric | Current | Target | Measurement |
|--------|---------|--------|-------------|
| Single-device coverage of mixed-input gates | 0% (forced to pick one mode) | 100% | All gate hardware combinations work on one PC |
| Operator switching overhead | Manual mode change in settings + app restart | Zero | No mode change ever needed; both inputs always live |
| Cross-source duplicate scans (within 3s window) | N/A (modes are exclusive) | 0 duplicates submitted to server | Scan log review under simulated overlap |
| USB scanner "still alive" diagnostics | None — silent until first scan | Visual indicator within 60s if no scans received | Manual test: unplug USB scanner, confirm warning state appears |

---

## Scope

### In Scope

- New `Both` value for `Scanner.Mode` preference alongside existing `Camera` and `USB`
- `MainViewModel` simultaneously starts and subscribes to events from `IMultiCameraManager` AND `UsbQrScannerService`
- Setup wizard exposes a "Also accept USB scanner input" option enabling concurrent operation
- USB scanner indicator slot card on the main page — sibling to camera slot cards, distinct visual styling (icon, accent color, condensed layout)
- 60-second no-scan health heuristic on the USB indicator: shows a soft warning state ("No recent scans") if no scan from the USB source has arrived within the threshold
- Scan log parity: USB scans persist to `ScanLogs` with the same metadata fields as camera scans (`ScanMethod = "USB"`, source identifier, timestamp, etc.), so admin dashboards see all scans uniformly
- Cross-source deduplication via existing `ScanDeduplicationService` (already a singleton — both pipelines use it natively)
- Backward compatibility: existing `Camera` and `USB` exclusive modes continue to work for installs that haven't migrated

### Out of Scope

- Plug/unplug detection for USB scanners (Windows USB device enumeration API integration) — relies on the 60s heuristic only
- Multiple simultaneous USB scanners on one device (only one USB scanner supported, as today)
- Bluetooth barcode scanners
- Per-USB-scanner ENTRY/EXIT override (USB scanner inherits the device-level scan type, same as cameras after US0089)
- Audio differentiation between camera and USB scans (same beeps as today)
- HID-mode (non-keyboard-wedge) USB scanners

### Affected Personas

- **Guard Gary:** Primary — gains the ability to scan via webcam OR handheld at the same gate; sees the USB indicator card so he knows the scanner is alive even when no one's scanning
- **IT Admin Ian:** Configures concurrent mode in setup; benefits from one PC handling all gate hardware; uses 60s heuristic to diagnose scanner connectivity remotely (via heartbeat dashboard)

---

## Acceptance Criteria (Epic Level)

- [ ] `Scanner.Mode = "Both"` starts both camera workers and USB keystroke listener at app launch
- [ ] Existing `Camera` and `USB` exclusive modes continue to function unchanged (no regression)
- [ ] Setup wizard offers a "Also accept USB scanner input" option that, when enabled, persists `Scanner.Mode = "Both"`
- [ ] When a webcam decodes a QR, the scan flow is identical to today (camera slot flash, student card update, history log, server submit)
- [ ] When the USB scanner sends a payload while in `Both` mode, the scan flow is identical (same student card update, history log, server submit) and the USB indicator card flashes for 3 seconds
- [ ] Cross-source dedup: scanning the same QR via webcam then USB within 3 seconds produces only one server submission
- [ ] USB indicator card renders alongside camera slot cards on the main page — visually distinct (different icon, accent color), shows "Listening" when idle
- [ ] If no USB scan arrives within 60 seconds, the USB indicator card transitions to a warning state ("No recent scans") with amber accent
- [ ] Receiving any USB scan clears the warning state and resets the 60s timer
- [ ] USB scans appear in `ScanLogs` table with `ScanMethod = "USB"` and the same set of fields populated as camera scans (timestamp, payload, status, processing time, network availability)
- [ ] USB scanner stays in USB-only mode is unaffected by the new card (card only renders in `Both` or `USB` modes — confirmed via UI visibility logic)
- [ ] Indicator card hidden in `Camera`-only mode
- [ ] Heartbeat service reports USB scanner health (last-scan-age) alongside camera health

---

## Dependencies

### Blocked By

| Dependency | Type | Status | Owner |
|------------|------|--------|-------|
| US0007 (Camera-Based QR Scanning) | Story | Done | AI Assistant |
| US0008 (USB Barcode Scanner Input) | Story | Done | AI Assistant |
| US0066 (Multi-Camera Manager Core) | Story | Done | AI Assistant |
| US0068 (Main Page Camera Grid UI) | Story | Done | AI Assistant |
| US0089 (Unify Scan Type to Device-Level) | Story | Done | AI Assistant |

### Blocking

| Item | Type | Impact |
|------|------|--------|
| None — pure scanner-side enhancement; server contract unchanged | — | — |

---

## Risks & Assumptions

### Assumptions

- USB keyboard-wedge scanners continue to emit keystrokes globally to the focused window — `MainPage` retains focus while cameras are also active
- The keystroke buffer logic in `UsbQrScannerService` does not interfere with camera UI controls (e.g., picker dropdowns, text inputs) when both are running
- The OS does not throttle keyboard event delivery when MediaCapture sessions are active on the same machine
- `ScanDeduplicationService` is already singleton-scoped (verified — see `MauiProgram.cs`) so cross-source dedup is automatic
- The 60s threshold is a reasonable default; can be tuned per deployment if needed

### Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| USB keystrokes captured while user is editing setup form fields | Medium | Medium | USB listener should be inactive on `SetupPage`; only `MainPage` consumes keystrokes |
| MainPage focus loss (modal dialogs, alerts) silently breaks USB capture | Medium | Medium | `OnAppearing` re-attaches focus; explicit re-focus after dismissing dialogs |
| 60s heuristic fires false-positive during a quiet period | Low | Low | "No recent scans" is informational, not error — does not stop the scanner from working |
| Concurrent camera + USB on low-spec hardware exceeds CPU budget | Low | Medium | USB pipeline is lightweight (event-driven, no decode work); cameras already throttle adaptively |
| Existing single-mode users broken by mode-handling refactor | Medium | High | Preserve `Camera` and `USB` enum values; add `Both` as third option; default migration: leave existing prefs untouched |

---

## Technical Considerations

### Architecture Impact

- `Scanner.Mode` preference gains a third valid value: `"Both"`. Code paths checking `_scannerMode == "Camera"` must be audited and updated where they need to also enable USB.
- `MainViewModel` constructor currently subscribes to `_multiCameraManager` events OR `_usbScanner.ScanCompleted` events based on mode — refactor to allow simultaneous subscription.
- `MainPage.xaml.cs` currently attaches the keyboard listener (`OnPageFocused`) only in USB mode — extend to attach in `Both` mode too.
- New `UsbScannerSlotState` ViewModel sibling to `CameraSlotState`, with USB-specific state (last-scan-age, listening status) and shared properties (DisplayName, ScanType, ShowFlash, FlashColor, FlashStudentName, LastScanMessage).
- New `UsbScannerSlotView` `DataTemplate` in `MainPage.xaml` rendered alongside the camera FlexLayout with distinct styling.
- `MainViewModel.LogScanToHistoryAsync` already uses `ScanMethod = _scannerMode`; needs slight tweak so USB scans in `Both` mode log as `"USB"` (not `"Both"`) and camera scans log as `"Camera"` (not `"Both"`) — source identification per scan, not per app session.

### Integration Points

- `UsbQrScannerService.ScanCompleted` event — existing
- `IMultiCameraManager.ScanCompleted` event — existing
- `IScanDeduplicationService` (singleton) — automatic cross-source dedup
- `IHeartbeatService` — reports per-device health; USB scanner becomes a reportable peer device alongside cameras
- `MAUI Preferences` — new value for `Scanner.Mode` key

### UI/UX Notes

USB indicator card visual differentiation from camera cards:
- Icon: ⌨️ or barcode glyph (not 📷)
- Accent stroke: indigo/purple (`#6A4C93`-ish), not teal
- No "fps" / frame rate display (irrelevant)
- No "Restart" button (HID device — nothing to restart)
- "Listening" / "No recent scans (Xs)" / scan-flash states only

---

## Sizing

**Story Points:** 13
**Estimated Story Count:** 3

**Complexity Factors:**
- Cross-cutting `Scanner.Mode` enum extension affects multiple files
- Concurrent input source management without race conditions in event handlers
- New UI component with custom styling and idle-state heuristic timer
- Cross-platform USB capture compatibility while cameras run (especially Windows MediaCapture + keyboard event interaction)

---

## Story Breakdown

- [ ] [US0121: Concurrent Camera + USB Scanner Mode](../stories/US0121-concurrent-camera-usb-scanner-mode.md)
- [ ] [US0122: Setup Wizard — Concurrent Scanner Mode Configuration](../stories/US0122-setup-wizard-concurrent-mode-config.md)
- [ ] [US0123: USB Scanner Indicator Slot with Health Heuristic](../stories/US0123-usb-scanner-indicator-slot.md)

---

## Test Plan

> Test spec to be generated via `/sdlc-studio test-spec --epic EP0012`

Key scenarios to cover:
- Scan via webcam, then USB within 3s — only one server submit (cross-source dedup)
- Scan via USB while no cameras running (`USB` mode) — indicator card visible, behaves correctly
- Scan via USB while cameras also running (`Both` mode) — indicator card shows alongside camera cards
- Idle USB for 60s — warning state appears; arriving scan clears it
- Setup wizard: enable concurrent mode, save, restart app — both pipelines start
- Backward compat: existing `Camera`-only and `USB`-only installs still work after upgrade

---

## Open Questions

- [x] **Resolved 2026-04-28:** 60s threshold is **hard-coded** for v1. Configurable threshold deferred until a deployment specifically requests it.
- [x] **Resolved 2026-04-28:** Warning state **stays until next scan arrives** — no auto-clear, no dismissal. Only proof of life clears the warning.
- [x] **Resolved 2026-04-28:** Heartbeat payload uses a **dedicated `usbScannerLastScanAge` field** at the top level, separate from the cameras array. Cameras and USB have honestly distinct schemas.
- [x] **Resolved 2026-04-28:** macOS keystroke-vs-camera-preview interaction will be **verified during US0121 hardware testing** on Mac. Fallback plan: replace the hidden `UITextField` capture with `NSEvent.AddLocalMonitorForEvents` (application-level keyboard event monitor) if the existing approach fights the camera preview's first-responder chain.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-28 | SDLC Studio | Initial epic created — concurrent camera + USB scanner support |
| 2026-04-28 | SDLC Studio | All 4 epic open questions resolved — 60s hard-coded, warning persists until scan, dedicated heartbeat field, macOS keystroke verification deferred to implementation with NSEvent fallback |
