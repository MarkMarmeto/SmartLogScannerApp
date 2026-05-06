# EP0018: Scanner Slim-down

> **Status:** Draft
> **Owner:** AI Assistant
> **Reviewer:** Mark Daniel Marmeto
> **Created:** 2026-05-06
> **Target Release:** 2.3.0

## Summary

Bounded cleanup pass on the Scanner app to remove dead surfaces, tighten engine ceilings to the real deployment shape (3 webcams + 1 USB scanner per gate), and drop one over-built abstraction. Outcome of a 12-feature reevaluation review on 2026-05-06; 8 features were kept as-is, 4 produce stories under this epic. No user-visible behavior change at the gate — every change is internal cleanup or OS-level metadata.

## Inherited Constraints

> See PRD and TRD for full constraint details. Key constraints for this epic:

| Source | Type | Constraint | Impact |
|--------|------|------------|--------|
| Deployment | Hardware | Per-gate ceiling: 3 webcams + 1 USB barcode scanner, concurrent `Both` mode | Engine cap of 8 cameras is overprovisioned by 2× — story US0129 tightens to 4 |
| EP0012 | Architecture | `Both` mode is load-bearing; HealthCheck and Heartbeat both run | US0131 optimizes interaction without removing either |
| TRD | Platform | Cross-platform (macOS MacCatalyst + Windows) | All four stories must be verified on both TFMs (`net8.0-maccatalyst`, `net8.0-windows10.0.19041.0`) |
| US0127 | UI | Auto-detect cap of 3 in setup wizard | US0129 leaves the UI cap unchanged at 3; only tightens the engine cap |

---

## Business Context

### Problem Statement

The Scanner app accumulated four pieces of weight that no longer earn their place:

1. **`AboutPage`** — A static info page in the Shell menu with a hardcoded `Version 1.0.0` label that nobody has updated since release. It exists as a kiosk-menu entry with no operational value, and the app's author/company metadata never made it into the OS-level file properties (Windows Details tab, macOS Get Info).
2. **Engine camera cap of 8** — `MultiCameraManager.InitializeAsync` throws on `cameras.Count > 8` and `ScanTypeMigrationService.MaxCameraSlots = 8`. Real deployments cap at 3 (per US0127). The 8-cap is fictional headroom that bloats the testing matrix and signals false flexibility.
3. **`AdaptiveDecodeThrottle`** — A 35-line static lookup table mapping camera count → frame-skip count. Misnamed (not adaptive), redundantly DI-registered (callers use it statically), and once the cap drops to 4 the table holds only two values (≤2 → 5; 3–4 → 8). Its abstraction outweighs its content.
4. **Heartbeat-while-offline POSTs** — `HeartbeatService` POSTs every 60s (with backoff) regardless of `IHealthCheckService.IsOnline`. While the server is down, heartbeat hits the network anyway, fails, and adds noise. HealthCheck already knows the server is unreachable.

**PRD Reference:** Maintenance epic — no PRD feature mapping. Documented in this epic for traceability and alignment with the SDLC process.

### Value Proposition

Smaller code is easier to read, test, and change. Specifically:

- One fewer page to maintain (`AboutPage`).
- Engine cap that matches the deployment ceiling reduces the testing matrix from `N ∈ [1,8]` to `N ∈ [1,4]`.
- One fewer abstraction (`AdaptiveDecodeThrottle`) and one fewer DI binding.
- Heartbeat traffic skipped when known-offline (small bandwidth + log noise reduction; no impact on functional correctness because backoff already handles outages).

### Success Metrics

| Metric | Current | Target | Measurement |
|--------|---------|--------|-------------|
| Source files in `SmartLog.Scanner` + `SmartLog.Scanner.Core` | N | N − 3 (`AboutPage.xaml`, `AboutPage.xaml.cs`, `AdaptiveDecodeThrottle.cs`) | `git diff --stat` |
| Engine camera cap (`MultiCameraManager` + `ScanTypeMigrationService.MaxCameraSlots`) | 8 | 4 | grep |
| OS-visible author / company / copyright on built `.exe` and `.app` | All blank | All set to "Mark Daniel Marmeto" | Right-click Properties (Win) / Get Info (mac) |
| Heartbeat HTTP POST attempts during a 5-minute simulated offline period | ~5 (every 60s) | 0 | Network log review while `IsOnline = false` |
| Total LOC removed (net) | — | 200–250 | `git diff --shortstat` after all stories merge |

---

## Scope

### In Scope

- Delete `AboutPage.xaml`, `AboutPage.xaml.cs`, and the corresponding `<ShellContent>` route in `AppShell.xaml`.
- Add `<Authors>`, `<Company>`, `<Copyright>`, `<Product>` properties to `SmartLog.Scanner.csproj` so the built artifacts surface author metadata in OS file properties on both platforms.
- Lower the engine camera cap from 8 → 4: `MultiCameraManager.InitializeAsync` guard, `ScanTypeMigrationService.MaxCameraSlots`, and any tests/asserts that hardcode 8.
- Inline `AdaptiveDecodeThrottle.Calculate(activeCount)` at its 3 call sites as `cam.DecodeThrottleFrames = activeCount <= 2 ? 5 : 8;`. Delete `AdaptiveDecodeThrottle.cs` and the dead DI registration in `MauiProgram.cs`.
- `HeartbeatService` consults `IHealthCheckService.IsOnline` and short-circuits the HTTP POST when known-offline; backoff state still advances so the next online check happens at the same cadence.

### Out of Scope

- Visual/layout changes to `MainPage`, `ScanLogsPage`, `OfflineQueuePage`, `SetupPage` (no UX redesign in this epic).
- Removing any of the 8 features that were reviewed and kept (security migrations, scan-type migration, visitor pass QR, dual-mode toggle, tiered dedupe, separate offline-queue page, sound service, camera worker factory abstraction).
- Touching the heartbeat payload schema or interval defaults.
- Changes to the WebApp side.
- Build/release pipeline changes beyond what the four stories require.

### Affected Personas

- **IT Admin Ian** — Sees author metadata when inspecting the installer/binary; otherwise no behavior change.
- **Guard Gary** — No behavior change. AboutPage removal removes one unused menu item.

---

## Acceptance Criteria (Epic Level)

- [ ] `AboutPage` is removed from the codebase and Shell menu; the app launches without it and no Shell route resolution errors occur on either platform.
- [ ] Right-clicking the published `.exe` on Windows and choosing Properties → Details shows `Mark Daniel Marmeto` in the Company field and a copyright line crediting Mark Daniel Marmeto.
- [ ] `Get Info` on the built `.app` on macOS shows the same copyright credit.
- [ ] `MultiCameraManager.InitializeAsync` rejects `cameras.Count > 4` (was: > 8); `ScanTypeMigrationService.MaxCameraSlots = 4`.
- [ ] `AdaptiveDecodeThrottle.cs` no longer exists; the 3 prior call sites assign `DecodeThrottleFrames` inline; the DI registration in `MauiProgram.cs` is removed; `MultiCameraManagerTests` still pass.
- [ ] During a simulated offline period (HealthCheck reports `IsOnline = false`), `HeartbeatService` produces zero HTTP POST attempts; once HealthCheck flips back to `true`, the next heartbeat fires within one base interval.
- [ ] All four stories build and pass tests on both `net8.0-maccatalyst` and `net8.0-windows10.0.19041.0`.

---

## Dependencies

### Blocked By

| Dependency | Type | Status | Owner |
|------------|------|--------|-------|
| US0127 (Auto-Detect Camera Slots) | Story | Done | AI Assistant |
| US0120 (Heartbeat Service) | Story | Done | AI Assistant |
| US0015 (Health Check Monitoring Service) | Story | Done | AI Assistant |

### Blocking

| Item | Type | Impact |
|------|------|--------|
| None — pure cleanup; no downstream feature depends on this epic | — | — |

---

## Risks & Assumptions

### Assumptions

- No deployed gate uses 4+ cameras today (confirmed: deployment ceiling is 3 webcams).
- The hardcoded `Version 1.0.0` in `AboutPage.xaml` is not referenced by any tooling that scrapes display text (release notes, screenshots, etc.).
- MAUI csproj `<Authors>`, `<Company>`, `<Copyright>`, `<Product>` flow into the assembly metadata for both Windows and macCatalyst targets without additional Info.plist edits.
- `IHealthCheckService.IsOnline` is `null` only briefly at startup (optimistic-online assumption per `HealthCheckService.cs:22`); skipping heartbeat on `null` would be wrong, so US0131 must guard for `IsOnline == false` specifically, not `!= true`.

### Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| MAUI csproj metadata properties don't flow into the macCatalyst `.app` Info.plist by default | Medium | Low | If `<Copyright>` doesn't appear in Get Info, add explicit `NSHumanReadableCopyright` to Info.plist (US0128 acceptance verifies on both platforms). |
| Removing `AboutPage` breaks deep links / Shell navigation if anything routes to `//AboutPage` | Low | Low | Grep for `AboutPage` route references before delete; remove all. |
| Tests fail because `MockMultiCameraManager` setup uses 5+ camera scenarios | Low | Low | Run full suite under both TFMs after cap change; update test fixtures if any exist with N>4. |
| A future "we need 5 cameras at one gate" requirement returns | Low | Medium | Cap is a single-line change in two files; reverting is trivial. |
| Heartbeat skip-when-offline masks a real reachability issue (HealthCheck stuck reporting offline) | Low | Low | HealthCheck has its own watchdog (15s polls + stability window); if it gets stuck, that's the root issue, not our skip. |

---

## Technical Considerations

### Architecture Impact

- `MauiProgram.cs` loses two registrations: `AddSingleton<AdaptiveDecodeThrottle>` (US0130) and gains nothing.
- `HeartbeatService` constructor adds one dependency: `IHealthCheckService` (US0131). Already a registered singleton — no DI graph changes.
- `SmartLog.Scanner.csproj` gains four PropertyGroup entries (US0128).
- `MultiCameraManager.InitializeAsync` argument validation message updates from "Maximum 8 cameras" to "Maximum 4 cameras" (US0129).

### Integration Points

- No external API contracts touched.
- No SQLite schema changes.
- No `Preferences` keys added or removed.
- No new NuGet dependencies.

### UI/UX Notes

- AppShell loses one entry. The remaining menu (after US0128 lands) is: MainPage, ScanLogsPage, OfflineQueuePage. Each earns its place per the 2026-05-06 review.

---

## Sizing

**Story Points:** 6
**Estimated Story Count:** 4

**Complexity Factors:**
- US0128 has the largest cross-platform surface (csproj metadata behavior differs between Win and Mac; needs build verification on both).
- US0129 is mostly mechanical but must update tests if any hardcode 8.
- US0130 is the smallest (delete a class, inline a one-liner three times).
- US0131 introduces a new dependency edge but no new behavior surface.

---

## Story Breakdown

- [ ] [US0128: Remove AboutPage and Surface Author Metadata in OS App Properties](../stories/US0128-remove-aboutpage-and-author-metadata.md)
- [ ] [US0129: Lower Engine Camera Cap from 8 to 4](../stories/US0129-lower-engine-camera-cap.md)
- [ ] [US0130: Inline Frame-Skip Throttle and Delete AdaptiveDecodeThrottle](../stories/US0130-inline-decode-throttle.md)
- [ ] [US0131: Skip Heartbeat POST When HealthCheck Reports Offline](../stories/US0131-skip-heartbeat-when-offline.md)

---

## Test Plan

> Test spec to be generated via `/sdlc-studio test-spec --epic EP0018`

Key scenarios to cover:
- Build on both TFMs: `dotnet build SmartLog.Scanner -f net8.0-maccatalyst` and `dotnet build SmartLog.Scanner -f net8.0-windows10.0.19041.0` succeed without warnings.
- Inspect Windows `.exe` Properties → Details: Company = "Mark Daniel Marmeto", Copyright populated.
- Inspect macOS `.app` Get Info: Copyright populated.
- Existing test suite passes after cap-lowering and AdaptiveDecodeThrottle removal.
- `HeartbeatServiceTests` add a case that asserts no POST is attempted when `IHealthCheckService.IsOnline` returns `false`.
- Manual: simulate offline (block server), confirm heartbeat traffic is zero, then unblock and confirm heartbeat resumes within one base interval.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-05-06 | AI Assistant | Initial draft. Source: 12-feature Scanner reevaluation review with Mark Daniel Marmeto (4 changes selected, 8 features kept as-is). |
