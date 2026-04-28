# US0126: ID-Card-Style Station Cards (Avatar Top, Device Strip Bottom)

> **Status:** Draft
> **Epic:** [EP0012: Concurrent Multi-Modal Scanning](../epics/EP0012-concurrent-multi-modal-scanning.md)
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-04-28

## User Story

**As** Guard Gary
**I want** each scan station card to look like a student ID card — avatar at the top, student details in the middle, and the camera/scanner identity at the bottom — with the device strip subtly colour-coded for cameras (green) vs. USB (purple)
**So that** my eyes are drawn to the student information first (which is the whole point of the scan), and the device that produced the scan is a quiet identifier underneath rather than a header that competes with the student data

## Context

### Persona Reference

**Guard Gary** — School security guard. Watches one lane at a time. Today's per-station card (US0124) leads with `Cam 1 + ENTRY` as a header, then shows student details below. After working with the layout, the device label at the top reads as the "headline" of the card, when really the student is the headline. Gary's eye should land on the student first; the camera identity is reference info.
[Full persona details](../personas.md#guard-gary)

### Background

Commit `4c3919c` (US0124) restructured `MainPage` so each camera and USB slot has its own station card showing full student details during a 1-second flash. The card layout is currently:

```
┌────────────────────┐
│ Cam 1     [ENTRY]  │  ← Header: name + scan-type badge
├────────────────────┤
│ Status text or:    │
│ Student #          │
│ LRN                │  ← Body
│ Grade · Section    │
├────────────────────┤
│ ✓ Accepted (msg)   │  ← Bottom banner — only during flash
│ time               │
└────────────────────┘
```

US0126 inverts the visual hierarchy: the student details become the always-visible primary content (with skeleton placeholders when no scan has happened), and the device identity moves to a small bottom strip:

```
┌────────────────────┐
│        👤          │  ← Avatar at top (generic placeholder)
│ ────────────────── │
│ ────────────────── │  ← Student details (skeleton or real)
│ ────────────────── │
├────────────────────┤
│ Cam 1              │  ← Bottom strip: device name + status
│ Ready to Scan      │    (green border = camera, purple = USB)
└────────────────────┘
```

When a scan flashes:
- Avatar stays generic (Q2 — server-side photo loading is out of scope)
- Detail rows fill with the real student number / LRN / grade · section
- Bottom strip's border colour pulses based on result (green = Accepted, amber = Duplicate, red = Rejected)
- Bottom strip's text swaps from "Ready to Scan" to the scan result message ("✓ Juan Cruz — Accepted")
- After 1 second, the slot resets — student details return to skeleton, bottom strip returns to "Ready to Scan" with default device colour

The per-card ENTRY/EXIT badge is **removed** (Q3) — the global ENTRY MODE pill in the top status bar is the single source of truth for scan type, consistent with US0089's device-level scan type model.

The statistics footer (queue / today / sync) remains at the bottom of the page (Q5) — unchanged.

The card width logic is the existing `CardWidth` proportional formula from US0124 (Q6) — body width divided equally among active cards. A `MaximumWidthRequest` clamp prevents cards from becoming unreadably wide when only 1 card is configured on a 1080p display.

---

## Inherited Constraints

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| US0124 | Architecture | Per-camera scan gate (`_cameraGated`), per-slot flash timers (`_flashTimers`), independent `CameraSlotState` per card | Unchanged — only the visual template changes |
| US0089 | Behaviour | Scan type is device-level (one ENTRY/EXIT for the whole device, not per-camera) | Per-card scan-type badge removed — top bar pill is the only display |
| EP0012 | UX | Distinct visual identity for camera vs. USB cards | Bottom strip colour: green (`#4CAF50`) for cameras, purple/indigo (`#6A4C93`) for USB |
| US0076 | UX | Visitor scans use a blue accent (`#2196F3`) | Bottom strip colour shifts to blue during a visitor scan flash |
| TRD | Architecture | MVVM with `[ObservableProperty]` source generators | New idle/flash state derives from existing `ShowFlash` boolean — no new ViewModel properties needed |
| PRD | Performance | UI must remain at 60 fps under 4-camera concurrent scanning | Skeleton state is static MAUI elements (no per-frame work); flash transition is a single border-colour change |

---

## Acceptance Criteria

### AC1: Avatar at the Top of Every Card

- **Given** any station card renders (camera or USB, idle or flashing)
- **When** the card is visible
- **Then** a circular avatar block sits at the top of the card showing a generic 👤 person glyph at ~85 px diameter (matches the avatar size from the previously deleted central student card)
- **And** the avatar background is light teal (`#E0F2F1`) when the card has student data (post-flash) or light grey (`#EEEEEE`) skeleton when idle
- **And** the avatar does NOT load a server-side photo — generic glyph only (Q2)

### AC2: Student Detail Block Always Visible (Skeleton When Idle, 4 Labelled Rows Matching Previous Central Card)

- **Given** a station card is idle (`ShowFlash = false`)
- **When** the card renders
- **Then** the student detail block shows **four labelled rows** with skeleton placeholders, matching the previously deleted central card layout:
  1. **STUDENT NUMBER** — bound to `LastStudentId`
  2. **STUDENT NAME** — bound to `FlashStudentName` (which falls back to ID when name is null)
  3. **LRN** — bound to `LastLrn` (with "N/A" target-null value)
  4. **GRADE · PROGRAM · SECTION** — bound to `LastGradeSection` (existing computed: emits `"Grade 11 · STEM · A"` when Program is set, `"Grade 11 · A"` when Program is null/empty)
- **And** each label is small uppercase grey text (`FontSize=9`, `TextColor=#9E9E9E`, `CharacterSpacing=1.2`) above the value/skeleton
- **And** the skeleton is a light grey rounded rectangle (`#EEEEEE`) sized to roughly match the real text height
- **And** when `ShowFlash = true`, the skeletons hide and the real labels show
- **And** for a visitor scan flash, only the STUDENT NAME row is visible (showing "Visitor Pass #N" via `FlashStudentName`); the STUDENT NUMBER, LRN, and GRADE · PROGRAM · SECTION rows are hidden via `IsVisitorScan` / `InvertedBoolConverter` bindings (visitor passes carry no student-number / LRN / grade metadata)

### AC3: Device Identity Strip at the Bottom

- **Given** any station card renders
- **When** the card is visible
- **Then** a strip at the bottom of the card displays the device name (e.g., "Cam 1") on the first line and the current status text ("Ready to Scan" when idle, or the scan result message during flash) on the second line
- **And** the strip uses a green tint (`#4CAF50`) for camera cards and a purple/indigo tint (`#6A4C93`) for the USB card when idle
- **And** during a flash, the strip colour shifts to match the scan result: green for Accepted, amber (`#FF9800`) for Duplicate / DebouncedLocally / RateLimited, red (`#F44336`) for Rejected / Error, blue (`#2196F3`) for visitor scans

### AC4: Per-Card Scan-Type Badge Removed

- **Given** the rebuilt station card template
- **When** the card renders
- **Then** there is no `ScanType` (ENTRY/EXIT) badge anywhere on the card
- **And** the global ENTRY MODE pill in the top status bar remains the single source of scan-type display (`CurrentScanType` binding unchanged)

### AC5: Bottom Strip Status Text Reverts on Flash End

- **Given** a card has just finished its 1-second flash (`ShowFlash` flipped to false)
- **When** the reset completes
- **Then** the bottom strip status text returns to "Ready to Scan"
- **And** the bottom strip colour returns to the default device colour (green for cameras, purple for USB) — visitor blue / amber / red are flash-only

### AC6: Card Sizing — Proportional with 1080p Visibility Floor

- **Given** the page renders on a 1920 × 1080 display
- **When** N cards (1–4) are visible
- **Then** card widths divide the body width equally per the existing `CardWidth` formula (1 = 100 %, 2 = 50/50, 3 = 33 × 3, 4 = 25 × 4) — same as US0124
- **And** a `MaximumWidthRequest` of ~480 px clamps individual cards so a single-card layout does not stretch the card across the full 1920 px width
- **And** card height stays under ~400 px so the full card fits in the body region (1080 − top bar (~70 px) − camera preview (~280 px) − statistics footer (~80 px) ≈ 650 px available; cards comfortably fit)

### AC7: Camera Preview Position Unchanged

- **Given** the page renders in camera mode (`IsCameraMode = true`)
- **When** the body draws
- **Then** the camera 0 live preview remains at the top of the body, centred, ~320 × 240 px — unchanged from US0124

### AC8: Statistics Footer Unchanged

- **Given** the page renders
- **When** the bottom of the page draws
- **Then** the statistics footer (queue count card, today scan count card, manual sync card, inline `SyncStatusMessage` label) remains as today — same content, same row, same styling

### AC9: Top Status Bar Unchanged

- **Given** the page renders
- **When** the top of the page draws
- **Then** the date/time, logo, connectivity pill, ENTRY MODE pill, navigation buttons, and "Scanning Status" stack are all unchanged — same content, same bindings

### AC10: Per-Camera Gate and Flash Timer Behaviour Unchanged

- **Given** the existing per-camera scan gate (`_cameraGated[cameraIndex]`) and per-slot flash timers (`_flashTimers`)
- **When** US0126 lands
- **Then** these mechanics are unchanged — incoming scans are still dropped on a card while it is mid-flash; flash duration is still 1 second; cards reset their student detail fields and `ShowFlash = false` on timer fire

### AC11: ScanResult Field Wiring Unchanged

- **Given** any scan completes (camera or USB)
- **When** `TriggerSlotFlash` / `TriggerUsbSlotFlash` populate the slot
- **Then** all existing field assignments (`LastStudentId`, `LastLrn`, `LastGrade`, `LastSection`, `LastProgram`, `LastScanTime`, `IsVisitorScan`, `LastScanStatus`, `LastScanMessage`, `FlashStudentName`) continue identically — no new slot properties are needed; the XAML re-binds the same data into the new template

---

## Open Questions

> All resolved 2026-04-28 (MarkMarmeto):

- [x] **Q1:** Idle = **skeleton** (greyed placeholder rows). Real student details only during flash.
- [x] **Q2:** Avatar = **generic 👤** glyph (same as previously deleted central card). No server-side photo loading.
- [x] **Q3:** **Remove** per-card ENTRY/EXIT badge entirely. Rely on global ENTRY MODE pill in top bar.
- [x] **Q4:** Bottom strip during flash = **option (a)** — colour pulses based on result and text shows the scan result message; reverts to "Ready to Scan" + default device colour after 1 s.
- [x] **Q5:** Statistics footer **kept at bottom**.
- [x] **Q6:** **Proportional `CardWidth`** logic from US0124 retained, with a `MaximumWidthRequest` clamp so cards stay readable on 1080p.

---

## Technical Notes

(Full implementation in PL0024.)

- **No `CameraSlotState` / `UsbScannerSlotState` field changes.** All data needed is already exposed by US0124 (`LastStudentId`, `LastLrn`, `LastGrade`, `LastSection`, `LastProgram`, `LastScanTime`, `IsVisitorScan`, `LastScanStatus`, `LastScanMessage`, `FlashStudentName`, `ShowFlash`, `DisplayName`).
- **`MainViewModel` not touched** — `TriggerSlotFlash`/`TriggerUsbSlotFlash` populate the same fields the new template reads.
- **`MainPage.xaml` station card `DataTemplate` rebuild:**
  - Outer `Border` keeps `WidthRequest="{Binding ... CardWidth}"`, gains `MaximumWidthRequest="480"` (Q6 1080p clamp).
  - Inner `Grid` rows: `Auto` (avatar), `*` (student details — skeleton or real), `Auto` (bottom device strip).
  - Avatar block: `Border` with `BackgroundColor` switching between skeleton grey and active teal via a converter or two stacked `Border`s (one visible at idle, one at flash) — pattern carried over from the previously deleted central card.
  - Student detail block: each of the 3 rows is two stacked elements — a skeleton `Border` (visible when `!ShowFlash`) and a real `Label` (visible when `ShowFlash`). `InvertedBoolConverter` gates the skeleton.
  - Bottom strip: `Border` with `BackgroundColor` bound to a result-aware colour; two `Label`s stacked (`DisplayName` + status text). Status text binds to a single string property — see below.
- **New computed property on `CameraSlotState` and `UsbScannerSlotState`:** `BottomStripStatusText` — returns `LastScanMessage` when `ShowFlash`, otherwise the literal `"Ready to Scan"`. Property changes raised when `ShowFlash` or `LastScanMessage` change. ~6 lines per ViewModel.
- **New computed property:** `BottomStripColor` — returns `FlashColor` when `ShowFlash`, otherwise the default device colour (green for cameras, purple for USB). Cascades from `ShowFlash` and `LastScanStatus`. ~10 lines per ViewModel.
- The existing `DisplayBrush` (used today on the outer card border) can stay or be retired — depends on whether the redesign keeps a coloured outer border. Recommendation: retire the outer-border colour shift; keep the outer card subtly bordered grey (`#E0E0E0`) and let the bottom strip carry all the colour signalling. Visually cleaner.
- Tests: extend `UsbScannerSlotStateTests` with cases for `BottomStripStatusText` and `BottomStripColor` transitions. `CameraSlotState` remains unreachable from tests (lives in MAUI project — same constraint as US0124 / PL0021).

---

## Definition of Ready

- [x] User story format complete
- [x] All ACs are testable
- [x] Inherited constraints traced
- [x] Open questions Q1–Q6 resolved (2026-04-28)
- [x] Persona linked
- [x] Technical notes capture file-level changes

---

## Definition of Done

- [ ] All ACs verified
- [ ] Build clean: 0 errors / 0 new warnings
- [ ] `dotnet test SmartLog.Scanner.Tests` — 225+ tests passing (extended for new computed properties)
- [ ] macOS dev verification: 1, 2, 3, 4 cards × Camera/USB/Both modes × 1920×1080 + 1366×768 + 1280×800
- [ ] Visual confirmation: avatar at top, skeleton at idle, bottom strip colour matches device + flash result
- [ ] Visitor scan path: avatar + visitor pass label + blue bottom strip during flash
- [ ] Per-card ENTRY/EXIT badge fully removed; ENTRY MODE pill in top bar still works
- [ ] Windows hardware verification (separate session)

---

## Risks

- **Layout breakage on tall content.** If a real student name is unusually long (>2 lines) it may push the bottom strip off-card. Mitigation: `LineBreakMode="TailTruncation"` on student name; cap the detail block at a max height; the bottom strip is always sized last via a `Grid Row="Auto"` placement.
- **Skeleton-vs-real swap flicker.** Switching `IsVisible` on stacked skeleton + label elements could cause a brief layout shift. Mitigation: both elements have identical `HeightRequest` so the layout slot doesn't change size; only opacity-equivalent visibility flips.
- **Bottom strip colour read on rapid scans.** A card flashing back-to-back (Camera 1 scans Student A then Student B 1.5 s later) has the bottom strip colour change twice in 2.5 s — risk of confusion. Mitigation: existing per-camera gate (`_cameraGated`) prevents new scans during the 1 s flash, so back-to-back scans always get a clean reset between them. Verified by US0124's existing flow.
- **Single-card layout looks empty on a wide display.** With `MaximumWidthRequest="480"` and only 1 card configured, the card sits in a sea of white space (the body is centred but otherwise empty). Acceptable — this matches the wireframe (window 3 shows a single centred card with empty space around). Mitigation only if UAT reports it feels barren.
- **Avatar block dominating short cards.** ~85 px avatar + ~120 px detail block + ~50 px bottom strip ≈ 255 px minimum card height. If `MaximumWidthRequest` keeps cards narrow but the body is short on a small window, vertical breathing room may feel cramped. Acceptable for now; revisit if real Windows hardware feedback complains.

---

## Estimated Effort

3 points. Roughly half a day:

| Phase | Time |
|-------|------|
| 1 — Add `BottomStripStatusText` + `BottomStripColor` to `CameraSlotState` and `UsbScannerSlotState` | ~30 min |
| 2 — Rebuild station card `DataTemplate` in `MainPage.xaml` (camera + USB versions) | ~2 h |
| 3 — Drop per-card `ScanType` binding usage; verify global ENTRY MODE pill is the only badge | ~15 min |
| 4 — Tests for new computed properties (`UsbScannerSlotStateTests`) | ~30 min |
| 5 — macOS verification across card counts and display sizes | ~45 min |
| 6 — Windows hardware verification (separate session) | ~30 min |
| **Total** | **~4.5 hours** |

---

## Out of Scope

- Server-side student photo loading (Q2 default = generic 👤 glyph; future story if requested).
- Animated avatar / photo crossfade transitions.
- Per-card ENTRY/EXIT toggle (US0089 already moved scan type to device-level).
- Additional card layouts beyond 1, 2, 3, 4 active cards (the system supports up to 8 cameras + USB = 9 cards, but the wireframe only mocks up 1–4; 5–9 will use the same flex-wrap behaviour from US0124).
- Audio / haptic redesign on flash.
- Dark mode.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-28 | MarkMarmeto + Claude (Sonnet 4.6) | Initial story drafted under EP0012; layout direction set by user via wireframe (4 windows showing 4/3/1/2 card configurations). Avatar moves to top of card, device identity moves to bottom strip, per-card ENTRY/EXIT badge removed. Q1–Q6 all resolved. |
| 2026-04-28 | MarkMarmeto | Review feedback applied — six post-review decisions: (1) `MaximumWidthRequest` bumped 480 → **520 px**; (2) avatar size stays 80 px; (3) **outer card border colour-shift on flash retained** (binds `DisplayBrush` / `DisplayColor` as today — both bottom strip AND outer border pulse colour together); (4) **USB barcode glyph kept** — moved into the bottom strip, left of the device name (white fill on indigo); (5) `DisplayBrush` / `DisplayColor` properties stay alive (they're now actively bound to the outer border again, so #5 is moot); (6) detail block expanded to **4 labelled rows matching the previously deleted central card design** — STUDENT NUMBER, STUDENT NAME, LRN, GRADE · PROGRAM · SECTION (Program included inline via existing `LastGradeSection` formatter). For visitor scans: STUDENT NAME row remains visible carrying "Visitor Pass #N"; STUDENT NUMBER, LRN, GRADE rows hidden. |
