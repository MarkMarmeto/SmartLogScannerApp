# US0125: Setup Wizard — Compact Layout (No Scroll, Sticky Save)

> **Status:** Draft
> **Epic:** [EP0001: Device Setup and Configuration](../epics/EP0001-device-setup-and-configuration.md)
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-04-28

## User Story

**As** IT Admin Ian
**I want** the setup wizard to fit within a typical desktop window without heavy scrolling, with the Save button always visible at the bottom
**So that** I can review every setting at a glance during initial configuration and don't have to hunt for the Save button after editing a field deep in the page

## Context

### Persona Reference

**IT Admin Ian** — School IT administrator responsible for first-time setup of every scanner PC. Configures server URL, credentials, camera count, and per-camera assignments. Today this means scrolling a 1100–1400 px tall page on a 768 px desktop window — losing visual context every time he scrolls past a section. Multiply by 20 PCs at deployment time and the friction adds up.
[Full persona details](../personas.md#it-admin-ian)

### Background

`SetupPage.xaml` (519 lines) is currently a single `ScrollView` containing a `VerticalStackLayout` with four stacked cards plus header and save button. On a 1024×768 or 1280×800 desktop window the page extends ~600 px below the viewport — Ian must scroll to see Camera Configuration and the Save button.

The four cards and their content are unchanged by this story — only their **outer layout** changes:

| Card | Today (single column, stacked) | After US0125 |
|------|-------------------------------|--------------|
| 🌐 Server Connection | Full-width row 1 | **Row 1, left half** |
| 🔐 Security Configuration | Full-width row 2 | **Row 1, right half** (TLS warning box visual treatment unchanged — only placement changes) |
| 📷 Scanner Configuration | Full-width row 3 | **Row 2, full width** |
| 🎥 Camera Configuration | Full-width row 4 | **Row 3, full width** |
| Save / Back buttons | Bottom of scroll content | **Sticky bottom bar — always visible regardless of scroll position** |

Inside each card, the field layout is unchanged in this story (no internal redesign). The TLS warning box keeps its prominent orange-bordered styling — only its parent card moves from row 2 (full-width) to row 1's right half.

The header (SmartLog Scanner title + page subtitle) stays at the top, unchanged.

The save error banner (the red strip that appears when validation fails) stays where it is in the flow — directly below the header — unchanged.

A `ScrollView` is still present so the page degrades gracefully on tiny windows or when the per-camera list grows beyond the visible area; but for a typical 1280×800 setup session with 1–3 cameras configured, no scrolling should be needed.

---

## Inherited Constraints

> See PRD and parent stories. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | UX | Minimal friction during initial setup; deployment is bulk (20+ PCs) | Sticky Save eliminates "scroll to save" hunt; two-column upper section keeps Server + Security visible together for cross-checking |
| US0004 | Pattern | `SetupPage` uses MAUI `ScrollView` + `VerticalStackLayout` with `MaximumWidthRequest=600` | Replace the `MaximumWidthRequest=600` constraint — that's what forces the narrow column. New layout sizes to the body width. |
| US0122 | Behaviour | "Also accept USB scanner input" checkbox in the Scanner Configuration card | Checkbox stays in Scanner Configuration card — only the card's row position changes |
| TRD | Architecture | MVVM bindings on `SetupViewModel` properties unchanged | This story changes XAML only — no `SetupViewModel` changes |

---

## Acceptance Criteria

### AC1: Header Unchanged

- **Given** the setup wizard renders
- **When** the page loads
- **Then** the header block (SmartLog Scanner title + `PageTitle` subtitle) is visually identical to today's wizard — same font sizes, colours, alignment, spacing

### AC2: Save Error Banner Unchanged in Position

- **Given** the user submits invalid input and `SaveError` is non-empty
- **When** the page renders
- **Then** the red error banner appears between the header and the cards row — same as today

### AC3: Server Connection and Security Side-by-Side

- **Given** the page body renders on a window wider than ~900 px
- **When** the layout draws
- **Then** the 🌐 Server Connection card occupies the **left half** of row 1
- **And** the 🔐 Security Configuration card occupies the **right half** of row 1
- **And** the two cards share the same `Grid.Row` and have equal column widths (`*`/`*` star sizing)
- **And** the cards have the same outer styling as today (white background, rounded corners, soft shadow)

### AC4: Scanner Configuration Full-Width Row

- **Given** the page body renders
- **When** the layout draws
- **Then** the 📷 Scanner Configuration card occupies **row 2, full width** — its inner field layout (Detected Device, Camera picker, USB scanner checkbox, Default scan type) is unchanged from today (vertically stacked inside the card)

### AC5: Camera Configuration Full-Width Row

- **Given** the page body renders
- **When** the layout draws
- **Then** the 🎥 Camera Configuration card occupies **row 3, full width** — its inner content (count stepper, USB 3.0 warning, per-camera CollectionView) is unchanged from today

### AC6: Save Button Sticky at Bottom

- **Given** the page renders, regardless of scroll position
- **When** the user is anywhere in the body (top, mid, or scrolled)
- **Then** the Save button is **always visible at the bottom of the page** — outside the ScrollView, in a dedicated row below the body
- **And** the "← Back to Scanner" button (visible only in `IsEditMode`) sits in the same sticky row, to the left or right of Save (decision: see Open Question Q1)
- **And** the sticky row has a subtle top border or shadow so it visually separates from the scrollable content above

### AC7: ScrollView Wraps Body Cards Only — Not Header or Save Bar

- **Given** the body content height exceeds the available vertical space (e.g., 8 cameras configured, narrow window)
- **When** the user scrolls
- **Then** only the cards area scrolls; the header at the top and the save bar at the bottom remain fixed in place

### AC8: TLS Warning Visual Treatment Unchanged

- **Given** the Security Configuration card renders
- **When** it draws
- **Then** the TLS warning box keeps its current orange (`#FF9800`) border, light orange (`#FFF3E0`) background, the ⚠️ icon, the multi-paragraph warning text, and the "Accept self-signed certificates" checkbox styled exactly as today
- **And** only the card's outer placement changes (row 2 full-width → row 1 right column)

### AC9: Narrow-Window Fallback (Below ~900 px Wide)

- **Given** the page renders on a window narrower than the breakpoint
- **When** the layout measures itself
- **Then** the two-column upper section collapses to single column (Server Connection above Security Configuration), the rest stacks normally, and the user can scroll through the body
- **And** the sticky save bar remains sticky regardless of window width

### AC10: Field Bindings Unchanged

- **Given** the rebuilt XAML
- **When** the page is bound to `SetupViewModel`
- **Then** every field bound today (`ServerUrl`, `ApiKey`, `HmacSecret`, `AcceptSelfSignedCerts`, `EnableUsbScannerInput`, `CameraCount`, `CameraSlots`, etc.) binds identically — no `SetupViewModel` properties added, removed, or renamed

### AC11: Save Button State Behaviour Unchanged

- **Given** `IsSaving` is true OR validation fails
- **When** the Save button renders
- **Then** the button's `Text` (`SaveButtonText`), `IsEnabled` gate, and `Command` binding are identical to today — same `ManualSync`-style button bound to `SaveCommand`

---

## Open Questions

- [x] **Q1 — Resolved 2026-04-28 (MarkMarmeto):** Save bar — Save **right-aligned**, Back **left-aligned**. Both vertically centred in the bar.
- [x] **Q2 — Resolved 2026-04-28 (MarkMarmeto):** Sticky bar separator — soft upward shadow (matches MainPage statistics footer pattern).
- [x] **Q3 — Resolved 2026-04-28 (MarkMarmeto):** Per-camera rows — unchanged. Inner card redesign deferred to a future story if 4+ camera setups become common.
- [x] **Q4 — Resolved 2026-04-28 (MarkMarmeto):** Breakpoint at **900 px**. Below this, upper row collapses to single column.

---

## Technical Notes

(Full implementation in PL0023.)

- This is a **XAML-only** change. `SetupViewModel` is not touched. `SetupPage.xaml.cs` may need a `SizeChanged` handler if we implement Q4 dynamically (otherwise a fixed `MinimumWidthRequest` on the body Grid suffices).
- Replace the outer `<ScrollView><VerticalStackLayout MaximumWidthRequest=600>...` with a three-row `Grid` (`RowDefinitions="Auto,*,Auto"`):
  - **Row 0:** Header + save error banner (unchanged content)
  - **Row 1:** ScrollView wrapping the body — body is itself a Grid (`RowDefinitions="Auto,Auto,Auto"`, `ColumnDefinitions="*,*"` for the upper row only) where Server Connection sits at `Grid.Row=0,Column=0`, Security at `Grid.Row=0,Column=1`, Scanner Config at `Grid.Row=1,ColumnSpan=2`, Camera Config at `Grid.Row=2,ColumnSpan=2`
  - **Row 2:** Sticky save bar with `Save` + `Back` buttons
- The `MaximumWidthRequest=600` constraint that forces narrow stacking goes away — the body grid takes full width.
- Card inner content is copy-pasted unchanged from current XAML, only their `Grid.Row` / `Grid.Column` placement changes.
- The body Grid sits inside a `ScrollView` so AC7 + AC9 fall out naturally — when content exceeds window height, the body scrolls inside its container while header (above ScrollView) and save bar (below ScrollView) stay fixed.
- Q4 narrow-window fallback — implement via `OnSizeAllocated` in code-behind: when `Width < 900`, programmatically reset the upper row's `ColumnDefinitions` to a single `*` and move Security card from `Column=1` to `Row=2,Column=0` (and shift Scanner/Camera rows down). Alternative: use MAUI `VisualStateManager` if it supports `Width` triggers cleanly. Plan picks the simpler approach.
- Cross-build limitation: XAML changes mean the Windows TFM build must run on Windows (per CLAUDE.md). macOS dev verification covers Phase 5a; Windows hardware Phase 5b.

---

## Definition of Ready

- [x] User story format complete (As / I want / So that)
- [x] All ACs are testable and trace to current code
- [x] Persona reference linked
- [x] Open questions Q1–Q4 resolved (2026-04-28)
- [x] Technical notes capture file-level changes

---

## Definition of Done

- [ ] All ACs verified
- [ ] Build clean: `dotnet build SmartLog.Scanner -f net8.0-maccatalyst` — 0 errors / 0 new warnings
- [ ] `dotnet test SmartLog.Scanner.Tests` — 225+ tests still passing (no test changes expected; story is XAML-only)
- [ ] macOS dev verification: window resized 800 / 1024 / 1280 / 1600 px wide — layout adapts cleanly
- [ ] macOS dev verification: 1, 3, 8 cameras configured — body scrolls only when content exceeds window; save bar stays sticky
- [ ] Windows hardware verification: identical checks on a Windows scanner PC at 1366×768, 1920×1080
- [ ] No regressions in the underlying setup flow (Save / Test Connection / Camera Test)

---

## Risks

- **Sticky save bar covers content on very short windows.** If window height < ~500 px, save bar + header eat most of the vertical space and the body becomes a thin slice. Mitigation: `MinimumHeightRequest` on the page or a hard floor on the body Grid; revisit if real-world deployment hits this.
- **`OnSizeAllocated` reflow flicker.** Programmatically rearranging the upper row's `ColumnDefinitions` on every resize tick can cause flicker. Mitigation: only reflow when crossing the 900 px breakpoint (compare prior vs. current bucket); skip otherwise. Or use `VisualStateManager` if it cleanly supports width triggers in MAUI.
- **`ScrollView` inside Grid Row sizing surprises.** MAUI `ScrollView` in a `*`-sized row sometimes doesn't measure correctly without an explicit `VerticalOptions="Fill"`. Mitigation: confirm during Phase 2 build; add explicit options if the body collapses.
- **Per-camera CollectionView still scrolls internally** if 4+ cameras configured. By design (Q3 default = leave unchanged) — but the card's height grows with row count, eating body space. If the body is taller than the viewport, the OUTER ScrollView scrolls; the CollectionView inside doesn't double-scroll. Verify in Phase 5.

---

## Estimated Effort

3 points. Roughly half a day:

| Phase | Time |
|-------|------|
| 1 — XAML restructure (most of the work — careful copy-paste of card inner content into the new Grid) | ~2 h |
| 2 — Sticky save bar styling + button order | ~30 min |
| 3 — Narrow-window breakpoint (`OnSizeAllocated` or `VisualStateManager`) | ~45 min |
| 4 — macOS verification across 4 widths × 1/3/8 camera counts | ~45 min |
| 5 — Windows hardware verification | ~30 min |
| **Total** | **~4.5 hours** |

---

## Out of Scope

- Per-camera row redesign (Q3 default = unchanged; future story if needed).
- Dark mode.
- Tablet / phone form factors — desktop-only as today.
- New `SetupViewModel` properties or commands.
- Field-level UI redesign (entry styling, picker styling, etc.) — purely an outer-layout story.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-28 | MarkMarmeto + Claude (Sonnet 4.6) | Initial story drafted under EP0001. Layout direction set by user: header unchanged, row 1 = Server + Security side-by-side, row 2 = Scanner Config full-width, row 3 = Camera Config full-width, sticky save bar at bottom. TLS warning box visual treatment unchanged (placement only). Q1–Q4 open. |
| 2026-04-28 | MarkMarmeto | Q1–Q4 all defaults approved. Story ready for Phase 1 execution. |
