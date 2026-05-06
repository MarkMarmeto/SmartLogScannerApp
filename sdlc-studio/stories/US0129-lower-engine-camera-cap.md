# US0129: Lower Engine Camera Cap from 8 to 4

> **Status:** Draft
> **Epic:** [EP0018: Scanner Slim-down](../epics/EP0018-scanner-slim-down.md)
> **Owner:** AI Assistant
> **Reviewer:** Mark Daniel Marmeto
> **Created:** 2026-05-06

## User Story

**As** the Scanner app maintainer
**I want** the engine-level camera cap lowered from 8 to 4
**So that** the runtime tolerance reflects realistic deployment headroom (3 webcams + 1 USB scanner per gate, with one slot of safety buffer), shrinks the implicit testing matrix, and stops signaling false flexibility

## Context

### Background

The Scanner has **two** camera caps in the codebase:

1. **UI / auto-detect cap = 3**, set in US0127 (AC6: "Hard cap of 3 slots regardless of connected devices"). The setup wizard never produces more than 3 slots. This is the user-facing ceiling and matches the deployment policy.
2. **Engine cap = 8**, in two places:
   - `SmartLog.Scanner.Core/Services/MultiCameraManager.cs` — `InitializeAsync` throws `ArgumentException("Maximum 8 cameras are supported.")` if `cameras.Count > 8`.
   - `SmartLog.Scanner.Core/Services/ScanTypeMigrationService.cs` — `public const int MaxCameraSlots = 8;` (used to iterate legacy per-camera preference keys during one-time migration).

Since the UI cap is already 3, the engine never sees more than 3 cameras at runtime. The 8-cap is fictional headroom: it does not enable any feature, and lowering it does not change shipped behavior. What it *does* do:

- Tightens the implicit testing matrix (`N ∈ [1,8]` → `N ∈ [1,4]`).
- Makes the engine ceiling reflect documented deployment policy (3 webcams max, per [Scanner deployment shape memory](../../../../.claude/projects/-Users-markmarmeto-Projects/memory/project_scanner_deployment_shape.md)).
- Leaves a 1-slot safety buffer between the UI cap (3) and the engine cap (4) so a small future expansion doesn't immediately bump the engine ceiling.

### Why 4, not 3 (matching UI), and not 8 (status quo)

- **3** would tightly match the UI cap with zero buffer. If a future story bumps the UI to 4, the engine would have to be touched again. Aesthetic minimum, no breathing room.
- **8** is the current status quo — fictional flexibility, never tested at the upper bounds in real deployments.
- **4** matches the UI + 1 buffer. Resilient to a single-step UI expansion without code change. This is the user's chosen value (2026-05-06 review, Option A).

### Reference

- Setup-wizard-side cap is enforced in `SetupViewModel` per US0127 and is **not** changed by this story.
- The `ScanTypeMigrationService` migration (US0089) has a done-flag and short-circuits on subsequent launches; lowering its `MaxCameraSlots` to 4 only affects laptops that have **never** completed the migration. Per [memory: SecurityMigrationService kept](../../../..) the practical risk is zero for already-migrated installs.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| Deployment | Hardware | Per-gate ceiling: 3 webcams + 1 USB scanner | New cap of 4 is user-confirmed correct headroom |
| US0127 | UI | Setup wizard cap of 3 | This story does **not** change the UI cap; only the engine cap |
| US0089 | Migration | `ScanTypeMigrationService` is one-shot, idempotent | Lowering `MaxCameraSlots` is safe for migrated installs; pre-migration installs only iterate slots `0..3` instead of `0..7` (legacy keys at indices 4–7 would be left orphaned, but no laptop is known to have used them) |

---

## Acceptance Criteria

### AC1: Engine cap rejects > 4

- **Given** `MultiCameraManager.InitializeAsync` is called
- **When** `cameras.Count > 4`
- **Then** an `ArgumentException` is thrown with message containing `Maximum 4 cameras` (was: `Maximum 8`)

### AC2: Engine cap accepts ≤ 4

- **Given** `MultiCameraManager.InitializeAsync` is called with 1, 2, 3, or 4 cameras
- **When** the call returns
- **Then** all camera workers are initialized and the camera grid renders correctly

### AC3: Migration constant reflects new cap

- **Given** `SmartLog.Scanner.Core/Services/ScanTypeMigrationService.cs`
- **When** read after this story
- **Then** `MaxCameraSlots = 4` (was: `8`)

### AC4: All hardcoded "8 cameras" references in source and tests are reconciled

- **Given** the codebase before this story
- **When** the story is complete
- **Then** `grep -rn "8 cameras\|MaxCameraSlots = 8\|Count > 8" SmartLog.Scanner SmartLog.Scanner.Core SmartLog.Scanner.Tests` returns no results in `.cs` source (excluding XML doc references to historical context)

### AC5: Tests pass after cap lowering

- **Given** the existing test suite
- **When** `dotnet test SmartLog.Scanner.Tests` is run
- **Then** all tests pass; if any test fixture iterates through 5+ cameras to exercise the cap boundary, it is updated to use 4 as the boundary value

### AC6: Builds clean on both platforms

- **Given** the changes from AC1–AC4
- **When** built for both target frameworks
- **Then** `dotnet build SmartLog.Scanner -f net8.0-maccatalyst` and `dotnet build SmartLog.Scanner -f net8.0-windows10.0.19041.0` both succeed with no new warnings

---

## Scope

### In Scope
- `MultiCameraManager.InitializeAsync` cap and exception message
- `ScanTypeMigrationService.MaxCameraSlots`
- Any test that explicitly tests the boundary at 8 cameras
- Any XML doc comments that say "1–8" or "max 8"

### Out of Scope
- Changing the UI cap (stays at 3 per US0127)
- Removing or refactoring `MultiCameraManager` itself
- Changing per-camera preference key format
- Changes to `SetupViewModel` or `SetupPage.xaml`
- WebApp-side device limits (separate project)

---

## Technical Notes

### Files to change

| File | Change |
|------|--------|
| `SmartLog.Scanner.Core/Services/MultiCameraManager.cs` | Line ~55: `if (cameras.Count > 8)` → `if (cameras.Count > 4)`; update exception message |
| `SmartLog.Scanner.Core/Services/ScanTypeMigrationService.cs` | Line 17: `public const int MaxCameraSlots = 8;` → `public const int MaxCameraSlots = 4;` |
| `SmartLog.Scanner.Core/Services/MultiCameraManager.cs` (XML doc on class line ~7) | "1–8 simultaneous camera QR scanner instances" → "1–4 simultaneous camera QR scanner instances" |
| `SmartLog.Scanner/CLAUDE.md` (architecture section) | "1–8 concurrent camera workers" → "1–4 concurrent camera workers" |
| `SmartLog.Scanner.Tests/Services/MultiCameraManagerTests.cs` | If a `Theory(InlineData(8))` or similar over-cap boundary test exists, change to `(5)` to exercise the new "1 over cap" scenario; update any expected message strings |

### Discovery commands

```bash
# Find all hardcoded references to 8 cameras
grep -rn "8 cameras\|MaxCameraSlots = 8\|Count > 8\|1-8\|1–8" SmartLog.Scanner SmartLog.Scanner.Core SmartLog.Scanner.Tests
```

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|--------------------|
| A test passes exactly 4 cameras | Accepts (boundary inclusive) |
| A test passes 5 cameras | Throws `ArgumentException` with new message |
| A laptop with pre-US0089 prefs has data at slot indices 4–7 | Migration runs over slots 0–3 only; legacy keys at 4–7 remain orphaned in `Preferences`. No functional impact (those keys are never read elsewhere). Acceptable — see Open Questions for one-line cleanup option |
| Future deploy genuinely needs 5+ cameras | One-line revert in two files (engine-side); UI cap also needs raising — separate story |

---

## Test Scenarios

- [ ] `MultiCameraManager.InitializeAsync` with 4 cameras: succeeds
- [ ] `MultiCameraManager.InitializeAsync` with 5 cameras: throws `ArgumentException` containing "Maximum 4"
- [ ] `ScanTypeMigrationService.MaxCameraSlots == 4`
- [ ] No `.cs` file in source or tests contains `Maximum 8` / `MaxCameraSlots = 8` / `Count > 8`
- [ ] Existing tests pass on `net8.0` test TFM
- [ ] Manual smoke: launch app on macOS with 1 camera connected — slot renders
- [ ] Manual smoke (Windows): launch app with 2 cameras connected — both slots render

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| [US0127](US0127-auto-detect-camera-slots.md) | Predecessor | UI cap of 3 already in place | Done |
| [US0089](US0089-unify-scan-type-to-device-level.md) | Predecessor | Migration done-flag in place | Done |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| None | — | — |

---

## Estimation

**Story Points:** 1
**Complexity:** Low — two-line code change plus test/doc reconciliation. Main risk is missing a hardcoded `8` in tests or doc strings; grep-driven sweep keeps it bounded.

---

## Open Questions

- [ ] Should the migration also clean up orphaned `MultiCamera.{4..7}.ScanType` preference keys on laptops that had them set pre-US0089? **Proposed: No.** Migration is one-shot; touching it now risks introducing a second migration step. Orphaned keys are inert and consume <1KB. Defer to a future "purge migrations" sweep.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-05-06 | AI Assistant | Initial draft from Scanner Slim-down review (EP0018). |
