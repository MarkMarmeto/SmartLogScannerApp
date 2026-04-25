# US0091: Scanner Tile — Fix Section Name Trimming, Show Program Code

> **Status:** Done
> **Epic:** EP0011: Multi-Camera Scanning (cross-project) / Scan Feedback
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-04-24

## User Story

**As a** Guard Gary (at the gate)
**I want** the student's Section to display in full (not truncated) on the scanner tile, alongside the Program Code
**So that** I can positively confirm the student's identity without squinting or missing details when multiple students approach the gate.

## Context

### Persona Reference
**Guard Gary** — Visual-confirms each scan against the person in front of him.

### Background
The scanner currently renders a student feedback tile with Name, Grade, and Section. The Section name is truncated (either by UI width or a character limit) which can clip meaningful suffixes like "STEM-Aquinas". Additionally, the tile does not display the Program Code, which — now that Programs are first-class in EP0010 — is a useful cue when multiple programs share a grade level (e.g. Grade 11 STEM vs Grade 11 ABM).

---

## Inherited Constraints

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| EP0010 | Data | Scan API response carries Program alongside Grade and Section | Server already returns the needed data |
| EP0011 | UI | Camera tiles are rendered in a responsive grid (1-8 cameras) | Section + Program must fit without breaking the tile layout at any grid size |
| PRD | UX | Scan feedback is shown for ~2-3 seconds before the next scan | Text must be immediately readable, not require scroll/interaction |

---

## Acceptance Criteria

### AC1: Full Section Name Rendered
- **Given** a scan matches a student in Section "STEM-Aquinas"
- **Then** the scanner feedback tile shows the full Section name "STEM-Aquinas" (no truncation, no ellipsis)

### AC2: Program Code Displayed
- **Given** a scan is accepted for a student
- **Then** the tile shows `Grade · Program · Section` (e.g. "Grade 11 · STEM · STEM-Aquinas")

### AC3: Responsive Line Wrapping
- **Given** the camera grid is at a narrow column width (e.g. 4 cameras wide)
- **Then** Grade/Program/Section wraps cleanly to a second line rather than clipping
- **And** font size remains readable at 2-3 second glance distance

### AC4: Fallbacks
- **Given** a scan for a student whose Program is null/legacy
- **Then** the tile shows `Grade · Section` without the Program segment (graceful fallback)

### AC5: Visitor and Rejection Tiles Unaffected
- **Given** a visitor pass scan or a rejection
- **Then** the existing layouts (visitor card, rejection reason) are not changed

---

## Scope

### In Scope
- Scanner student feedback tile layout
- Remove any hardcoded truncation / ellipsis on Section field
- Add Program Code display from scan response
- Responsive behaviour validated at camera counts 1, 2, 4, 6, 8

### Out of Scope
- Visitor tile layout
- Rejection reason layout
- Parent phone / SMS opt-in indicator changes

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Very long Section name (e.g. 40+ chars) | Wraps to next line; if still overflows, ellipsis at final line only with full text available via tooltip on hover (desktop) |
| REGULAR program | Shown explicitly: "Grade 7 · REGULAR · 7-A" |
| Program field absent in older API response | Fallback to Grade · Section |
| Grade and Section fit on one line | Keep on one line; don't force a wrap |

---

## Test Scenarios

- [ ] Long Section name renders in full at single-camera layout
- [ ] Long Section name wraps cleanly at 4-camera grid
- [ ] Program Code appears in tile for students with a Program
- [ ] Fallback to Grade · Section when Program missing
- [ ] No visual regression on visitor / rejection tiles
- [ ] Readable at 2-second glance (manual smoke test with guard)

---

## Technical Notes

### Files to Modify
- **Modify:** scanner tile template — likely a DataTemplate in `SmartLog.Scanner/Views/` or a `CameraSlot` XAML region
- **Modify:** `CameraSlotViewModel` (or `CameraSlotState`) — expose `ProgramCode` from scan response
- **Modify:** `ScanApiService` response DTO — confirm Program field is deserialised (already returned by server)

### Layout
- Use a FlexLayout / horizontal StackLayout with wrap for Grade/Program/Section row
- No width clamps or fixed-width text containers

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| EP0010 / US0058 | Data | Program entity + API response carries Program | Done (WebApp) |
| [US0011](US0011-color-coded-student-feedback-display.md) | UI foundation | Existing student tile component | Done |

---

## Estimation

**Story Points:** 2
**Complexity:** Low — layout and a binding addition

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-24 | Claude | Initial story drafted from V2 planning session |
