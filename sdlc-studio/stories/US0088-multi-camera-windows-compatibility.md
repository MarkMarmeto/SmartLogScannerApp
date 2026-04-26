# US0088: Multi-Camera — Windows Platform Compatibility Verification

> **Status:** Done
> **Marked Done:** 2026-04-26
> **Epic:** EP0011: Multi-Camera Scanning (cross-project)
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-04-24

## User Story

**As a** Tony (IT Admin deploying to Windows gate PCs)
**I want** the multi-camera pipeline verified end-to-end on Windows — enumeration, concurrent decode, setup page, and runtime grid
**So that** schools running the scanner on Windows 10/11 get the same reliability as macOS development has been enjoying, with no camera-platform surprises on deploy day.

## Context

### Persona Reference
**Tony (IT Admin)** — Owns the on-prem Windows install at each school.
**Guard Gary** — Runs the scanner day-to-day.

### Background
EP0011 was developed primarily on macOS (`net8.0-maccatalyst`). Multi-camera services have a platform-specific seam: `ICameraEnumerationService` (Windows MediaFoundation vs macOS AVFoundation) and a per-platform `ICameraWorker` implementation. The macOS path has been exercised extensively; the Windows path compiles and passes unit tests but has not been run end-to-end against real USB webcams on Windows 10/11 at the multi-camera configuration level.

This story adds a verification sweep and fixes any platform-specific issues surfaced — it is **not** adding new features. If Windows passes cleanly, the deliverable is the test evidence + any trivial polish.

---

## Inherited Constraints

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| TRD | Platform | Windows 10 build 19041+ and Windows 11 are supported deployment targets | Verification targets both |
| EP0011 | Architecture | `IMultiCameraManager` orchestrates 1-8 cameras via platform workers | Enumerate, start, stop cycle must work on Windows |
| CLAUDE.md | Ops | USB webcams are the expected hardware; no IP cameras | Test fixtures use USB cameras |

---

## Acceptance Criteria

### AC1: Camera Enumeration on Windows
- **Given** a Windows 10/11 host with 2+ USB webcams attached
- **When** the setup page opens and lists cameras
- **Then** all physically connected cameras appear in the picker with stable identifiers across app restarts
- **And** camera names match what Windows Device Manager reports

### AC2: Concurrent Decode — Two Cameras
- **Given** two USB webcams configured on Windows
- **When** cameras are started from the main page
- **Then** both camera tiles display live video
- **And** QR scans from either camera are decoded and submitted independently
- **And** neither camera stalls the other

### AC3: Concurrent Decode — Four Cameras
- **Given** four USB webcams configured on Windows (or the maximum available)
- **When** all four are running simultaneously for at least 10 minutes
- **Then** `AdaptiveDecodeThrottle` keeps CPU usage within acceptable bounds (no thermal throttling, no frame freezes)
- **And** no camera worker crashes or silently stops

### AC4: Stop / Restart Cycle
- **Given** cameras are running on Windows
- **When** the user stops all cameras and restarts them
- **Then** all cameras resume without requiring an app restart
- **And** no `Access Denied` or device-busy errors are logged

### AC5: Device Disconnect Handling
- **Given** a Windows host with cameras running
- **When** a USB webcam is physically unplugged mid-session
- **Then** that camera's tile shows an error state (existing US0070 behaviour)
- **And** other cameras continue running unaffected
- **When** the camera is re-plugged
- **Then** it can be re-added from setup without restarting the app (or a clear "restart required" message is shown if that's the chosen path)

### AC6: Setup Page UX on Windows
- **Given** the multi-camera setup page on Windows
- **Then** camera picker dropdowns, preview, and save work identically to macOS
- **And** any Windows-specific dialogs (e.g. webcam permission consent) are surfaced appropriately

### AC7: Documentation Captures Windows Notes
- **Given** any Windows-specific caveat found during verification (e.g. driver expectations, permission prompts, known webcam models that misbehave)
- **Then** the findings are documented in `docs/` or the Scanner CLAUDE.md so future engineers don't rediscover them

---

## Scope

### In Scope
- End-to-end verification on Windows 10 and Windows 11 with 2 and 4 USB webcams
- Fix any Windows-specific bugs discovered (build, runtime, UI)
- Capture logs + screenshots as test evidence
- Update deploy/test docs with findings

### Out of Scope
- IP camera / RTSP support
- Windows Hello-style biometric integration
- Changes to enumeration contract or `IMultiCameraManager` public API (only platform-impl fixes)

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Webcam driver requires Windows privacy-settings approval | First-run prompt acknowledged; permission persists; documented |
| Two identical webcam models share the same name | Identifiers remain unique (use device path, not friendly name); documented in setup UI |
| Camera in use by another Windows app | Clear error on the affected tile, other cameras unaffected |
| Windows sleeps/resumes with cameras running | Cameras re-initialise on resume or surface a clear "restart" cue |

---

## Test Scenarios

- [ ] Verified on Windows 10 build 19041+ with 2 webcams
- [ ] Verified on Windows 10 build 19041+ with 4 webcams
- [ ] Verified on Windows 11 with 2 webcams
- [ ] 10-minute soak test on 4 cameras (CPU stable, no crashes)
- [ ] Unplug/replug cycle handled gracefully
- [ ] Stop/start cycle within the app
- [ ] Concurrent QR scans from different cameras submit independently
- [ ] Logs captured + findings documented in `docs/` or CLAUDE.md

---

## Technical Notes

### Pre-Verification Checklist
- Publish with `dotnet publish SmartLog.Scanner -f net8.0-windows10.0.19041.0 -c Release`
- Deploy to a real Windows gate PC (not just a VM; webcam pass-through in VMs is unreliable)
- Use `Logging.MinimumLevel=Debug` during verification to capture MediaFoundation diagnostics

### Likely Touch Points (if fixes are needed)
- `SmartLog.Scanner/Platforms/Windows/CameraEnumerationService.cs`
- `SmartLog.Scanner/Platforms/Windows/CameraWorker.cs` (or equivalent)
- MediaFoundation initialisation / COM threading model
- Webcam permission manifest (Package.appxmanifest if MSIX)

### Deliverables
- Test run report (dated, host model, Windows build, camera models, durations, outcomes)
- Fix commits (if any)
- Updated Scanner CLAUDE.md Windows-specific section

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| [US0066](US0066-multi-camera-manager-core.md) | Foundation | MultiCameraManager exists | Done |
| [US0067](US0067-adaptive-decode-throttle.md) | Foundation | Throttle exists | Done |
| [US0068](US0068-main-page-camera-grid-ui.md) | Foundation | Grid UI exists | Done |
| [US0070](US0070-error-isolation-and-recovery.md) | Foundation | Error isolation exists | Done |

---

## Estimation

**Story Points:** 3
**Complexity:** Low-Medium — testing-heavy, with contingency for platform-specific fixes

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-24 | Claude | Initial story drafted from V2 planning session |
