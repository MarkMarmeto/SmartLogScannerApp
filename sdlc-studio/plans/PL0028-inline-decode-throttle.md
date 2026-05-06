# PL0028: Inline Frame-Skip Throttle and Delete AdaptiveDecodeThrottle — Implementation Plan

> **Status:** Draft
> **Story:** [US0130: Inline Frame-Skip Throttle and Delete AdaptiveDecodeThrottle](../stories/US0130-inline-decode-throttle.md)
> **Epic:** [EP0018: Scanner Slim-down](../epics/EP0018-scanner-slim-down.md)
> **Created:** 2026-05-06
> **Language:** C#

---

## Overview

Three call-site replacements + delete one source file + delete one DI registration + delete a 70-line test file that tests a class that no longer exists. Frame-skip behavior preserved exactly: 5 frames between decodes for ≤2 cameras, 8 for 3–4 cameras. The 5+ camera cases in the old lookup table become unreachable after PL0027 caps the engine at 4 — they are not preserved in the inlined ternary.

This is the smallest plan in EP0018 by impact. Most of the friction is making sure the `AdaptiveDecodeThrottleTests.cs` deletion is intentional and doesn't leave a dangling test reference somewhere.

---

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | Source file deleted | `AdaptiveDecodeThrottle.cs` no longer exists |
| AC2 | DI registration removed | `MauiProgram.cs:325` no longer registers the class |
| AC3 | Three call sites inlined | Inline ternary at all three known sites |
| AC4 | No remaining references | Grep returns no results in live source/tests |
| AC5 | `CameraInstance.DecodeThrottleFrames` default unchanged | Still `5` |
| AC6 | Tests + builds pass | `dotnet test` + both TFMs |
| AC7 | Frame-skip values preserved | `≤2 → 5`, `3–4 → 8` |

---

## Technical Context

### Language & Framework
- **Primary Language:** C# 12 / .NET 8
- **Test Framework:** xUnit + Moq

### Existing Patterns
- Three known call sites of `AdaptiveDecodeThrottle.Calculate(...)`:
  - `Core/Services/MultiCameraManager.cs:197` — `var throttle = AdaptiveDecodeThrottle.Calculate(Math.Max(activeCount, 1));`
  - `ViewModels/MainViewModel.cs:371` — `DecodeThrottleFrames = AdaptiveDecodeThrottle.Calculate(count)`
  - `ViewModels/MainViewModel.cs:402` — `filtered[i].DecodeThrottleFrames = AdaptiveDecodeThrottle.Calculate(filtered.Count);`
- DI registration: `MauiProgram.cs:325` — `builder.Services.AddSingleton<AdaptiveDecodeThrottle>();` (dead — all callers use the static `Calculate` method)
- Existing test file: `Scanner.Tests/Services/AdaptiveDecodeThrottleTests.cs` — 70 lines of `[Theory]` cases against `Calculate`
- Default value: `CameraInstance.DecodeThrottleFrames` initializer is `= 5` at line ~33

### Pre-flight grep results (already confirmed)
- Three call sites identified above are the **only** non-test, non-doc references in `SmartLog.Scanner` and `SmartLog.Scanner.Core`
- `AdaptiveDecodeThrottleTests.cs` has 8 inline data points (cameraCount 1–8); after deletion this test surface is gone

---

## Recommended Approach

**Strategy:** Test-After (the inline code is trivially correct; existing camera tests cover any regression in `MultiCameraManager`/`MainViewModel`)
**Rationale:** Replacing a static lookup with a one-line ternary is a transcription-level change. Existing `MultiCameraManagerTests` exercise the full initialization path including the throttle assignment.

### Test Priority
1. `MultiCameraManagerTests` continue to pass (regression net for inlined assignment)
2. Manual smoke: launch app with 2 cameras → confirm `DecodeThrottleFrames == 5` on each slot (via debug breakpoint or log)

---

## Implementation Tasks

| # | Task | File | Depends On | Status |
|---|------|------|------------|--------|
| 1 | Inline ternary in `MultiCameraManager.cs` line ~197 | `Core/Services/MultiCameraManager.cs` | — | [ ] |
| 2 | Inline ternary in `MainViewModel.cs` line ~371 | `ViewModels/MainViewModel.cs` | — | [ ] |
| 3 | Inline ternary in `MainViewModel.cs` line ~402 | `ViewModels/MainViewModel.cs` | — | [ ] |
| 4 | Remove DI registration | `MauiProgram.cs:325` | — | [ ] |
| 5 | Delete `AdaptiveDecodeThrottle.cs` | `Core/Services/AdaptiveDecodeThrottle.cs` | 1, 2, 3, 4 | [ ] |
| 6 | Delete `AdaptiveDecodeThrottleTests.cs` | `Scanner.Tests/Services/AdaptiveDecodeThrottleTests.cs` | 5 | [ ] |
| 7 | Update XML doc on `CameraInstance.DecodeThrottleFrames` if it references the deleted class by name | `Core/Models/CameraInstance.cs` | 5 | [ ] |
| 8 | Update `CLAUDE.md` services table — remove `AdaptiveDecodeThrottle` row | `CLAUDE.md` | 5 | [ ] |
| 9 | Final grep sweep | (n/a — discovery) | 1–8 | [ ] |
| 10 | `dotnet test` + both TFM builds + manual smoke | (n/a — verification) | 9 | [ ] |

### Parallel Execution Groups

| Group | Tasks | Prerequisite |
|-------|-------|--------------|
| A | 1, 2, 3, 4 | — |
| B | 5, 6, 7, 8 | A |
| C | 9 (final grep) | B |
| D | 10 (verification) | C |

---

## Implementation Phases

### Phase 1: Inline the three call sites

**File 1:** `SmartLog.Scanner.Core/Services/MultiCameraManager.cs` line ~197

```csharp
// Before:
var activeCount = _cameras.Count(c =>
    c.Status == CameraStatus.Scanning ||
    c.Status == CameraStatus.Starting);
var throttle = AdaptiveDecodeThrottle.Calculate(Math.Max(activeCount, 1));
foreach (var cam in _cameras)
{
    cam.DecodeThrottleFrames = throttle;
    ...
}

// After:
var activeCount = _cameras.Count(c =>
    c.Status == CameraStatus.Scanning ||
    c.Status == CameraStatus.Starting);
var throttle = activeCount <= 2 ? 5 : 8;
foreach (var cam in _cameras)
{
    cam.DecodeThrottleFrames = throttle;
    ...
}
```

Note: the `Math.Max(activeCount, 1)` guard is dropped. With the inline form, `activeCount == 0` falls into the `<= 2` branch and yields `5` — same as the old `<= 0 => 5` table entry.

**File 2:** `SmartLog.Scanner/ViewModels/MainViewModel.cs` line ~371

```csharp
// Before:
DecodeThrottleFrames = AdaptiveDecodeThrottle.Calculate(count)

// After:
DecodeThrottleFrames = count <= 2 ? 5 : 8
```

**File 3:** `SmartLog.Scanner/ViewModels/MainViewModel.cs` line ~402

```csharp
// Before:
filtered[i].DecodeThrottleFrames = AdaptiveDecodeThrottle.Calculate(filtered.Count);

// After:
filtered[i].DecodeThrottleFrames = filtered.Count <= 2 ? 5 : 8;
```

---

### Phase 2: Remove DI registration

**File:** `SmartLog.Scanner/MauiProgram.cs` (line ~325)

```csharp
// Delete this line:
builder.Services.AddSingleton<AdaptiveDecodeThrottle>();
```

If a `using SmartLog.Scanner.Core.Services;` or similar is present and used only for this line, leave it — other services in the same namespace are very likely also used in `MauiProgram`.

---

### Phase 3: Delete files

- [ ] Delete `SmartLog.Scanner.Core/Services/AdaptiveDecodeThrottle.cs`
- [ ] Delete `SmartLog.Scanner.Tests/Services/AdaptiveDecodeThrottleTests.cs`

The test file tests a class that will not exist. Deletion is the correct action — there is no "the lookup is gone, but we want to verify the inline policy" test to write at the unit level (the inline ternary is one expression). Behavior is exercised by `MultiCameraManagerTests` already.

---

### Phase 4: Update doc references

#### 4a — `CameraInstance.cs` XML doc

```csharp
// Before (line ~30):
/// Adaptive frame-skip count calculated by AdaptiveDecodeThrottle.
public int DecodeThrottleFrames { get; set; } = 5;

// After:
/// Frame-skip count for the camera decode pipeline. Set by the orchestrator at slot init:
/// 5 for ≤2 active cameras, 8 for 3–4. Default of 5 covers single-camera startup before init.
public int DecodeThrottleFrames { get; set; } = 5;
```

#### 4b — `CLAUDE.md` services table

Remove the `AdaptiveDecodeThrottle` row from the multi-camera services table (line ~104). The line currently reads:

```
| `AdaptiveDecodeThrottle` | Dynamically adjusts per-worker decode frame rate based on CPU/decode pressure to prevent thermal throttling |
```

After deletion the surrounding rows close up. The brief "frame-skip 5/8" policy is now a comment in `CameraInstance.cs` (4a) and inline at the call sites; it does not need a top-level architecture entry.

---

### Phase 5: Final grep sweep

```bash
grep -rn "AdaptiveDecodeThrottle" \
  /Users/markmarmeto/Projects/SmartLogScannerApp/SmartLog.Scanner \
  /Users/markmarmeto/Projects/SmartLogScannerApp/SmartLog.Scanner.Core \
  /Users/markmarmeto/Projects/SmartLogScannerApp/SmartLog.Scanner.Tests \
  /Users/markmarmeto/Projects/SmartLogScannerApp/CLAUDE.md \
  /Users/markmarmeto/Projects/SmartLogScannerApp/README.md \
  /Users/markmarmeto/Projects/SmartLogScannerApp/docs/ \
  --include="*.cs" --include="*.xaml" --include="*.md"
```

Expected: zero hits.

`sdlc-studio/` (epics/stories/plans) **will** contain references to `AdaptiveDecodeThrottle` — those are historical artifacts and stay as-is.

---

### Phase 6: Verification

```bash
dotnet test SmartLog.Scanner.Tests -c Release
dotnet build SmartLog.Scanner -f net8.0-maccatalyst
dotnet build SmartLog.Scanner -f net8.0-windows10.0.19041.0
```

**Expected:** all green; no new warnings.

**Manual smoke:**
- macOS: launch with 1 camera → set a debug breakpoint or log line where `DecodeThrottleFrames` is assigned → confirm value `5`
- Windows: launch with 2 cameras → confirm both slots get `5`

If a 3-camera test hardware setup is available, also confirm `8`.

---

## Edge Case Handling Plan

| # | Edge Case (from Story) | Handling Strategy | Phase |
|---|------------------------|-------------------|-------|
| 1 | `activeCount == 0` | Inline ternary yields `5` (`0 <= 2` is true) — matches old `<= 0 => 5` branch | Phase 1 |
| 2 | `activeCount > 4` | Cannot occur after PL0027 caps at 4; defensive value of `8` matches old `3 => 8, 4 => 8` for 5+ cases up to the discontinuity | Phase 1 |
| 3 | A test directly calls `AdaptiveDecodeThrottle.Calculate(...)` | Test file deleted in Phase 3 — no orphaned test references | Phase 3 |
| 4 | Static analyzer flags magic numbers `5` and `8` | Acceptable per story Open Question — three sites, comment in `CameraInstance.cs` provides context. Suppress at site if linter strict | Phase 1 / 4a |

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| A fourth call site is missed and breaks build | Build failure | Phase 5 grep catches any reference; the build itself catches missing-symbol errors |
| `AdaptiveDecodeThrottleTests.cs` deletion missed in PR review | Test compiles against deleted class → build fails fast | Phase 3 explicitly deletes the test file as a tracked task; `dotnet test` would fail to compile if missed |
| `Math.Max(activeCount, 1)` was guarding against a real `0` case in `MultiCameraManager.cs` | Silent wrong-value if the guard mattered | Old behavior at `0` was `5` (per `<= 0 => 5`). New behavior at `0` is also `5` (`0 <= 2`). Identical |
| Interpolation between old "5+ cams → higher throttle" and new flat `8` | Performance regression on 5+ camera setups | Deployment cap is 3 webcams + 1 USB (per memory); 5+ cameras cannot occur at any deployed gate. PL0027 enforces this at the engine level |

---

## Definition of Done

- [ ] All 7 ACs implemented
- [ ] `find SmartLog.Scanner.Core -name "AdaptiveDecodeThrottle.cs"` returns no results
- [ ] `find SmartLog.Scanner.Tests -name "AdaptiveDecodeThrottleTests.cs"` returns no results
- [ ] `MauiProgram.cs` no longer registers `AdaptiveDecodeThrottle`
- [ ] All three call sites use inline ternary
- [ ] `CameraInstance.DecodeThrottleFrames` initializer remains `= 5`
- [ ] Phase 5 grep sweep returns zero hits in live source/tests/docs
- [ ] `dotnet test SmartLog.Scanner.Tests` passes
- [ ] Both TFMs build clean with no new warnings
- [ ] Manual smoke (1 cam macOS + 2 cam Windows) shows `DecodeThrottleFrames == 5`

---

## Notes

- The decision to use raw `5` and `8` (vs. named constants) is per the story Open Question — keep as raw numbers for inline visibility.
- Coordination with PL0027 (US0129): the new ternary is compatible with both pre- and post-cap-change states. PL0028 does not depend on PL0027 having shipped first; either order works.
- `Plugin.Maui.Audio` and other camera-related dependencies are unaffected.
- The `MinThrottle = 3` constant from `AdaptiveDecodeThrottle.cs` is **not** preserved. With cap=4 and the new ternary returning either 5 or 8, both ≥ 3; the floor is implicit.
