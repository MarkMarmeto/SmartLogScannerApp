# PL0014: Multi-Camera — Windows Platform Compatibility Verification

> **Status:** In Progress
> **Story:** [US0088: Multi-Camera — Windows Platform Compatibility Verification](../stories/US0088-multi-camera-windows-compatibility.md)
> **Epic:** EP0011: Multi-Camera Scanning
> **Created:** 2026-04-25
> **Language:** C# 12 / .NET MAUI 8.0 (Windows target)

## Overview

End-to-end verification of the multi-camera pipeline on Windows 10/11 with real USB webcams. This is a test-and-fix story: run predefined verification scenarios; document findings; apply any platform-specific fixes discovered. If all scenarios pass cleanly the deliverable is the test evidence and updated docs — no code changes. If fixes are needed, they are committed with the verification evidence.

---

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | Camera enumeration | All connected USB webcams appear with stable identifiers on Windows 10/11 |
| AC2 | Concurrent decode — 2 cameras | Both tiles live; independent QR decode; neither stalls the other |
| AC3 | Concurrent decode — 4 cameras | 4 cameras run 10+ minutes; CPU stable; no crashes |
| AC4 | Stop / restart cycle | Cameras resume without app restart; no device-busy errors |
| AC5 | Disconnect handling | Unplugged camera shows error tile; others continue; replug handled or clearly messaged |
| AC6 | Setup page UX | Picker, preview, save work on Windows; permission prompts handled |
| AC7 | Documentation | Windows-specific findings captured in `docs/` or CLAUDE.md |

---

## Technical Context

### Language & Framework
- **Primary Language:** C# 12 / .NET MAUI 8.0
- **Target:** `net8.0-windows10.0.19041.0`
- **Platform seam:** `Platforms/Windows/CameraEnumerationService.cs`, `Platforms/Windows/CameraWorker.cs` (or equivalent names — confirm during verification)

### Pre-Verification Checklist
- Publish command: `dotnet publish SmartLog.Scanner -f net8.0-windows10.0.19041.0 -c Release`
- Deploy to a physical Windows gate PC (not VM — webcam pass-through is unreliable in VMs)
- Set `Logging.MinimumLevel = Debug` to capture MediaFoundation diagnostics
- Have 2 and 4 USB webcams available (identical models OK; capture model names)

---

## Recommended Approach

**Strategy:** Test-After (verification-first)  
**Rationale:** This story is a hardware verification run. Execute the scenario checklist; if all pass, close with evidence. If failures are found, fix them in the same PR.

---

## Implementation Phases

### Phase 1: Build and Deploy

**Goal:** Produce a Windows release build and deploy it to the test machine.

- [ ] Run: `dotnet publish SmartLog.Scanner -f net8.0-windows10.0.19041.0 -c Release -o publish/windows`
- [ ] Resolve any build errors (likely: platform-specific using directives, nullable warnings in Windows platform code).
- [ ] Copy published output to the Windows gate PC (USB drive or LAN share).
- [ ] Confirm the app launches without immediate crash.

**Files:** `Platforms/Windows/CameraEnumerationService.cs`, `Platforms/Windows/CameraWorker.cs`

### Phase 2: Execute Verification Scenarios

**Goal:** Run each AC scenario and record results.

- [ ] **AC1 — Enumeration:**
  - Attach 2+ USB webcams.
  - Open Setup page; verify all cameras appear by name.
  - Restart app; verify camera identifiers are stable (same order, same names).
  - Note any camera that does not appear.

- [ ] **AC2 — Concurrent decode (2 cameras):**
  - Configure 2 cameras in Setup.
  - Start from main page; verify both tiles show live video.
  - Scan a QR code from each camera; verify both scans are submitted independently.
  - Leave running for 5 minutes; check for freezes or stalls.

- [ ] **AC3 — 10-minute soak (4 cameras):**
  - Configure 4 cameras.
  - Monitor Task Manager CPU % throughout.
  - After 10 minutes, verify all 4 tiles still show live video; no worker silently stopped.

- [ ] **AC4 — Stop/restart cycle:**
  - From main page, stop all cameras.
  - Restart cameras; verify all come back.
  - Confirm no `Access Denied` or device-busy messages in logs.

- [ ] **AC5 — Disconnect handling:**
  - Unplug one camera mid-session.
  - Verify that camera's tile enters an error state.
  - Verify other cameras continue running.
  - Re-plug; attempt to re-add from Setup (or confirm a "restart required" message is shown).

- [ ] **AC6 — Setup UX:**
  - Open Setup; verify camera picker, preview, and save work.
  - If Windows shows a webcam permission dialog, acknowledge it; verify permission persists.

**Files:** (no code changes in this phase — evidence only)

### Phase 3: Fix Platform Issues (if any)

**Goal:** Resolve any failures found in Phase 2.

- [ ] For each failure, identify the root cause in the Windows platform implementation.
- [ ] Apply minimal targeted fix.
- [ ] Re-run the affected scenario to confirm it passes.
- [ ] Common likely touch points (from US0088 Technical Notes):
  - `Platforms/Windows/CameraEnumerationService.cs` — MediaFoundation COM threading / enumeration ordering
  - `Platforms/Windows/CameraWorker.cs` — frame callback threading, device-busy error handling
  - `Package.appxmanifest` or app manifest — webcam capability declaration (if MSIX packaged)

**Files:** `Platforms/Windows/CameraEnumerationService.cs`, `Platforms/Windows/CameraWorker.cs`, manifest files as needed

### Phase 4: Documentation

**Goal:** Capture findings so they're not re-discovered.

- [ ] Create or update `docs/windows-deployment.md`:
  - Supported Windows versions (confirmed: Win 10 build 19041+, Win 11)
  - USB webcam notes: any driver requirements, permission prompts, known-good models tested
  - Any Windows-specific caveats found (identical-model naming, sleep/resume, COM init)
  - Publish / install steps
- [ ] Update Scanner `CLAUDE.md` with a "Windows Platform Notes" section if substantive issues were found.

**Files:** `docs/windows-deployment.md`, `CLAUDE.md`

---

## Edge Case Handling

| # | Edge Case | Handling |
|---|-----------|----------|
| 1 | Webcam privacy setting blocks access | First-run dialog; acknowledge; document in `docs/` |
| 2 | Two identical-model cameras share same friendly name | Use device path for stable ID; document in Setup UI and `docs/` |
| 3 | Camera in use by another app | Error on that tile; others continue |
| 4 | Windows sleep/resume with cameras running | Investigate; either auto-reinitialise or surface a clear "Restart cameras" prompt |
| 5 | Fewer than 4 cameras available | Run soak with max available; document in test report |

---

## Definition of Done

- [ ] App builds for `net8.0-windows10.0.19041.0` without errors
- [ ] AC1–AC6 scenarios executed and each marked Pass or Pass-with-fix
- [ ] Any fixes applied, re-verified, and committed
- [ ] `docs/windows-deployment.md` created or updated with findings
- [ ] Scanner CLAUDE.md updated if substantive platform notes exist
- [ ] `dotnet test` passes (unit tests cover platform-agnostic logic; hardware tests are manual)

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-25 | Claude | Initial plan |
