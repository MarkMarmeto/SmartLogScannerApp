# US0067: Adaptive Decode Throttle

> **Status:** Done
> **Epic:** EP0011: Multi-Camera Scanning
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-04-18

## User Story

**As a** system
**I want** an adaptive frame-skip throttle that scales decode rate inversely with camera count
**So that** CPU usage stays manageable when running multiple cameras simultaneously and no single camera overwhelms the decode pipeline

## Context

### Persona Reference
**System** — Performance optimisation; no direct user interaction.

### Background
Each camera generates a continuous stream of frames. Without throttling, decoding every frame from 8 cameras simultaneously would saturate the CPU. `AdaptiveDecodeThrottle` provides a static lookup that maps active camera count to a frame-skip value (e.g., 8 cameras → skip every 15 frames). `MultiCameraManager` calls `UpdateThrottleValues()` to push the current skip value to each `CameraInstance.DecodeThrottleFrames`.

---

## Inherited Constraints

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| EP0011 | Performance | Total decode budget ~30 fps across all cameras | Throttle table must not exceed this budget |
| EP0011 | Minimum | Minimum 3-frame skip to prevent thrashing | `Calculate` returns ≥ 3 |

---

## Acceptance Criteria

### AC1: Throttle Table
- **Given** active camera count is provided
- **When** `AdaptiveDecodeThrottle.Calculate(count)` is called
- **Then** it returns: 1→5, 2→5, 3→8, 4→8, 5→10, 6→12, 7→13, 8→15

### AC2: Minimum Floor
- **Given** any input ≤ 0 or edge value
- **When** `Calculate` is called
- **Then** it returns at least 3

### AC3: Dynamic Recalculation
- **Given** cameras are running and one is stopped
- **When** `MultiCameraManager.UpdateThrottleValues()` is called
- **Then** the new skip value based on remaining active count is pushed to all `CameraInstance.DecodeThrottleFrames`

### AC4: Throttle Applied Per Camera
- **Given** `CameraInstance.DecodeThrottleFrames` is set
- **When** the platform worker processes frames
- **Then** only every N-th decoded QR payload is forwarded (existing `CameraQrScannerService` 500 ms debounce provides additional protection)

### AC5: Single Camera Unchanged
- **Given** camera count = 1
- **When** `Calculate(1)` is called
- **Then** returns 5 — identical effective behaviour to pre-EP0011 single-camera mode

### AC6: Registered as Singleton
- **Given** the app starts
- **When** `AdaptiveDecodeThrottle` is resolved from DI
- **Then** a single instance is shared by `MultiCameraManager`

---

## Scope

### In Scope
- `AdaptiveDecodeThrottle` stateless class with static `Calculate` method
- `MultiCameraManager.UpdateThrottleValues()` pushing values to `CameraInstance`
- DI registration as singleton in `MauiProgram.cs`

### Out of Scope
- Dynamic CPU-load-based throttle adjustment (noted as future enhancement)
- Per-camera independent throttle values (all cameras use same value for simplicity)

---

## Technical Notes

`AdaptiveDecodeThrottle.Calculate(int activeCameraCount)` uses a simple lookup. `MultiCameraManager` calls it at `InitializeAsync` time and again via `UpdateThrottleValues()` whenever a camera starts or stops.

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Count = 0 | Returns minimum 3 |
| Count > 8 | Returns 15 (capped at 8-camera value) |
| All cameras offline | `UpdateThrottleValues` uses `Math.Max(activeCount, 1)` to avoid divide-by-zero |

---

## Test Scenarios

- [x] `Calculate(1)` → 5
- [x] `Calculate(2)` → 5
- [x] `Calculate(3)` → 8
- [x] `Calculate(4)` → 8
- [x] `Calculate(5)` → 10
- [x] `Calculate(6)` → 12
- [x] `Calculate(7)` → 13
- [x] `Calculate(8)` → 15
- [x] `Calculate(0)` → ≥ 3 (floor)

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0066 | Predecessor | `MultiCameraManager` to call `UpdateThrottleValues` | Done |

---

## Estimation

**Story Points:** 2
**Complexity:** Low

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-18 | EP0011 implementation | Created; marked Done (implemented in EP0011 session) |
