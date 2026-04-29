# PL0023: Setup Wizard — Compact Layout

> **Status:** Draft
> **Story:** [US0125: Setup Wizard — Compact Layout (No Scroll, Sticky Save)](../stories/US0125-setup-wizard-compact-layout.md)
> **Epic:** [EP0001: Device Setup and Configuration](../epics/EP0001-device-setup-and-configuration.md)
> **Created:** 2026-04-28
> **Language:** C# 12 / .NET 8 MAUI (Windows + macOS)
> **Drafted by:** Claude (Sonnet 4.6) + MarkMarmeto

---

## Overview

Restructure `SetupPage.xaml` from a single-column scrolling list of four cards to a row-based grid that fits a typical desktop window without scrolling for the common 1–3 camera setup case. The four cards' inner content is unchanged — only their outer layout placement moves.

Final structure:

```
Row 0: Header                                 (fixed at top, never scrolls)
Row 1: Save error banner                      (fixed at top, conditional)
Row 2: ScrollView wrapping body Grid:         (scrolls if content exceeds height)
       ┌─────────────────────┬──────────────┐
       │ 🌐 Server Connection│ 🔐 Security  │  ← row 0 of body, two columns
       ├─────────────────────┴──────────────┤
       │ 📷 Scanner Configuration            │  ← row 1 of body, full width
       ├────────────────────────────────────┤
       │ 🎥 Camera Configuration             │  ← row 2 of body, full width
       └────────────────────────────────────┘
Row 3: Sticky save bar — Save + Back buttons  (fixed at bottom, always visible)
```

This is a **XAML-only** change. `SetupViewModel` is not touched. `SetupPage.xaml.cs` gains one method (`OnSizeAllocated` override) to handle the narrow-window breakpoint per Q4.

---

## Acceptance Criteria Mapping

| AC (US0125) | Phase |
|-------------|-------|
| AC1: Header unchanged | Phase 1 (preserved verbatim in row 0) |
| AC2: Save error banner unchanged | Phase 1 (preserved in row 1) |
| AC3: Server Connection + Security side-by-side | Phase 1 (body Grid row 0, two `*` columns) |
| AC4: Scanner Configuration full-width row | Phase 1 (body Grid row 1, `ColumnSpan=2`) |
| AC5: Camera Configuration full-width row | Phase 1 (body Grid row 2, `ColumnSpan=2`) |
| AC6: Sticky save bar at bottom | Phase 2 (page-level Grid row 3, outside ScrollView) |
| AC7: Header + save bar fixed; only body scrolls | Phase 1 + Phase 2 (header above ScrollView, save bar below) |
| AC8: TLS warning visual unchanged | Phase 1 (Security card content copy-pasted verbatim) |
| AC9: Narrow-window fallback | Phase 3 (`OnSizeAllocated` reflow at 900 px breakpoint) |
| AC10: Bindings unchanged | Phase 1 — every `{Binding ...}` expression verbatim |
| AC11: Save button state unchanged | Phase 2 — same `SaveCommand`, `IsSaving`, `SaveButtonText` bindings |

---

## Open Questions Resolution

Defaults from US0125, pending user override:

- **Q1 (button order):** Save **right-aligned**, Back **left-aligned** in the sticky bar. Both vertically centred. → Phase 2.
- **Q2 (separator):** Soft upward shadow on the sticky bar (matches MainPage statistics footer pattern). → Phase 2.
- **Q3 (per-camera rows):** Unchanged from today (vertical stack inside each row). Internal scroll within the Camera Configuration card if 4+ cameras configured AND body taller than viewport. → No code change.
- **Q4 (breakpoint):** **900 px**. Below this, upper row collapses to single column. → Phase 3.

If any default is reversed, the affected phase's diff is small (≤30 lines).

---

## Technical Context (Verified)

### Confirmed via code read (`SetupPage.xaml` HEAD)

- **Outer container** (line 11): `<ScrollView>` wrapping `<VerticalStackLayout Padding="30" Spacing="20" MaximumWidthRequest="600">`. The `MaximumWidthRequest="600"` is what forces the narrow column today — must be removed for two-column layout.
- **Header** (lines 15–25): `VerticalStackLayout` with title + `PageTitle` subtitle. Self-contained — extract verbatim into Phase 1's row 0.
- **Save error banner** (lines 28–46): conditional `Border` bound to `SaveError`. Self-contained — extract verbatim into Phase 1's row 1.
- **Server Connection card** (lines 49–158): white-on-shadow `Border` with HMAC-style internal layout. Self-contained.
- **Security Configuration card** (lines 161–250): includes the orange TLS warning box (lines 209–248) — that warning box is fully nested inside the card, so when the card moves the warning moves with it. No special handling needed.
- **Scanner Configuration card** (lines 253–364): includes the EP0012/US0122 USB checkbox at lines 324–342.
- **Camera Configuration card** (lines 367–486): includes the `CollectionView` of `CameraSlotViewModel` at lines 419–483.
- **Save button** (lines 489–502): bound to `SaveCommand`, `IsSaving`, `SaveButtonText`.
- **Back button** (lines 505–515): conditional on `IsEditMode`, bound to `CancelCommand`. Has a peculiar `Margin="0,-30,0,40"` to overlap the save button — that needs to go in the new flat sticky bar.
- **`SetupPage.xaml.cs`** (49 lines): currently only constructor + lifecycle plumbing; we'll add an `OnSizeAllocated` override.
- **No converters needed** — all `StringNotNullOrEmptyConverter`, `InvertedBoolConverter`, `EnumNotNoneConverter`, `TestingTextConverter`, `TestResultIconConverter`, `TestResultColorConverter` already registered in `App.xaml`. Verified by current usage in `SetupPage.xaml`.

### Files to touch

| File | Change |
|------|--------|
| `SmartLog.Scanner/Views/SetupPage.xaml` | Full restructure — outer Grid (page-level) wraps header / save banner / ScrollView (body) / sticky save bar. Inner card content copy-pasted verbatim. Net effect: same total content, ~50 lines added for the new outer Grid structure (519 → ~570 lines). |
| `SmartLog.Scanner/Views/SetupPage.xaml.cs` | Add `OnSizeAllocated` override (~15 lines) to handle the 900 px breakpoint reflow. Existing constructor and lifecycle methods unchanged. |
| `SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs` | **No change.** |
| `SmartLog.Scanner.Tests/ViewModels/SetupViewModelTests.cs` | **No change.** This is an XAML-only story. |

---

## Implementation Phases

### Phase 1 — Body Grid restructure (XAML)

**File:** `SmartLog.Scanner/Views/SetupPage.xaml`

Wholesale rewrite of the page structure. The four card `<Border>` elements (Server, Security, Scanner, Camera Config) are copy-pasted verbatim from the current XAML — only their parent wrappers and `Grid.Row` / `Grid.Column` attributes change.

**New page-level structure:**

```xml
<ContentPage xmlns="..." x:Class="..." x:DataType="vm:SetupViewModel" ...>

    <Grid RowDefinitions="Auto,Auto,*,Auto" BackgroundColor="#F8F9FA">

        <!-- Row 0: Header — fixed at top -->
        <VerticalStackLayout Grid.Row="0"
                             Spacing="5"
                             Padding="30,30,30,20"
                             HorizontalOptions="Center">
            <Label Text="SmartLog Scanner" FontSize="32" ... />
            <Label Text="{Binding PageTitle}" FontSize="16" ... />
        </VerticalStackLayout>

        <!-- Row 1: Save error banner — fixed at top, conditional -->
        <Border Grid.Row="1"
                IsVisible="{Binding SaveError, Converter={StaticResource StringNotNullOrEmptyConverter}}"
                Margin="30,0,30,10"
                ...>
            ... (verbatim from current lines 28–46) ...
        </Border>

        <!-- Row 2: Body — scrollable content area -->
        <ScrollView Grid.Row="2" x:Name="BodyScrollView" Padding="30,0,30,20">

            <!-- Inner body Grid: row 0 has two columns, rows 1+2 are full-width -->
            <Grid x:Name="BodyGrid"
                  RowDefinitions="Auto,Auto,Auto"
                  ColumnDefinitions="*,*"
                  RowSpacing="20"
                  ColumnSpacing="20">

                <!-- Row 0, Column 0: Server Connection -->
                <Border Grid.Row="0" Grid.Column="0" x:Name="ServerCard" ...>
                    ... (verbatim from current lines 49–158) ...
                </Border>

                <!-- Row 0, Column 1: Security Configuration -->
                <Border Grid.Row="0" Grid.Column="1" x:Name="SecurityCard" ...>
                    ... (verbatim from current lines 161–250) ...
                </Border>

                <!-- Row 1, full width: Scanner Configuration -->
                <Border Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="2" ...>
                    ... (verbatim from current lines 253–364) ...
                </Border>

                <!-- Row 2, full width: Camera Configuration -->
                <Border Grid.Row="2" Grid.Column="0" Grid.ColumnSpan="2" ...>
                    ... (verbatim from current lines 367–486) ...
                </Border>

            </Grid>
        </ScrollView>

        <!-- Row 3: Sticky save bar — fixed at bottom -->
        ... (Phase 2) ...

    </Grid>
</ContentPage>
```

**Key differences from current XAML:**

- The outermost `<ScrollView><VerticalStackLayout MaximumWidthRequest=600>` is replaced by a 4-row page Grid.
- The `MaximumWidthRequest="600"` constraint is gone — the body Grid takes full available width.
- Header `Margin="0,20,0,30"` becomes `Padding="30,30,30,20"` on the header `VerticalStackLayout` for consistent edge spacing.
- Each card moves into a specific `Grid.Row` / `Grid.Column` slot — outer styling (white background, rounded corners, shadow) preserved verbatim.
- `x:Name` attributes added to `BodyScrollView`, `BodyGrid`, `ServerCard`, `SecurityCard` so Phase 3 code-behind can reflow them at the breakpoint.

**Inner card contents:** absolutely no changes. Copy-paste current lines as-is into the new wrappers. Bindings, converters, styling, padding, spacing all preserved (AC10 + AC8).

### Phase 2 — Sticky save bar (XAML)

Replace the current bottom `Save` + `Back` buttons (lines 489–515) with a sticky row at the page Grid's row 3:

```xml
<!-- Row 3: Sticky save bar -->
<Border Grid.Row="3"
        BackgroundColor="White"
        Padding="30,16"
        StrokeThickness="0">
    <Border.Shadow>
        <Shadow Brush="Black" Opacity="0.08" Radius="16" Offset="0,-6" />
    </Border.Shadow>

    <Grid ColumnDefinitions="Auto,*,Auto">

        <!-- Back button — left-aligned, only in edit mode -->
        <Button Grid.Column="0"
                Text="← Back to Scanner"
                IsVisible="{Binding IsEditMode}"
                Command="{Binding CancelCommand}"
                BackgroundColor="Transparent"
                BorderColor="#999999"
                BorderWidth="1"
                TextColor="#666666"
                CornerRadius="8"
                Padding="20,12"
                FontSize="14"
                VerticalOptions="Center" />

        <!-- Save button — right-aligned, always visible -->
        <Button Grid.Column="2"
                Text="{Binding SaveButtonText}"
                Command="{Binding SaveCommand}"
                IsEnabled="{Binding IsSaving, Converter={StaticResource InvertedBoolConverter}}"
                BackgroundColor="#4D9B91"
                TextColor="White"
                CornerRadius="8"
                Padding="24,14"
                FontSize="16"
                FontAttributes="Bold"
                VerticalOptions="Center"
                MinimumWidthRequest="200">
            <Button.Shadow>
                <Shadow Brush="Black" Opacity="0.2" Radius="8" Offset="0,4" />
            </Button.Shadow>
        </Button>

    </Grid>
</Border>
```

**Notes:**

- The `Border` with upward shadow (`Offset="0,-6"`) gives the visual separator from the scrollable body (Q2 default).
- Back button on column 0 (left), Save on column 2 (right), with a `*` spacer in column 1 (Q1 default).
- Save button has `MinimumWidthRequest="200"` so it doesn't shrink awkwardly on narrow windows.
- The peculiar `Margin="0,-30,0,40"` overlap on the current Back button is gone — both buttons sit in a flat row.
- `IsEditMode` still hides the Back button on first-launch flow.

### Phase 3 — Narrow-window breakpoint (code-behind)

**File:** `SmartLog.Scanner/Views/SetupPage.xaml.cs`

Add an `OnSizeAllocated` override that reflows the body Grid's column structure when the page width crosses the 900 px breakpoint:

```csharp
public partial class SetupPage : ContentPage
{
    private const double TwoColumnBreakpointPx = 900;
    private bool _wasSingleColumn = true; // initial state assumed narrow

    public SetupPage(SetupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width <= 0) return; // first measure pass

        var shouldBeSingleColumn = width < TwoColumnBreakpointPx;
        if (shouldBeSingleColumn == _wasSingleColumn) return; // no breakpoint cross

        _wasSingleColumn = shouldBeSingleColumn;
        ApplyResponsiveLayout(shouldBeSingleColumn);
    }

    /// <summary>
    /// US0125 AC9: Reflows the upper row of the body Grid between two-column (Server + Security
    /// side-by-side) and single-column (stacked) based on window width. Called only when the
    /// breakpoint is crossed to avoid layout-thrash flicker.
    /// </summary>
    private void ApplyResponsiveLayout(bool singleColumn)
    {
        if (BodyGrid is null) return;

        if (singleColumn)
        {
            // Single column: 1 col, 4 rows. Server row 0, Security row 1, Scanner row 2, Camera row 3.
            BodyGrid.ColumnDefinitions.Clear();
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            BodyGrid.RowDefinitions.Clear();
            for (int i = 0; i < 4; i++)
                BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            Grid.SetRow(SecurityCard, 1);
            Grid.SetColumn(SecurityCard, 0);
            Grid.SetColumnSpan(SecurityCard, 1);

            Grid.SetRow(ScannerCard, 2);
            Grid.SetColumnSpan(ScannerCard, 1);

            Grid.SetRow(CameraCard, 3);
            Grid.SetColumnSpan(CameraCard, 1);
        }
        else
        {
            // Two column: 2 cols, 3 rows. Server (0,0), Security (0,1), Scanner (1,*), Camera (2,*).
            BodyGrid.ColumnDefinitions.Clear();
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            BodyGrid.RowDefinitions.Clear();
            for (int i = 0; i < 3; i++)
                BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            Grid.SetRow(SecurityCard, 0);
            Grid.SetColumn(SecurityCard, 1);

            Grid.SetRow(ScannerCard, 1);
            Grid.SetColumnSpan(ScannerCard, 2);

            Grid.SetRow(CameraCard, 2);
            Grid.SetColumnSpan(CameraCard, 2);
        }
    }
}
```

**Required `x:Name` references on the cards** (added in Phase 1): `BodyGrid`, `ServerCard`, `SecurityCard`, `ScannerCard`, `CameraCard`.

**Why programmatic, not VisualStateManager:** MAUI's `VisualStateManager` doesn't natively support width-based triggers without custom triggers — code-behind is the standard pattern (verified against MAUI 8 docs). The `_wasSingleColumn` guard ensures the reflow only fires on breakpoint crossings, not on every resize tick.

**Initial state:** XAML defines the two-column layout (`ColumnDefinitions="*,*"`, three rows, Security at row 0 col 1, etc.). On the first `OnSizeAllocated` call, if width >= 900, `_wasSingleColumn` flips from `true` (initial) to `false` and we don't actually reflow (since the XAML already represents the two-column state). The first reflow only happens when the user's actual window crosses the breakpoint.

**Edge case:** if the very first measure reports width = 0 or width < 900, the page renders as the XAML-defined two-column briefly, then reflows to single-column. To avoid this flash, set `_wasSingleColumn = false` initially (matching XAML) and trust the first `OnSizeAllocated` to reflow if needed. Actually, better: in the constructor, after `InitializeComponent()`, peek at `Width` and call `ApplyResponsiveLayout(Width < 900)` if `Width > 0`. Otherwise rely on `OnSizeAllocated`. Plan: do both — set `_wasSingleColumn = false` initially (matches XAML), let `OnSizeAllocated` flip if needed.

### Phase 4 — Manual verification

**4.a — macOS dev** (`dotnet run --project SmartLog.Scanner -f net8.0-maccatalyst`):

1. **Wide window (1280+ px):** header at top, two-column body (Server | Security), Scanner Config full-width, Camera Config full-width, sticky save bar at bottom.
2. **Resize to ~1000 px:** layout still two-column (above breakpoint).
3. **Resize to ~800 px:** upper row collapses to single column (Server above Security). Save bar stays sticky.
4. **Resize back to 1200 px:** layout reflows back to two-column without flicker.
5. **Edit mode (existing install):** Back button visible at left of save bar; Save at right.
6. **First-launch mode:** Back button hidden; Save fills the right side of the bar.
7. **Tall content** (configure 8 cameras): body scrolls within ScrollView; header + save bar stay fixed.
8. **TLS warning visual** unchanged (orange border, light orange background, ⚠️ icon, multi-paragraph text + checkbox).
9. **Save error path:** trigger validation failure (empty server URL); red banner shows below header, doesn't push save bar.
10. **All field bindings still work:** edit each field, click Save → verify `SetupViewModel.SaveCommand` runs as today.

**4.b — Windows hardware** (separate session):

1. Build on Windows: `dotnet publish SmartLog.Scanner -f net8.0-windows10.0.19041.0 -c Release -o ./publish/win-x64`.
2. Test at 1366×768 (low-end laptop), 1920×1080 (standard desktop), 2560×1440 (high-DPI).
3. Confirm two-column layout fits at 1366 px wide; sticky save bar visible without scroll for 1–3 cameras.
4. Confirm narrow window fallback works on Windows resize.
5. Confirm `Save` and `Back` buttons render correctly with their shadow + border treatments.

---

## Risks & Considerations

- **`OnSizeAllocated` flicker on first paint.** First measure may report width = 0, causing a wrong-state reflow. Mitigated by the `_wasSingleColumn` initial value matching the XAML default and the `width <= 0` early return.
- **Card content overflow at narrow widths.** The Camera Configuration card's per-camera rows have inputs that may not shrink below ~280 px legibly. Below 900 px (single column), the card is full-width again — this should be fine. Verify in Phase 4.a step 3.
- **Sticky save bar on Mac Catalyst.** MacCatalyst has occasionally inconsistent shadow rendering. The upward `Offset="0,-6"` shadow may render less prominently than on Windows. Acceptable — primary visual separator is the white-on-grey background contrast.
- **Save button width on very narrow screens (<600 px).** With `MinimumWidthRequest="200"` and back button at left, button-pair plus padding may exceed the bar width. Below 600 px the layout is unsupported for setup (kiosk PCs are >= 768 px). Acceptable.
- **`Grid.SetColumnSpan` / `Grid.SetRow` on bound elements.** Reflowing while bindings are active is safe in MAUI — verified against MAUI documentation. No state loss.
- **Cross-build limitation:** XAML changes mean Windows build must run on a Windows host (per CLAUDE.md). Phase 4.a covers macOS dev; Phase 4.b is the Windows session.
- **Setup wizard tests** (`SetupViewModelTests.cs`) — ViewModel is unchanged; existing tests must remain green. Confirmed in Phase 4 by running `dotnet test`.

---

## Out of Scope

- Per-camera row redesign (Q3 default = unchanged).
- New `SetupViewModel` properties / commands.
- Tab-based or wizard-step navigation.
- Dark mode.
- Mobile / tablet form factors.

---

## Estimated Effort

| Phase | Time |
|-------|------|
| 1 — XAML restructure (careful copy-paste of card content into new Grid) | ~2 h |
| 2 — Sticky save bar | ~30 min |
| 3 — Code-behind breakpoint reflow | ~45 min |
| 4.a — macOS verification (4 widths × 1/3/8 camera counts) | ~45 min |
| 4.b — Windows hardware verification | ~30 min |
| **Total** | **~4.5 hours** |

3 points story; aligns with one focused half-day session.

---

## Rollout Plan

1. Continue on `dev` branch.
2. Phase 1 — XAML body Grid + card relocation. Build clean, run app once on macOS to confirm cards render in correct positions.
3. Phase 2 — sticky save bar. Build clean.
4. Phase 3 — code-behind breakpoint. Build clean. Resize window through breakpoint to confirm reflow works.
5. Phase 4.a — macOS verification.
6. **Pause for user review** before Windows hardware verification.
7. Phase 4.b — Windows hardware.
8. Update US0125 + PL0023 status to Done. Update indexes. Commit.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-28 | Claude (Sonnet 4.6) + MarkMarmeto | Initial plan drafted; verified `SetupPage.xaml` structure (519 lines, 4 cards, scroll wrapper). Defaults proposed for Q1–Q4. ~4.5 h estimate. Ready for user review before Phase 1 execution. |
