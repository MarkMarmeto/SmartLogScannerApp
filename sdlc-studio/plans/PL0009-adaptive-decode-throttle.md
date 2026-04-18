# PL0009: Adaptive Decode Throttle — Implementation Plan

> **Status:** Complete
> **Story:** [US0067: Adaptive Decode Throttle](../stories/US0067-adaptive-decode-throttle.md)
> **Epic:** EP0011: Multi-Camera Scanning
> **Created:** 2026-04-18
> **Language:** C# / .NET MAUI 8

## Overview

Stateless `AdaptiveDecodeThrottle` class with a static `Calculate(int)` method returning a frame-skip count based on active camera count. `MultiCameraManager` calls `UpdateThrottleValues()` to push the value to all `CameraInstance.DecodeThrottleFrames`. All phases complete.

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | Throttle Table | 1→5, 2→5, 3→8, 4→8, 5→10, 6→12, 7→13, 8→15 |
| AC2 | Minimum Floor | Returns ≥ 3 for any input |
| AC3 | Dynamic Recalculation | `UpdateThrottleValues()` recalculates on camera count change |
| AC4 | Applied Per Camera | `DecodeThrottleFrames` set on each `CameraInstance` |
| AC5 | Single Camera Unchanged | `Calculate(1)` → 5 |
| AC6 | Singleton Registration | One instance shared by `MultiCameraManager` |

---

## Implementation Phases

### Phase 1: AdaptiveDecodeThrottle ✅
- [x] `SmartLog.Scanner.Core/Services/AdaptiveDecodeThrottle.cs`
  - Static `Calculate(int activeCameraCount)` with lookup table
  - `Math.Max(result, 3)` floor guard
  - Registered as singleton in `MauiProgram.cs`

### Phase 2: Integration in MultiCameraManager ✅
- [x] `UpdateThrottleValues()` — `AdaptiveDecodeThrottle.Calculate(activeCount)`, pushed to all `CameraInstance.DecodeThrottleFrames`
- [x] Called at `InitializeAsync` time and after camera start/stop

### Phase 3: Tests ✅
- [x] `AdaptiveDecodeThrottleTests.cs` — all breakpoints verified

---

## Edge Case Handling

| # | Edge Case | Handling Strategy | Phase |
|---|-----------|-------------------|-------|
| 1 | Count = 0 | `Math.Max(activeCount, 1)` before Calculate | Phase 2 |
| 2 | Count > 8 | Returns 15 (8-camera value) | Phase 1 |

**Coverage:** 2/2

---

## Definition of Done

- [x] All acceptance criteria implemented
- [x] Unit tests written and passing
- [x] Build succeeds (0 errors)
