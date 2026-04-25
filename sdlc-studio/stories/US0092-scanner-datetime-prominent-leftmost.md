# US0092: Scanner Header — Enlarge Date/Time, Anchor Left-Most

> **Status:** Done
> **Epic:** EP0011: Multi-Camera Scanning (cross-project) / Scan Feedback
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-04-24

## User Story

**As a** Guard Gary (at the gate)
**I want** the current date and time displayed prominently on the left edge of the scanner screen, larger than it is now
**So that** I can verify the clock at a glance (critical for timestamp integrity on every scan) without having to search the UI.

## Context

### Persona Reference
**Guard Gary** — Refers to the clock throughout the shift, especially during reconciliation or disputes.

### Background
Today the date/time element is small and placed where it competes with other header chrome. Clock legibility is a first-class need on the scanner — every scan's `scannedAt` timestamp depends on the device clock being right, and guards confirm this visually at shift start.

---

## Inherited Constraints

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| EP0011 | UI | Camera grid takes most of the screen; header is narrow | Date/time must be prominent without stealing grid space |
| PRD | UX | Scanner runs on a mix of display sizes (14" to 22" on-site) | Font scales responsively; stays readable at smallest supported display |

---

## Acceptance Criteria

### AC1: Left-Most Placement
- **Given** the scanner main page
- **Then** the current date and time is anchored at the left-most side of the header/toolbar
- **And** no other element (logo, menu, status chip) sits to its left

### AC2: Enlarged Font
- **Given** the current header styling
- **Then** the date/time font size is at minimum 1.5× its previous size
- **And** it remains readable from ~2 metres on a 14" display

### AC3: Two-Line or Single-Line Layout
- **Given** the enlarged size
- **Then** the time (HH:mm:ss or HH:mm) is the largest element
- **And** the date (e.g. "Thu, 24 Apr 2026") is above or below the time in a smaller but still readable weight

### AC4: Live-Updating
- **Given** the scanner is idle
- **Then** the clock ticks every second (or minute, matching the chosen precision) without visible jank
- **And** the display matches the system clock (no drift)

### AC5: Locale/Format
- **Given** the current app locale (en-PH default)
- **Then** date format is "EEE, dd MMM yyyy" and time format is 24-hour "HH:mm"
- **And** formatting respects the locale if the OS changes it

### AC6: No Regression at Small Viewport
- **Given** a narrow laptop display
- **Then** the enlarged date/time still fits alongside the rest of the toolbar without wrapping the toolbar controls out of reach

---

## Scope

### In Scope
- Header layout change on MainPage
- Typography (font size + weight) for date and time
- Live-updating clock (already present in some form) retained

### Out of Scope
- Timezone display
- NTP / clock-sync indicator (could be a future story)
- Setup page / other pages' headers (scope limited to the scanning surface)

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| System clock out of sync | Display current device time as-is (out of scope to detect drift here) |
| Very narrow display (<1000px wide) | Date wraps below time; toolbar remains usable; no horizontal scroll |
| RTL locale | Respect OS RTL direction (stretch goal; en-PH is LTR-only for the current product) |

---

## Test Scenarios

- [ ] Date/time is the left-most header element
- [ ] Font size is visibly larger (≥1.5×)
- [ ] Time updates every second without jank
- [ ] Format matches en-PH: "Thu, 24 Apr 2026" and "14:32"
- [ ] No toolbar regression at 1366×768 (common gate-PC resolution)
- [ ] Readable at 2-metre distance on 14" display

---

## Technical Notes

### Files to Modify
- **Modify:** `SmartLog.Scanner/Pages/MainPage.xaml` — header Grid / FlexLayout
- **Modify:** `SmartLog.Scanner/Resources/Styles/*.xaml` — add/adjust `HeaderDateLabel` + `HeaderTimeLabel` styles
- **Modify:** `MainViewModel` — ensure clock binding exists; if present, no change needed

### Clock Source
- Bind to `DateTime.Now` via a `Timer`-driven property (existing pattern if already implemented)

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| [US0068](US0068-main-page-camera-grid-ui.md) | Layout | MainPage layout exists | Done |

---

## Estimation

**Story Points:** 1
**Complexity:** Low — XAML + styles

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-24 | Claude | Initial story drafted from V2 planning session |
