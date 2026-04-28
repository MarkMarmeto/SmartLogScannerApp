# US0124: Per-Station Student Detail Cards

> **Status:** Draft
> **Epic:** [EP0012: Concurrent Multi-Modal Scanning](../epics/EP0012-concurrent-multi-modal-scanning.md)
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-04-28

## User Story

**As** Guard Gary
**I want** each scan station (every webcam, plus the USB scanner) to display the scanned student's full details on its own card
**So that** when multiple lanes scan students at the same time, every operator sees the result for *their* lane on *their* card — not whatever the "last scan wins" central display happens to be showing

## Context

### Persona Reference

**Guard Gary** — School security guard. At a busy gate with 2–3 webcam lanes plus a handheld USB scanner for visitor passes, Gary doesn't watch a shared display in the middle of the screen. He watches the lane in front of him. Today's central student card forces him to mentally correlate "did that result come from my lane or the next one?" — slowing throughput and creating uncertainty during a rush.
[Full persona details](../personas.md#guard-gary)

### Background

The current `MainPage` layout splits the body into two columns:

- **Left column:** camera 0 live preview (top) + a `FlexLayout` of compact camera slot status cards (1–8 cards, each ~220 px wide showing camera name, ENTRY/EXIT badge, status, and a brief 1-second flash with student name only) + a USB indicator card (US0123).
- **Right column:** one large central student card (`Border x:Name="studentCard"`) showing student number, LRN, name, grade · program · section, scan time, and a coloured border indicating Accepted / Duplicate / Rejected. This card is updated by `MainViewModel.OnScanCompleted` from **whichever camera (or USB) scan fired most recently**.

The central card's "last scan wins" semantic is the root of the multi-camera coordination pain we patched in commit `5b1562b` (the `_centralCardCts` fix). Even with the cancellation token in place, two operators looking at the same shared card cannot tell whose scan is currently displayed without checking the small camera slot card next to it. With 3 webcams firing during a class change, the central card flickers between students — the operator at lane 1 cannot confidently confirm "the system saw my student" until the per-slot flash on lane 1's small card lights up.

This story eliminates the shared central card entirely. Each station's small slot card grows into a full "scan station card" that shows everything the central card used to show (student number, LRN, name, grade · program · section, scan time, coloured outcome border) plus what it already shows (camera name, ENTRY/EXIT badge, ready state). The body of `MainPage` becomes a single full-width adaptive grid of these cards, sized for the realistic deployment (1–3 webcams + 0–1 USB scanner).

The per-camera scan gate (`_cameraGated` array in `MainViewModel`) already ensures one card finishes its display cycle before accepting the next scan from the same camera — that mechanic stays. What changes is purely the visual surface: the per-slot card now carries the full student identity, and the shared central display goes away.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | UX | Zero decision-making during scanning for Guard Gary | Each lane's operator must be able to read their own scan result without checking a separate display element |
| EP0012 | UX | Visual differentiation between camera and USB cards (icon, color, layout) | New layout preserves indigo accent for USB, teal/red for camera ENTRY/EXIT badges, status-coloured border for the active flash |
| EP0011 | Architecture | Per-camera isolation — one camera's failure must not affect others | Each card renders from its own `CameraSlotState`; no shared writeable state across cards |
| US0068 | Pattern | `MainPage` renders camera slots in a `FlexLayout` with `BindableLayout.ItemsSource` | Layout container can change (Grid / FlexLayout / etc.) but the item template binding to `CameraSlotState` remains |
| US0123 | Pattern | USB indicator card is a peer element with the same flash mechanic as camera cards | USB card grows the same student detail block; flash mechanic from US0123 stays unchanged |
| US0091 | Format | Section trim and program code formatting in the central card | Identical formatting carries to the per-slot card (`{Grade} · {Program} · {Section}`) |
| TRD | Architecture | MVVM with `[ObservableProperty]` source generators | New student detail fields on `CameraSlotState` and `UsbScannerSlotState` use the same pattern |

---

## Acceptance Criteria

### AC1: Slot State Carries Full Student Identity

- **Given** the `CameraSlotState` and `UsbScannerSlotState` ViewModels
- **When** US0124 is built
- **Then** both expose the following observable properties (in addition to fields they already have): `LastStudentId`, `LastLrn`, `LastGrade`, `LastSection`, `LastProgram`, `LastScanTime` (string `HH:mm:ss`), `IsVisitorScan` (bool)
- **And** both expose a computed `LastGradeSection` returning `{Grade} · {Program} · {Section}` with `· Program` omitted when `LastProgram` is null/empty (matches existing central-card formatting from US0091)
- **And** the property-changed cascades for `LastGrade`, `LastSection`, `LastProgram` notify `LastGradeSection`

### AC2: Slot Card Renders Student Details During Flash

- **Given** any camera slot card OR the USB indicator card
- **When** `ShowFlash = true` (the slot is mid-display after a scan)
- **Then** the card visibly shows: result icon (✓/⚠/✗/📥/⏱), student number (large, prominent), full name, LRN (or "N/A" if null), `LastGradeSection`, the friendly scan message, and the scan time
- **And** the card border is coloured per the result status (green Accepted, amber Duplicate / DebouncedLocally / RateLimited, red Rejected / Error, blue for visitor scans)
- **And** when `ShowFlash = false` the card returns to its idle state (camera name, ENTRY/EXIT badge, "● Ready to Scan", neutral green border) and the student detail rows collapse out of view

### AC3: Visitor Scan Surface

- **Given** a slot card is flashing for a visitor pass scan
- **When** the card renders
- **Then** the student-number row shows `Visitor Pass #{N}` instead of a student ID; the LRN and grade · section rows are hidden (visitor passes carry no student metadata)
- **And** the border colour follows the existing visitor palette (`#2196F3` blue per US0076-AC2)

### AC4: Central Student Card Removed

- **Given** the rebuilt `MainPage`
- **When** the page renders
- **Then** there is no large shared student card on the right side of the body; the previous central-card `Border` (`x:Name="studentCard"`) and its avatar/skeleton/footer markup are gone
- **And** the `MainViewModel` properties used solely by the removed card (`LastStudentId`, `LastLrn`, `LastStudentName`, `LastGrade`, `LastSection`, `LastProgram`, `LastGradeSection`, `HasScannedStudent`, `CardBorderColor`, `LastScanValid`, `LastScanMessage` — when no longer used by anything else, `ShowFeedback`, `FeedbackColor`, `_currentOptimisticScanAt`, `_centralCardCts`) are deleted from `MainViewModel`
- **And** `OnScanCompleted` and `OnScanUpdated` are simplified to keep only history logging, sound playback, statistics updates, and per-slot flash routing — the central-card switch statement is removed

### AC5: Adaptive Layout — Cards Fill Page Width Proportionally

- **Given** the `Scanner.Mode` preference and the configured camera count (1–8 supported, 1–3 typical)
- **When** the page renders
- **Then** scan station cards (camera slots with `IsVisible = true` + the USB card if `IsUsbMode`) divide the available body width **equally** in a single row
- **And** the per-card width is computed as `bodyWidth ÷ totalActiveCards` minus inter-card spacing, so 1 camera + USB = 2 cards each ~50 % wide; 2 cameras + USB = 3 cards each ~33 % wide; 3 cameras + USB = 4 cards each ~25 % wide
- **And** when total active cards exceed 4, cards wrap to a second row (each row equally distributes its share)
- **And** a `MinimumWidthRequest` of ~260 px prevents the student detail block from collapsing illegibly on very narrow displays — if `bodyWidth ÷ totalActiveCards` drops below the minimum, FlexLayout wraps

### AC6: Per-Camera Gate Behaviour Preserved

- **Given** the per-camera scan gate (`_cameraGated[cameraIndex]`) added in commit `5b1562b`
- **When** this story lands
- **Then** the gate continues to drop incoming scans on a camera while that camera's card is mid-flash
- **And** each camera's flash timer (`_flashTimers[cameraIndex]`) remains independent — Camera 1's flash finishing has no effect on Camera 2's flash timer

### AC7: Camera 0 Live Preview Placement

- **Given** `Scanner.Mode = "Camera"` or `"Both"` (camera pipeline active)
- **When** the page renders
- **Then** the existing camera 0 live preview (`<controls:CameraPreviewView x:Name="CameraPreview0" />`) remains visible at a fixed location on the page (decision: top of body row, full-width sized down) — the preview is *not* embedded into camera 0's station card in this story (deferred — see Out of Scope)
- **And** in `USB`-only mode, the live preview is hidden (existing behaviour from US0123)

### AC8: Feedback Banner Moves Per-Card; Statistics Footer Hosts Sync Messages

- **Given** the bottom feedback banner (`Border Grid.Row="2"` showing `LastScanMessage` + `StatusIcon`) and the statistics footer (queue count, today scan count, manual sync)
- **When** this story lands
- **Then** the **shared bottom feedback banner is removed** — but the same prominent treatment (large icon, coloured background matching `FlashColor`, white message text, scan time) is preserved as a **per-card bottom banner row inside each station card**, so every operator gets the same long-distance "did it work?" cue on their own card
- **And** the per-card banner background uses `FlashColor` when `ShowFlash` is true and is hidden otherwise (idle cards show only the header + status text, no coloured strip)
- **And** the **statistics footer remains unchanged** in structure (queue / today / sync card row stays)
- **And** sync-result and queue-cleared messages (currently surfaced via `ShowFeedback` from `ManualSync`/`ClearQueue`/`OnSyncCompleted`) route to a thin inline label below the statistics card row — auto-clears via a single `_syncStatusCts`

### AC9: Scan Log Parity Unchanged

- **Given** any scan completes via camera or USB
- **When** `LogScanToHistoryAsync` runs
- **Then** the persisted `ScanLogEntry` fields remain identical to before US0124 (no schema or attribution changes — this story is UI-only)

### AC10: Top Status Bar Unchanged

- **Given** the top status bar (date/time, logo, connectivity pill, scan-type toggle, navigation buttons, scanning status text)
- **When** this story lands
- **Then** the top bar markup and behaviour are unchanged
- **And** `StatusMessage` and `StatusIcon` (used by the top bar's "Scanning Status" stack) continue to update — these properties are NOT in the deletion list from AC4

### AC11: USB Card Health Warning Preserved

- **Given** the 60-second no-scan health warning from US0123
- **When** the USB card grows the new student detail block
- **Then** the warning state (`IsHealthWarning = true`) still flips the card border to amber and shows "⚠ No recent scans (1m+)" — the new layout does not break the warning surface

---

## Open Questions

- [x] **Q1 — Resolved 2026-04-28 (MarkMarmeto):** Camera 0 live preview stays at fixed top of body (AC7). Per-card embedding deferred to a future story.
- [x] **Q2 — Resolved 2026-04-28 (MarkMarmeto):** Null student name falls back to `LastStudentId`; full name fills in on `OnScanUpdated`.
- [x] **Q3 — Resolved 2026-04-28 (MarkMarmeto):** `Rejected` / `Error` / `RateLimited` show only the result icon + message; student detail rows are hidden.
- [x] **Q4 — Resolved 2026-04-28 (MarkMarmeto):** Shared bottom banner removed; the same prominent coloured strip lives inside each station card as a per-card bottom banner row. Operators retain the long-distance "did it work?" cue on their own lane's card.
- [x] **Q5 — Resolved 2026-04-28 (MarkMarmeto):** Sync / queue messages route to an inline label below the statistics card row in the footer; auto-clears via `_syncStatusCts`.

---

## Technical Notes

(Full implementation plan in PL0022.)

- `CameraSlotState` lives in `SmartLog.Scanner/ViewModels/` (MAUI project) — adding fields here. `UsbScannerSlotState` lives in `SmartLog.Scanner.Core/ViewModels/` — adding the same fields there. Both use `[ObservableProperty]` source generators.
- `TriggerSlotFlash(int cameraIndex, ScanStatus, string?, string?)` and `TriggerUsbSlotFlash(ScanResult)` in `MainViewModel` change to populate the new fields. Camera method's signature simplifies to `TriggerSlotFlash(int cameraIndex, ScanResult result)` — `FlashSourceSlot` collapses into the call.
- `OnScanCompleted` becomes a thin pass-through: history log + sound + stats + USB slot routing. The 70+ lines of central-card switch statement are deleted. `OnScanUpdated` similarly thins out — only the per-slot re-flash via `OnMultiCameraScanUpdated` (already present) and the optimistic-→-confirmed sound correction remain.
- `_centralCardCts` and the `Task.Delay(3000)` reset chains tied to the central card go away (the per-slot 1-second flash CTS via `_flashTimers` already manages each station card's lifecycle independently).
- XAML body grid restructures from `ColumnDefinitions="Auto,*"` to a single column with the camera 0 preview at the top and an adaptive grid of station cards below. Card minimum width grows from 220 → ~340 px to fit the student detail block.
- Tests: `CameraSlotStateTests` and `UsbScannerSlotStateTests` get new cases asserting that flash population sets all student detail fields and that clear empties them. `MainViewModel` integration remains unreachable from tests (per CLAUDE.md / PL0021 constraint) — covered by Phase 5 manual verification.
- Cross-build limitation per CLAUDE.md: XAML changes force Windows build verification on a Windows host.

---

## Definition of Ready

- [x] User story format complete (As / I want / So that)
- [x] All ACs are testable and reference current code lines / files where possible
- [x] Inherited constraints traced to PRD / TRD / parent stories
- [x] Open questions Q1–Q5 resolved (2026-04-28)
- [x] Persona reference linked
- [x] Technical notes capture file-level changes and the layout decision

---

## Definition of Done

- [ ] All ACs verified via the implementation plan (PL0022) phases
- [ ] `dotnet test SmartLog.Scanner.Tests` green (`CameraSlotStateTests` and `UsbScannerSlotStateTests` extended)
- [ ] macOS dev manual verification of layout for 1, 2, and 3 cameras (with and without USB)
- [ ] Windows hardware verification — vector elements still render correctly, layout doesn't clip on common Windows resolutions, real concurrent scans (camera + USB) populate the right cards
- [ ] PR review: no remaining references to deleted `MainViewModel` properties; `MainPage.xaml` no longer references `studentCard`, `HasScannedStudent`, `LastStudentName`, etc.
- [ ] Story status flipped to `Done` after Windows hardware sign-off

---

## Risks

- **Layout shift discomfort:** Operators familiar with the central card may need a few shifts to acclimate. Mitigation: pilot with one school for a week before broad rollout.
- **Card density on small screens:** Three full-detail station cards in a row on a 1366×768 laptop may force smaller fonts than ideal. Mitigation: test on the lowest-spec target hardware during macOS verification (or simulate via window-resize) and tune `MinimumWidthRequest` per card.
- **Long-distance visibility loss:** Removing the bottom feedback banner removes the cross-room "did it work?" cue. Mitigation: monitor in UAT; the per-slot border colour and result icon must be readable from ≥1 m. If not, restore a slim toast-style banner.
- **USB card detail fields populated but not visible:** If the USB card's flash template doesn't bind the new fields, the data is set but invisible. Mitigation: Phase 5 verifies USB scan flash shows full details, not just the name.
- **`MainViewModel` deletion regressions:** Removing properties used by other XAML elements (not just the central card) would break bindings silently. Mitigation: grep `MainPage.xaml` for each deleted property name before commit; build runs the XAML compiler and surfaces dangling bindings.

---

## Estimated Effort

5 points. Roughly 1 day of focused work — most of the time is in the XAML restructure and verifying no orphaned bindings remain, not in the ViewModel changes (~1 hr) or tests (~1 hr).

---

## Out of Scope

- **Embedding live preview per camera card.** Only camera 0 has a live preview today (cameras 1+ are headless workers). Per-card preview embedding requires per-platform handler attachment for each card and is a separate scope.
- **Animated transitions between idle and flashing card states** (e.g., crossfade, slide). Story is functional-only; animation polish defers to a future visual-polish story.
- **Reordering / drag-rearranging cards.** Layout is determined by camera index order from setup.
- **Per-card mute / disable.** Camera ENTRY/EXIT badges and the per-camera scan type are unchanged.
- **Theme support / dark mode.** Colour palette stays light-mode-only as today.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-28 | MarkMarmeto + Claude (Sonnet 4.6) | Initial story drafted under EP0012; targeted at the 3-webcam + 1-USB realistic deployment; eliminates the shared central card in favour of per-station student detail cards |
| 2026-04-28 | MarkMarmeto | Open questions Q1–Q5 resolved with all defaults accepted. Two refinements: AC5 sharpened to specify cards divide body width equally based on count (1+USB → 50/50; 2+USB → 33/33/33; 3+USB → 25/25/25/25); AC8 amended so the deleted shared bottom banner is reborn as a per-card bottom banner row (each card carries its own coloured result strip — long-distance "did it work?" cue preserved per lane). |
