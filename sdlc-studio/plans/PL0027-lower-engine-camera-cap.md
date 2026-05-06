# PL0027: Lower Engine Camera Cap from 8 to 4 — Implementation Plan

> **Status:** Draft
> **Story:** [US0129: Lower Engine Camera Cap from 8 to 4](../stories/US0129-lower-engine-camera-cap.md)
> **Epic:** [EP0018: Scanner Slim-down](../epics/EP0018-scanner-slim-down.md)
> **Created:** 2026-05-06
> **Language:** C# / Markdown

---

## Overview

Two-line code change plus a doc/test sweep. Engine cap moves from 8 to 4 in `MultiCameraManager` and `ScanTypeMigrationService`. Confirmed no test currently asserts the 8-cap (verified by grep before drafting), so test impact is minimal — but the plan still includes a verification step in case anything changed.

Net code change is essentially nil — this is a constant change. The work is in being thorough about doc strings and indirect references that could mislead a future reader.

---

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | Engine cap rejects > 4 | `MultiCameraManager.InitializeAsync` throws on `cameras.Count > 4` |
| AC2 | Engine cap accepts ≤ 4 | 1, 2, 3, 4 cameras all initialize successfully |
| AC3 | Migration constant is 4 | `ScanTypeMigrationService.MaxCameraSlots = 4` |
| AC4 | Hardcoded "8 cameras" gone from source | Grep returns no results |
| AC5 | Tests pass | `dotnet test` clean |
| AC6 | Both TFMs build clean | `net8.0-maccatalyst` and `net8.0-windows10.0.19041.0` |

---

## Technical Context

### Language & Framework
- **Primary Language:** C# 12 / .NET 8
- **Test Framework:** xUnit + Moq

### Existing Patterns
- `MultiCameraManager.InitializeAsync(IReadOnlyList<CameraInstance> cameras)` validates `cameras.Count > 8` at line ~55 and throws `ArgumentException("Maximum 8 cameras are supported.", nameof(cameras))`
- `ScanTypeMigrationService.MaxCameraSlots` is a `public const int = 8` consumed by `MigrateIfNeeded()` to iterate legacy slot indices `0..N-1`
- The XML doc on `MultiCameraManager` class line ~7 says "Orchestrates 1–8 simultaneous camera QR scanner instances" — needs updating to `1–4`
- `CLAUDE.md` (root architecture documentation) line ~99 says "Orchestrates 1–8 concurrent camera workers" — same update

### Pre-flight grep results (already confirmed)
- `MultiCameraManagerTests.cs` does **not** reference `8` as a cap or use `Theory(InlineData(8))` for over-cap testing
- `ScanTypeMigrationServiceTests.cs` does **not** reference `MaxCameraSlots = 8` directly
- `AdaptiveDecodeThrottleTests.cs` has `InlineData(5..8, ...)` cases — these become unreachable after the cap drops, but those tests are **deleted entirely** by PL0028 (US0130) which removes the throttle class. No coordination needed: PL0028 deletes the test file.

---

## Recommended Approach

**Strategy:** Test-After (no new behavior to test; existing suite acts as regression net)
**Rationale:** This is a constant change. The interesting verification is "does anything break" rather than "does new behavior work."

### Test Priority
1. Existing `MultiCameraManagerTests` continue to pass
2. Existing `ScanTypeMigrationServiceTests` continue to pass
3. Manual smoke: launch app on macOS with 1 camera; on Windows with 2 cameras

---

## Implementation Tasks

| # | Task | File | Depends On | Status |
|---|------|------|------------|--------|
| 1 | Lower engine cap in `MultiCameraManager.InitializeAsync` | `Core/Services/MultiCameraManager.cs` | — | [ ] |
| 2 | Update XML doc on `MultiCameraManager` class summary | `Core/Services/MultiCameraManager.cs` | 1 | [ ] |
| 3 | Lower `MaxCameraSlots` constant | `Core/Services/ScanTypeMigrationService.cs` | — | [ ] |
| 4 | Update `CLAUDE.md` architecture text | `CLAUDE.md` | — | [ ] |
| 5 | Grep for any remaining `8 cameras` / `Count > 8` / `Maximum 8` references | (n/a — discovery) | 1–4 | [ ] |
| 6 | Run `dotnet test` and confirm pass | `Scanner.Tests` | 1–4 | [ ] |
| 7 | Build both TFMs | (n/a — verification) | 1–4 | [ ] |
| 8 | Manual smoke: 1 cam macOS + 2 cam Windows | (n/a — verification) | 6, 7 | [ ] |

### Parallel Execution Groups

| Group | Tasks | Prerequisite |
|-------|-------|--------------|
| A | 1, 2, 3, 4 | — |
| B | 5 (final grep sweep) | A |
| C | 6, 7, 8 (verification) | B |

---

## Implementation Phases

### Phase 1: Edit the two source files

**File 1:** `SmartLog.Scanner.Core/Services/MultiCameraManager.cs`

Line ~7 (XML doc summary):

```csharp
// Before:
/// EP0011 (US0066–US0070): Orchestrates 1–8 simultaneous camera QR scanner instances.

// After:
/// EP0011 (US0066–US0070), capped to 4 by US0129: Orchestrates 1–4 simultaneous camera QR scanner instances.
```

Line ~55 (`InitializeAsync` argument validation):

```csharp
// Before:
if (cameras.Count > 8)
    throw new ArgumentException("Maximum 8 cameras are supported.", nameof(cameras));

// After:
if (cameras.Count > 4)
    throw new ArgumentException("Maximum 4 cameras are supported.", nameof(cameras));
```

**File 2:** `SmartLog.Scanner.Core/Services/ScanTypeMigrationService.cs`

Line 17:

```csharp
// Before:
public const int MaxCameraSlots = 8;

// After:
public const int MaxCameraSlots = 4;
```

---

### Phase 2: Update root architecture doc

**File:** `CLAUDE.md` (project root)

Find the line in the multi-camera services table that says:

```
| `IMultiCameraManager` / `MultiCameraManager` | Orchestrates 1–8 concurrent camera workers; ...
```

Change to:

```
| `IMultiCameraManager` / `MultiCameraManager` | Orchestrates 1–4 concurrent camera workers; ...
```

The `MainPage` paragraph below also says "(one per active camera, 1–8 configurable)". Change to "(one per active camera, 1–4 configurable; UI cap of 3 per US0127)".

---

### Phase 3: Final grep sweep

**Goal:** Catch any stragglers in source, tests, or docs that still reference 8.

```bash
# Source / tests
grep -rn "Maximum 8\|MaxCameraSlots = 8\|Count > 8\|1-8 cameras\|1–8 cameras\|1 to 8 cameras\|up to 8 cameras" \
  /Users/markmarmeto/Projects/SmartLogScannerApp/SmartLog.Scanner \
  /Users/markmarmeto/Projects/SmartLogScannerApp/SmartLog.Scanner.Core \
  /Users/markmarmeto/Projects/SmartLogScannerApp/SmartLog.Scanner.Tests \
  --include="*.cs" --include="*.xaml"

# Project root docs
grep -rn "1-8 cameras\|1–8 cameras\|1 to 8 cameras\|up to 8 cameras" \
  /Users/markmarmeto/Projects/SmartLogScannerApp/CLAUDE.md \
  /Users/markmarmeto/Projects/SmartLogScannerApp/README.md \
  /Users/markmarmeto/Projects/SmartLogScannerApp/docs/
```

Expected after Phases 1–2: zero hits.

If hits remain in `sdlc-studio/` (epics, stories, plans), **leave them** — those are historical SDLC artifacts pinned in time. Updating them would falsify the historical record. Only sweep the live source/tests/docs.

---

### Phase 4: Verification

```bash
# Tests
dotnet test SmartLog.Scanner.Tests -c Release

# Builds
dotnet build SmartLog.Scanner -f net8.0-maccatalyst
dotnet build SmartLog.Scanner -f net8.0-windows10.0.19041.0
```

**Expected:** all green; no new warnings.

**Manual smoke:**
- macOS: launch with 1 camera connected → camera slot renders; scan succeeds
- Windows: launch with 2 cameras connected → both slots render; scan from either succeeds

---

## Edge Case Handling Plan

| # | Edge Case (from Story) | Handling Strategy | Phase |
|---|------------------------|-------------------|-------|
| 1 | Test passes exactly 4 cameras | New cap accepts 4 (boundary inclusive); no test changes needed | Phase 1 |
| 2 | Test passes 5 cameras | New cap rejects with new message; no existing test does this today (verified via grep) | Phase 1 |
| 3 | Pre-US0089 prefs at slot indices 4–7 | Migration iterates only `0..3` after the change. Legacy keys at 4–7 remain orphaned in `Preferences` with no functional impact. Acceptable per story Open Question | n/a |
| 4 | Future need for 5+ cameras | One-line revert in two files; UI cap also needs raising — separate story | n/a |

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Hidden test or fixture iterates `for (i = 0; i < 8; i++)` to drive cap behavior | Test failure post-change | Phase 3 grep + Phase 4 `dotnet test` together catch it. If found, update fixture to use 4 |
| Doc-only references (`docs/windows-multi-camera.md`) still claim 1–8 | Misleading future readers | Phase 3 grep covers `docs/`; update any hits |
| `MainViewModel` uses a hardcoded `8` for camera-grid sizing math | Layout artifact | Verified during US0127 plan that grid sizing uses `CameraSlots.Count`, not a constant `8`; no code reads the cap directly |
| `Preferences.GetCameraName(i)` for `i ∈ 4..7` returns stored values from old installs | Misleading state | The migration's `for` loop now stops at 4; no consumer reads slot 4+; orphaned keys are harmless |

---

## Definition of Done

- [ ] All 6 ACs implemented
- [ ] `MultiCameraManager.InitializeAsync` rejects > 4 with updated exception message
- [ ] `ScanTypeMigrationService.MaxCameraSlots == 4`
- [ ] XML doc on `MultiCameraManager` updated
- [ ] `CLAUDE.md` architecture lines updated
- [ ] Phase 3 grep returns zero hits in live source/tests/docs (sdlc-studio artifacts excluded)
- [ ] `dotnet test SmartLog.Scanner.Tests` passes
- [ ] Both TFMs build clean
- [ ] Manual smoke on macOS (1 cam) and Windows (2 cams) passes

---

## Notes

- This plan deliberately does **not** touch the UI cap of 3 (set in US0127). If a future story bumps the UI to 4, the engine cap of 4 already covers it without code change.
- Doc updates in `CLAUDE.md` are part of the plan's Definition of Done so the root project docs stay accurate. SDLC documents (epics/stories/plans) are historical and not updated.
- `ScanTypeMigrationService` has a done-flag (`Migration.ScanTypeUnified.v1`) — laptops that already migrated are unaffected by the constant change. Pre-migration laptops read fewer slots; orphaned per-camera ScanType keys (if any) at indices 4–7 are inert. No data loss for any existing deployment.
