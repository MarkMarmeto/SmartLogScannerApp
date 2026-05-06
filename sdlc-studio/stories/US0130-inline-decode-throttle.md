# US0130: Inline Frame-Skip Throttle and Delete AdaptiveDecodeThrottle

> **Status:** Draft
> **Epic:** [EP0018: Scanner Slim-down](../epics/EP0018-scanner-slim-down.md)
> **Owner:** AI Assistant
> **Reviewer:** Mark Daniel Marmeto
> **Created:** 2026-05-06

## User Story

**As** the Scanner app maintainer
**I want** the misnamed `AdaptiveDecodeThrottle` class deleted and its single-line policy inlined at the three call sites
**So that** the throttle policy is visible where it is applied, the dead DI registration is removed, and the codebase loses one over-built abstraction without changing runtime behavior

## Context

### Background

`AdaptiveDecodeThrottle.cs` (35 lines, in `SmartLog.Scanner.Core/Services/`) is a static lookup table that maps `activeCameraCount` → frame-skip count for the camera decode pipeline:

```csharp
public static int Calculate(int activeCameraCount)
{
    var value = activeCameraCount switch
    {
        <= 0 => 5,
        1 => 5,
        2 => 5,
        3 => 8,
        4 => 8,
        5 => 10,
        6 => 12,
        7 => 13,
        _ => 15   // 8+ cameras
    };
    return Math.Max(value, MinThrottle);
}
```

It is *not* adaptive in the dynamic-CPU-feedback sense the name suggests — it is a static table. After US0129 lowers the engine cap to 4, the table reduces to two values: `≤2 → 5` and `3–4 → 8`. The 5+ branches become unreachable.

It is also **misregistered**: `MauiProgram.cs:325` does `builder.Services.AddSingleton<AdaptiveDecodeThrottle>();` but every call site uses the static method `AdaptiveDecodeThrottle.Calculate(...)`, so the DI registration never resolves and is dead.

### Why inline rather than keep + simplify

Two clean options were considered during the 2026-05-06 review:

- **A. Inline + delete**: replace 3 call sites with `cam.DecodeThrottleFrames = activeCount <= 2 ? 5 : 8;`, delete `AdaptiveDecodeThrottle.cs`, delete the DI registration. Net: −35 lines, −1 file, −1 binding. **Selected.**
- **B. Trim in place**: keep the file, shrink the switch to two cases, delete the DI registration. Lower-risk but keeps an abstraction wrapping ~2 lines of logic.

Option A was selected because once the table is two values, the abstraction is heavier than its content. Inlining puts the policy at the call site where someone is most likely to question or tune it later.

### Frame-skip behavior is preserved

The behavior — decode every 5th frame for 1–2 cameras, every 8th for 3–4 — is preserved. This is not a refactor that changes the throttle; it changes only where the constants live. (Whether frame-skipping itself is needed at all on modern hardware is a tuning question for a separate future story; not in scope here.)

### Three call sites

```text
SmartLog.Scanner.Core/Services/MultiCameraManager.cs:197
    var throttle = AdaptiveDecodeThrottle.Calculate(Math.Max(activeCount, 1));
    ...
    cam.DecodeThrottleFrames = throttle;

SmartLog.Scanner/ViewModels/MainViewModel.cs:371
    DecodeThrottleFrames = AdaptiveDecodeThrottle.Calculate(count)

SmartLog.Scanner/ViewModels/MainViewModel.cs:402
    filtered[i].DecodeThrottleFrames = AdaptiveDecodeThrottle.Calculate(filtered.Count);
```

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| EP0018 | Scope | No behavior change | Frame-skip values must remain functionally identical (5 for ≤2 cams, 8 for 3–4 cams) |
| US0129 | Cap | Engine cap is now 4 | The collapsed table only needs to cover N ∈ [1,4]; `activeCount` will never exceed 4 |
| TRD | Platform | Cross-platform | No platform-specific code touched; both TFMs must build clean |

---

## Acceptance Criteria

### AC1: AdaptiveDecodeThrottle.cs is deleted

- **Given** the codebase before this story
- **When** the story is complete
- **Then** `SmartLog.Scanner.Core/Services/AdaptiveDecodeThrottle.cs` no longer exists

### AC2: DI registration is removed

- **Given** `MauiProgram.cs:325` previously contained `builder.Services.AddSingleton<AdaptiveDecodeThrottle>();`
- **When** the story is complete
- **Then** that line is removed and `MauiProgram.cs` does not reference `AdaptiveDecodeThrottle` anywhere

### AC3: Three call sites assign DecodeThrottleFrames inline

- **Given** the three known call sites (`MultiCameraManager.cs:~197`, `MainViewModel.cs:~371`, `MainViewModel.cs:~402`)
- **When** the story is complete
- **Then** each site assigns `DecodeThrottleFrames = activeCount <= 2 ? 5 : 8` (variable name follows the local context — `count`, `activeCount`, or `filtered.Count`)

### AC4: No remaining references to AdaptiveDecodeThrottle in source

- **Given** the codebase after AC1–AC3
- **When** `grep -rn "AdaptiveDecodeThrottle" SmartLog.Scanner SmartLog.Scanner.Core SmartLog.Scanner.Tests` is run
- **Then** no results are returned (excluding the SDLC docs — `sdlc-studio/` is not part of the build and references there are historical)

### AC5: CameraInstance.DecodeThrottleFrames default unchanged

- **Given** `CameraInstance.cs:33`
- **When** read after this story
- **Then** the default `public int DecodeThrottleFrames { get; set; } = 5;` is unchanged (it remains the safe single-camera default before any throttle calculation runs)

### AC6: Tests pass and builds are clean

- **Given** the changes from AC1–AC5
- **When** `dotnet test SmartLog.Scanner.Tests` and both TFM builds are run
- **Then** all tests pass and both `net8.0-maccatalyst` and `net8.0-windows10.0.19041.0` builds succeed with no new warnings

### AC7: Frame-skip values preserved for cap range

- **Given** the new inline policy
- **When** `activeCount ∈ {1, 2}`
- **Then** the assigned value is `5`
- **And when** `activeCount ∈ {3, 4}`
- **Then** the assigned value is `8`

---

## Scope

### In Scope
- Delete `SmartLog.Scanner.Core/Services/AdaptiveDecodeThrottle.cs`
- Remove the DI registration line in `MauiProgram.cs`
- Inline the policy at the three known call sites in `MultiCameraManager.cs` and `MainViewModel.cs`
- Verify no other call sites exist via grep (the plan will confirm exhaustiveness)
- Update XML doc comments on `CameraInstance.DecodeThrottleFrames` if they reference `AdaptiveDecodeThrottle` by name

### Out of Scope
- Changing the throttle policy (5 / 8 stay as-is)
- Removing frame skipping entirely (separate decision; explicitly out of scope per epic)
- Adding telemetry / instrumentation around decode throughput
- Touching the camera worker or capture session code

---

## Technical Notes

### Inline pattern

```csharp
// Before:
cam.DecodeThrottleFrames = AdaptiveDecodeThrottle.Calculate(activeCount);

// After:
cam.DecodeThrottleFrames = activeCount <= 2 ? 5 : 8;
```

The `Math.Max(activeCount, 1)` guard at `MultiCameraManager.cs:197` is no longer needed: with the inline form, `activeCount <= 2` correctly handles the 0/1/2 cases identically (all yield 5).

### Files to change

| File | Change |
|------|--------|
| `SmartLog.Scanner.Core/Services/AdaptiveDecodeThrottle.cs` | Delete |
| `SmartLog.Scanner/MauiProgram.cs` (line ~325) | Remove `AddSingleton<AdaptiveDecodeThrottle>()` |
| `SmartLog.Scanner.Core/Services/MultiCameraManager.cs` (line ~197) | Replace `Calculate(...)` call with inline ternary; drop the now-redundant `Math.Max(activeCount, 1)` |
| `SmartLog.Scanner/ViewModels/MainViewModel.cs` (line ~371) | Replace `Calculate(count)` with inline ternary |
| `SmartLog.Scanner/ViewModels/MainViewModel.cs` (line ~402) | Replace `Calculate(filtered.Count)` with inline ternary |
| `SmartLog.Scanner.Core/Models/CameraInstance.cs` (line ~30) | Update XML doc comment if it references `AdaptiveDecodeThrottle` |

### Discovery command

```bash
grep -rn "AdaptiveDecodeThrottle" SmartLog.Scanner SmartLog.Scanner.Core SmartLog.Scanner.Tests
```

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|--------------------|
| `activeCount == 0` | Inline ternary yields `5` (matches old `<= 0 => 5` branch) |
| `activeCount > 4` | Cannot occur after US0129 caps at 4. If a caller passes 5+ before the cap fix, ternary yields `8` — same as old `3 => 8, 4 => 8` branch; the engine cap (US0129) rejects before the value reaches a worker |
| Test directly invokes `AdaptiveDecodeThrottle.Calculate(...)` | Test must be updated to assert against the inline expression instead, or removed if it was only validating the lookup table mechanism |
| Static analysis flags the magic numbers `5` and `8` | Acceptable — the policy is short and visible; if a linter complains, suppress at site or extract a private constant in `MultiCameraManager` (plan-level decision) |

---

## Test Scenarios

- [ ] `find SmartLog.Scanner.Core -name "AdaptiveDecodeThrottle.cs"` returns no results
- [ ] `grep -rn "AdaptiveDecodeThrottle" SmartLog.Scanner SmartLog.Scanner.Core` returns no results
- [ ] All three call sites use the inline ternary form
- [ ] `MultiCameraManager` initialized with 1 camera → `DecodeThrottleFrames == 5`
- [ ] `MultiCameraManager` initialized with 2 cameras → both have `DecodeThrottleFrames == 5`
- [ ] `MultiCameraManager` initialized with 3 cameras → all have `DecodeThrottleFrames == 8`
- [ ] `MultiCameraManager` initialized with 4 cameras → all have `DecodeThrottleFrames == 8`
- [ ] Existing `MultiCameraManagerTests` pass
- [ ] Both TFM builds clean

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| [US0129](US0129-lower-engine-camera-cap.md) | Soft predecessor — not strictly blocking | Cap of 4 ensures the inline `<= 2 ? 5 : 8` covers all possible runtime values | Draft (this epic) |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| None | — | — |

---

## Estimation

**Story Points:** 1
**Complexity:** Low — file delete, one DI line removal, three identical inline replacements. Largest risk is missing a fourth call site or a test that calls `Calculate(...)` directly — grep handles both.

---

## Open Questions

- [ ] Should `5` and `8` be exposed as private constants in `MultiCameraManager` (e.g., `LowDecodeThrottle` and `HighDecodeThrottle`) for clarity, or left as raw numbers? **Proposed: leave as raw numbers** — three call sites, comment-line context suffices. Constants would re-introduce a small named-thing layer that the inline form was meant to remove.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-05-06 | AI Assistant | Initial draft from Scanner Slim-down review (EP0018). Option A (inline + delete) selected by user. |
