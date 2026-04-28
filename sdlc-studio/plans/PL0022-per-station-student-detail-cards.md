# PL0022: Per-Station Student Detail Cards

> **Status:** Draft
> **Story:** [US0124: Per-Station Student Detail Cards](../stories/US0124-per-station-student-detail-cards.md)
> **Epic:** [EP0012: Concurrent Multi-Modal Scanning](../epics/EP0012-concurrent-multi-modal-scanning.md)
> **Created:** 2026-04-28
> **Language:** C# 12 / .NET 8 MAUI (Windows + macOS)
> **Drafted by:** Claude (Sonnet 4.6) + MarkMarmeto

---

## Overview

Eliminate the shared central student card (`<Border x:Name="studentCard">` on `MainPage.xaml` lines 363–584) and grow each per-station card (camera slots + USB indicator) to display the full student identity that the central card used to show. After this lands, the body of `MainPage` is a single-column layout: camera 0 live preview at the top (when in camera mode), then an adaptive row of "scan station" cards below — one per configured camera, plus the USB card if `IsUsbMode` — each carrying its own student details, scan time, and result-coloured border.

This is a UI-only refactor. The scan pipeline (HMAC validation, dedup, server submission, history logging, sound playback, statistics) is untouched. The per-camera scan gate (`_cameraGated`) and per-slot flash CTS dictionary (`_flashTimers`) added in commit `5b1562b` continue to govern each card's lifecycle independently — they were the architectural prerequisite for this redesign.

The story's open questions Q1–Q5 are resolved with the defaults proposed in US0124 (live preview stays at fixed top location; null student name falls back to ID; Rejected/Error/RateLimited hide student rows; bottom feedback banner is removed; sync messages move to an inline label inside the statistics footer). The plan is structured so any of those defaults can be reversed in a follow-up without re-architecting.

---

## Acceptance Criteria Mapping

| AC (US0124) | Phase |
|-------------|-------|
| AC1: Slot state carries full student identity | Phase 1 (`CameraSlotState` + `UsbScannerSlotState` field additions) |
| AC2: Slot card renders student details during flash | Phase 2 (XAML card template) + Phase 3 (`MainViewModel.TriggerSlotFlash` populates fields) |
| AC3: Visitor scan surface | Phase 2 (`IsVisitorScan` binding gates name format + LRN/grade visibility) |
| AC4: Central student card removed | Phase 2 (XAML deletion) + Phase 3 (`MainViewModel` property and switch-statement deletion) |
| AC5: Adaptive layout for 1–3 cameras + USB | Phase 1 (`CardWidth` computed prop) + Phase 2 (binding) + Phase 3.g (IsVisible cascade) |
| AC6: Per-camera gate behaviour preserved | No code change required — Phase 3 keeps `_cameraGated` and `_flashTimers` intact |
| AC7: Camera 0 live preview placement | Phase 2 (preview moves from left-column row 0 to body row 0, full-width) |
| AC8: Feedback banner removed; sync messages relocated | Phase 2 (delete `Grid.Row="2"` borders) + Phase 3 (sync inline label binding) |
| AC9: Scan log parity unchanged | No code change — `LogScanToHistoryAsync` untouched |
| AC10: Top status bar unchanged | No code change — `StatusMessage` / `StatusIcon` retained |
| AC11: USB card health warning preserved | Phase 2 (warning state styling is on the card border, unchanged by detail-block addition) |

---

## Technical Context (Verified)

### Confirmed via code read (current `dev` HEAD `5b1562b`)

- **`CameraSlotState.cs`** (`SmartLog.Scanner/ViewModels/`, 152 lines) — already exposes `DisplayName`, `ScanType`, `Status`, `ShowFlash`, `FlashStudentName`, `LastScanStatus`, `LastScanMessage`, `IsVisible`, computed `StatusText` / `FlashColor` / `FlashIcon` / `DisplayBrush`. **Missing for AC1:** `LastStudentId`, `LastLrn`, `LastGrade`, `LastSection`, `LastProgram`, `LastScanTime`, `IsVisitorScan`, computed `LastGradeSection`. Adding these is straightforward — the file pattern is `[ObservableProperty] private T _field;` plus `partial void OnFieldChanged` for cascades.
- **`UsbScannerSlotState.cs`** (`SmartLog.Scanner.Core/ViewModels/`, 124 lines) — same shape, in Core for test reachability. Same set of fields to add.
- **`MainViewModel.cs`** (1285 lines) — central card state lives in lines 31–78 (`LastStudentId`, `LastLrn`, `LastStudentName`, `LastGrade`, `LastSection`, `LastProgram`, `HasScannedStudent`, `CardBorderColor`, `LastScanValid`, `LastScanMessage`, `_currentOptimisticScanAt`, `FeedbackColor`, `ShowFeedback`, computed `LastGradeSection` with cascades). The big switch statement that updates these fields lives in `OnScanCompleted` lines 553–701 (~150 lines) and `OnScanUpdated` lines 757–803 (~50 lines). Both can be reduced to thin pass-throughs that keep only history logging, sound playback, statistics, and USB slot routing.
- **`_centralCardCts`** (line 128) and the `Task.Delay(3000, centralCardCts.Token)` chains added in commit `5b1562b` (six call-sites: `OnScanCompleted` line ~707, `ClearQueue` empty-queue ~815, `ClearQueue` success ~843, `ClearQueue` failure ~855, `ManualSync` failure ~885, `TestValidQr` no-secret ~905, `OnSyncCompleted` ~1119) — all six get reduced. The `OnScanCompleted` reset goes away entirely (per-slot card has its own 1-second flash). The five UI-action timers either go away (if `ShowFeedback` is deleted) or get redirected to the inline status footer label.
- **`MainPage.xaml`** (836 lines) — body Grid `Row="1"` lines 162–587 has the two-column structure (`ColumnDefinitions="Auto,*"`). The right column (`Grid.Column="1"` lines 360–586) is the central card and is fully removable. Bottom feedback banner (`Grid.Row="2"` lines 591–699) is two `Border` elements stacked (one for `ShowFeedback`-true, one for the "Ready" pulse state — both removable). Statistics footer (`Grid.Row="3"` lines 702–833) stays.
- **Camera 0 live preview** lives in left column row 0 (lines 175–191). Moving it to body Row 0 (above the new station-card row) is a single block relocation.
- **`OnMultiCameraScanCompleted`** (lines 332–363) calls `FlashSourceSlot` then `OnScanCompleted` — the second call drives the central card today. After this story, `OnScanCompleted` no longer touches the central card; `FlashSourceSlot` (renamed / inlined) populates the per-slot card with full details.
- **`OnMultiCameraScanUpdated`** (lines 354–372) similarly calls `FlashSourceSlot` (server-confirmed result re-paints the slot) and `OnScanUpdated`. The slot re-paint already works correctly because `TriggerSlotFlash` cancels and replaces the flash CTS — the server-confirmed result simply re-fires the flash with updated student data.
- **`InvertedBoolConverter`** is referenced by the central card (lines 414, 456, 480, 505, 528, 559) — once the central card is gone, verify whether this converter has any other callers (probably not). If unused, the `<x:StaticResource>` registration in `App.xaml` can be deleted in a cleanup pass; otherwise leave it.

### Files to touch

| File | Change |
|------|--------|
| `SmartLog.Scanner/ViewModels/CameraSlotState.cs` | Add 7 `[ObservableProperty]` fields, 1 computed `LastGradeSection`, 3 `partial void OnXxxChanged` cascades. ~30 lines added. |
| `SmartLog.Scanner.Core/ViewModels/UsbScannerSlotState.cs` | Same as above. ~30 lines added. |
| `SmartLog.Scanner/ViewModels/MainViewModel.cs` | Delete ~14 central-card properties (and their cascades), `_centralCardCts` field, `_currentOptimisticScanAt`. Simplify `OnScanCompleted` (delete 150-line switch). Simplify `OnScanUpdated` (delete 50-line switch). Refactor `TriggerSlotFlash` signature to take `ScanResult` and populate full detail fields. Refactor `TriggerUsbSlotFlash` similarly. Inline `FlashSourceSlot` into the call site. Net effect: ~250 lines deleted, ~80 lines refactored, ~40 lines added — **file shrinks by ~150 lines**. Sync messages from `ClearQueue` / `ManualSync` / `OnSyncCompleted` either get a new bound property (`SyncStatusMessage`) shown in the statistics footer with auto-clear via a single `_syncStatusCts`, or keep their existing `LastScanMessage` + new `ShowSyncToast` binding — exact wiring decided in Phase 3. |
| `SmartLog.Scanner/Views/MainPage.xaml` | Delete right column (~225 lines: lines 360–586). Delete bottom feedback banner (~110 lines: lines 591–699). Move camera 0 preview from left column row 0 to body row 0. Restructure body to single column. Expand camera slot `DataTemplate` and USB card markup to include student detail rows. Add inline sync status label inside the statistics footer (`Grid.Row="3"`). Net effect: ~836 → ~600 lines (file shrinks by ~230 lines). |
| `SmartLog.Scanner.Tests/ViewModels/CameraSlotStateTests.cs` | New (file does not exist today) — tests for `LastGradeSection` formatting, field population on flash, clear on reset. |
| `SmartLog.Scanner.Tests/ViewModels/UsbScannerSlotStateTests.cs` | Existing file (created under US0123) — extend with new cases for the same fields. |

**Out of scope (deferred):**
- `MainViewModel` integration tests — `MainViewModel` lives in MAUI project, unreachable from tests (CLAUDE.md constraint). Flash-population correctness covered by Phase 5 manual verification.
- `App.xaml` `InvertedBoolConverter` cleanup — only delete if a `grep` after the XAML change shows zero callers. Safer to leave it.
- Animation polish for card transitions (per US0124 Out of Scope).

---

## Implementation Phases

### Phase 1 — ViewModel field additions

Pure additive change. Both files get the same set of fields. Build green after this phase, no behaviour change yet (XAML doesn't bind the new fields until Phase 2).

**File:** `SmartLog.Scanner/ViewModels/CameraSlotState.cs`

Add inside the existing class (after the `_lastScanMessage` field, before the `_frameRateDisplay` field):

```csharp
// ── Student detail fields (populated during ShowFlash, cleared on reset) ──

/// <summary>Student number / ID (e.g., "STU12345"). Visitor scans show "Visitor Pass #N" via FlashStudentName instead.</summary>
[ObservableProperty] private string? _lastStudentId;

/// <summary>Learner Reference Number (12-digit DepEd ID).</summary>
[ObservableProperty] private string? _lastLrn;

/// <summary>Grade level (e.g., "Grade 11").</summary>
[ObservableProperty] private string? _lastGrade;

/// <summary>Section (e.g., "Section A").</summary>
[ObservableProperty] private string? _lastSection;

/// <summary>Program / strand (e.g., "STEM"). Optional — omitted from LastGradeSection when null/empty.</summary>
[ObservableProperty] private string? _lastProgram;

/// <summary>Local-time scan timestamp (HH:mm:ss) for the card footer.</summary>
[ObservableProperty] private string? _lastScanTime;

/// <summary>True when the last scan was a visitor pass — drives header label and hides student-only rows.</summary>
[ObservableProperty] private bool _isVisitorScan;

/// <summary>Composed "Grade · Program · Section" string. Program omitted when null/empty.</summary>
public string? LastGradeSection
{
    get
    {
        if (string.IsNullOrEmpty(LastGrade) && string.IsNullOrEmpty(LastSection))
            return null;
        var grade = LastGrade ?? string.Empty;
        var section = LastSection ?? string.Empty;
        return string.IsNullOrEmpty(LastProgram)
            ? $"{grade} · {section}".Trim(' ', '·')
            : $"{grade} · {LastProgram} · {section}".Trim(' ', '·');
    }
}

partial void OnLastGradeChanged(string? value) => OnPropertyChanged(nameof(LastGradeSection));
partial void OnLastSectionChanged(string? value) => OnPropertyChanged(nameof(LastGradeSection));
partial void OnLastProgramChanged(string? value) => OnPropertyChanged(nameof(LastGradeSection));
```

**File:** `SmartLog.Scanner.Core/ViewModels/UsbScannerSlotState.cs`

Identical additions, same placement (after `_isHealthWarning`).

**Verification:** `dotnet build SmartLog.Scanner.Core -c Release && dotnet build SmartLog.Scanner -f net8.0-maccatalyst` — both green, zero new warnings.

### Phase 2 — XAML rewrite

The largest mechanical change. Done in one pass to avoid intermediate broken states.

**File:** `SmartLog.Scanner/Views/MainPage.xaml`

**2.a — Delete the right column (central student card):**

Remove the entire `<Grid Grid.Column="1">` block (lines 360–586). The outer body grid changes from `ColumnDefinitions="Auto,*"` to a single column.

**2.b — Body grid restructure:**

Change body row from:

```xml
<Grid Grid.Row="1" ColumnDefinitions="Auto,*" BackgroundColor="#F8F9FA">
    <Grid Grid.Column="0" ...>  <!-- left column with preview + slots + USB -->
    <Grid Grid.Column="1">      <!-- central card (DELETED) -->
</Grid>
```

To:

```xml
<Grid Grid.Row="1" RowDefinitions="Auto,*" BackgroundColor="#F8F9FA" Padding="12,8">

    <!-- Camera 0 live preview (visible only in camera mode) -->
    <Border Grid.Row="0"
            IsVisible="{Binding IsCameraMode}"
            HeightRequest="240"
            HorizontalOptions="Center"
            WidthRequest="320"
            BackgroundColor="Black"
            Stroke="#2C5F5D"
            StrokeThickness="2"
            Margin="0,0,0,8">
        <Border.StrokeShape>
            <RoundRectangle CornerRadius="10" />
        </Border.StrokeShape>
        <Border.Shadow>
            <Shadow Brush="Black" Opacity="0.15" Radius="10" Offset="0,3" />
        </Border.Shadow>
        <controls:CameraPreviewView x:Name="CameraPreview0" />
    </Border>

    <!-- Scan station cards (camera slots + USB) — adaptive flex layout -->
    <ScrollView Grid.Row="1" VerticalScrollBarVisibility="Default">
        <FlexLayout Direction="Row"
                    Wrap="Wrap"
                    JustifyContent="Center"
                    AlignItems="Start">

            <!-- Camera slot cards (1–8, only IsVisible=true ones render visibly) -->
            <BindableLayout.ItemsSource>
                <Binding Path="CameraSlots" />
            </BindableLayout.ItemsSource>
            <BindableLayout.ItemTemplate>
                <DataTemplate x:DataType="vm:CameraSlotState">
                    <!-- ... full station-card template (see 2.c below) ... -->
                </DataTemplate>
            </BindableLayout.ItemTemplate>

            <!-- USB card appended (Note: FlexLayout + BindableLayout.ItemsSource doesn't append
                 a non-bound child directly — use a wrapping VerticalStackLayout or replace the
                 BindableLayout with an explicit children list. Implementation: wrap inside
                 a Grid with the FlexLayout in row 0 and the USB card in row 1, both inside
                 the ScrollView's VerticalStackLayout — same structure US0123 used.) -->
        </FlexLayout>
    </ScrollView>

</Grid>
```

**Note on FlexLayout + appended USB card:** the existing pattern in `MainPage.xaml` lines 197–354 wraps `<FlexLayout BindableLayout.ItemsSource="...">` and the USB `<Border>` inside a `<VerticalStackLayout>` so both render together inside the `ScrollView`. We keep that wrapping pattern. The result: camera cards wrap in a row at the top of the stack layout, USB card appears as a single peer below them. With 3 cameras + 1 USB, FlexLayout fits all three camera cards in row 1 and the USB card below — visually a 3+1 layout. With 1 camera + USB it's 1+1 stacked. Acceptable for AC5.

**Alternative considered:** put the USB card inside the same FlexLayout flow by exposing it as a synthetic `CameraSlotState`-like item in `CameraSlots`. Rejected — couples USB-specific state into the camera collection and breaks the `vm:CameraSlotState` typing on the camera template. Stick with the wrapper pattern.

**2.c — Card sizing strategy: each card fills `1 ÷ N` of body width where N = active card count**

Plan: expose a computed `MainViewModel.CardWidth` (double) bound by every card's `WidthRequest`. The property recalculates when:

- `CameraSlots` count of `IsVisible == true` items changes (during `InitializeAsync` and any future setup-driven reconfig)
- `IsUsbMode` becomes true/false
- A `BodyWidth` property — bound to the body Grid's `Width` via `SizeChanged` event handler in code-behind — changes

Implementation:

```csharp
// In MainViewModel, near the camera slot state:
[ObservableProperty] private double _bodyWidth;

public double CardWidth
{
    get
    {
        var visibleCameras = CameraSlots.Count(s => s.IsVisible);
        var totalActive = visibleCameras + (IsUsbMode ? 1 : 0);
        if (totalActive == 0 || BodyWidth <= 0) return 320; // fallback default
        const double interCardSpacing = 12; // matches Margin="6" on each side
        var available = BodyWidth - (interCardSpacing * (totalActive + 1));
        var perCard = available / totalActive;
        return Math.Max(260, perCard); // clamp to MinimumWidth — FlexLayout wraps if pinched
    }
}

partial void OnBodyWidthChanged(double value) => OnPropertyChanged(nameof(CardWidth));
```

In `MainPage.xaml.cs`, hook the body grid's `SizeChanged`:

```csharp
private void OnBodyGridSizeChanged(object? sender, EventArgs e)
{
    if (sender is VisualElement v && _viewModel != null)
        _viewModel.BodyWidth = v.Width;
}
```

XAML on the body Grid: `SizeChanged="OnBodyGridSizeChanged"` plus `x:Name="BodyGrid"`.

Each station card binds `WidthRequest="{Binding Source={x:Reference page}, Path=BindingContext.CardWidth}"` (or via the `MainPage` BindingContext root reference). Cleaner alternative: since `CameraSlotState` doesn't know about `MainViewModel`, the `WidthRequest` binding goes against the page-level `BindingContext` (which IS `MainViewModel`). Use `{Binding Source={RelativeSource AncestorType={x:Type ContentPage}}, Path=BindingContext.CardWidth}` — verbose but standard MAUI.

**Simpler alternative considered and rejected:** `FlexLayout.Basis="0"` + `FlexLayout.Grow="1"` on each card to fill width equally. Reason for rejection: when the FlexLayout wraps (active count > 4 on a tight display), `Grow="1"` makes the wrapped row's lone card take 100 % width — visually inconsistent with the other rows. Explicit `WidthRequest` keeps every card the same size whether on row 1 or row 2.

**Expanded camera station card template:**

Replace the current camera card `DataTemplate` (lines 204–280) with the expanded version including a per-card bottom banner row (per AC8 refinement — each card carries its own prominent coloured result strip):

```xml
<DataTemplate x:DataType="vm:CameraSlotState">
    <Border IsVisible="{Binding IsVisible}"
            WidthRequest="{Binding Source={RelativeSource AncestorType={x:Type ContentPage}}, Path=BindingContext.CardWidth}"
            MinimumWidthRequest="260"
            Margin="6"
            BackgroundColor="White"
            Stroke="{Binding DisplayBrush}"
            StrokeThickness="2"
            Padding="0">
        <Border.StrokeShape>
            <RoundRectangle CornerRadius="12" />
        </Border.StrokeShape>
        <Border.Shadow>
            <Shadow Brush="Black" Opacity="0.08" Radius="10" Offset="0,3" />
        </Border.Shadow>

        <Grid RowDefinitions="Auto,*,Auto" RowSpacing="0">

            <!-- HEADER: Camera name + scan type badge + result icon -->
            <Grid Grid.Row="0"
                  ColumnDefinitions="Auto,*,Auto"
                  Padding="16,12,16,8"
                  ColumnSpacing="8">
                <Label Grid.Column="0"
                       Text="{Binding FlashIcon}"
                       FontSize="18"
                       FontAttributes="Bold"
                       TextColor="{Binding FlashColor}"
                       VerticalOptions="Center"
                       IsVisible="{Binding ShowFlash}" />
                <Label Grid.Column="1"
                       Text="{Binding DisplayName}"
                       FontSize="15"
                       FontAttributes="Bold"
                       TextColor="#2C5F5D"
                       VerticalOptions="Center"
                       LineBreakMode="TailTruncation" />
                <Border Grid.Column="2"
                        BackgroundColor="{Binding ScanTypeBadgeColor}"
                        Padding="8,3"
                        StrokeThickness="0"
                        VerticalOptions="Center">
                    <Border.StrokeShape>
                        <RoundRectangle CornerRadius="5" />
                    </Border.StrokeShape>
                    <Label Text="{Binding ScanType}"
                           FontSize="11"
                           FontAttributes="Bold"
                           TextColor="White" />
                </Border>
            </Grid>

            <!-- BODY: Student detail block (during flash) OR idle status (no flash) -->
            <Grid Grid.Row="1"
                  Padding="16,4,16,16"
                  RowDefinitions="Auto,Auto,Auto,Auto"
                  RowSpacing="6">

                <!-- Idle: status text only -->
                <Label Grid.Row="0"
                       Text="{Binding StatusText}"
                       FontSize="14"
                       TextColor="#666666"
                       VerticalOptions="Center"
                       IsVisible="{Binding ShowFlash, Converter={StaticResource InvertedBoolConverter}}" />

                <!-- Flash: student number / visitor pass header (large) -->
                <Label Grid.Row="0"
                       Text="{Binding FlashStudentName}"
                       FontSize="24"
                       FontAttributes="Bold"
                       TextColor="{Binding FlashColor}"
                       LineBreakMode="TailTruncation"
                       IsVisible="{Binding ShowFlash}" />

                <!-- Flash + non-visitor: LRN row -->
                <Grid Grid.Row="1"
                      ColumnDefinitions="Auto,*"
                      ColumnSpacing="10"
                      IsVisible="{Binding ShowFlash}">
                    <Label Grid.Column="0"
                           Text="LRN"
                           FontSize="10"
                           FontAttributes="Bold"
                           TextColor="#9E9E9E"
                           CharacterSpacing="1.5"
                           VerticalOptions="Center" />
                    <Label Grid.Column="1"
                           Text="{Binding LastLrn, TargetNullValue='N/A'}"
                           FontSize="15"
                           TextColor="#546E7A"
                           VerticalOptions="Center"
                           LineBreakMode="TailTruncation" />
                </Grid>

                <!-- Flash + non-visitor: Grade · Program · Section -->
                <Label Grid.Row="2"
                       Text="{Binding LastGradeSection}"
                       FontSize="15"
                       TextColor="#37474F"
                       LineBreakMode="TailTruncation"
                       IsVisible="{Binding ShowFlash}" />

                <!-- Spacer so banner sits at card bottom -->
                <BoxView Grid.Row="3" Color="Transparent" HeightRequest="0" />
            </Grid>

            <!-- BOTTOM BANNER (per-card replacement for the deleted shared banner) -->
            <Border Grid.Row="2"
                    BackgroundColor="{Binding FlashColor}"
                    StrokeThickness="0"
                    Padding="14,10"
                    IsVisible="{Binding ShowFlash}">
                <Border.StrokeShape>
                    <RoundRectangle CornerRadius="0,0,11,11" />
                </Border.StrokeShape>
                <Grid ColumnDefinitions="Auto,*,Auto" ColumnSpacing="10">
                    <Label Grid.Column="0"
                           Text="{Binding FlashIcon}"
                           FontSize="20"
                           TextColor="White"
                           VerticalOptions="Center" />
                    <Label Grid.Column="1"
                           Text="{Binding LastScanMessage}"
                           FontSize="13"
                           FontAttributes="Bold"
                           TextColor="White"
                           LineBreakMode="WordWrap"
                           MaxLines="2"
                           VerticalOptions="Center" />
                    <Label Grid.Column="2"
                           Text="{Binding LastScanTime}"
                           FontSize="11"
                           TextColor="White"
                           Opacity="0.9"
                           VerticalOptions="Center" />
                </Grid>
            </Border>
        </Grid>
    </Border>
</DataTemplate>
```

The card now has three vertical regions: header (always visible), body (idle status OR student details), bottom banner (visible only during flash, coloured by `FlashColor`). The bottom banner replaces the deleted shared bottom banner — every operator gets the same prominent colour-coded long-distance cue on their own card.

**2.d — Expanded USB card markup:**

The USB card already exists (lines 285–352) and uses `Grid RowDefinitions="Auto,Auto,Auto,Auto"`. Expand to the same 6-row structure as the camera template (with the indigo `Path` barcode glyph kept in the header row 0 instead of the `FlashIcon` label). All bindings prefixed with `UsbScannerSlot.`.

**2.e — Delete bottom feedback banner:**

Remove the entire `Grid.Row="2"` content (both `<Border>` blocks — `ShowFeedback`-true and the "Ready" pulse, lines 591–699). The body now flows directly into the statistics footer.

**2.f — Inline sync status label in statistics footer:**

Inside the statistics footer (`Grid.Row="3"`, lines 702–833), add a thin label between the queue/today/sync card row and the bottom edge — bound to a new `MainViewModel.SyncStatusMessage` property (added in Phase 3). Visible when `SyncStatusMessage != null`. Auto-clears via the existing `_centralCardCts` rebranded as `_syncStatusCts` (or replace it entirely).

```xml
<!-- Sync status inline label (replaces the deleted bottom feedback banner for sync messages) -->
<Label Text="{Binding SyncStatusMessage}"
       FontSize="12"
       TextColor="White"
       Opacity="0.85"
       HorizontalOptions="Center"
       Margin="0,8,0,0"
       IsVisible="{Binding SyncStatusMessage, Converter={StaticResource StringNotEmptyConverter}}" />
```

**Note:** if `StringNotEmptyConverter` doesn't exist, either add it (one-liner converter) or bind visibility to a boolean `HasSyncMessage` that mirrors `!string.IsNullOrEmpty(SyncStatusMessage)`. Phase 3 picks one.

**2.g — Sanity grep before declaring Phase 2 done:**

Run `grep -n 'studentCard\|HasScannedStudent\|LastStudentName\|LastStudentId\|LastLrn\|LastGrade\|LastSection\|LastProgram\|CardBorderColor\|FeedbackColor\|ShowFeedback' SmartLog.Scanner/Views/MainPage.xaml` — expect zero matches. Anything left flags a binding that needs to be either deleted or rebound to a slot property.

### Phase 3 — `MainViewModel` simplification

**File:** `SmartLog.Scanner/ViewModels/MainViewModel.cs`

**3.a — Delete central-card properties (lines 40–78):**

Remove:

```csharp
[ObservableProperty] private string? _lastStudentId;
[ObservableProperty] private string? _lastScanTime;
[ObservableProperty] private bool _lastScanValid;
[ObservableProperty] private string? _lastScanMessage;
[ObservableProperty] private string? _lastLrn;
[ObservableProperty] private string? _lastStudentName;
[ObservableProperty] private string? _lastGrade;
[ObservableProperty] private string? _lastSection;
[ObservableProperty] private string? _lastProgram;
[ObservableProperty] private bool _hasScannedStudent;
[ObservableProperty] private Color _cardBorderColor = Color.FromArgb("#E0E0E0");

public string? LastGradeSection { get { ... } }   // and the 3 OnXxxChanged cascades

[ObservableProperty] private Color _feedbackColor = Colors.Transparent;
[ObservableProperty] private bool _showFeedback;

private DateTimeOffset? _currentOptimisticScanAt;
private CancellationTokenSource? _centralCardCts;
```

**Keep:**

- `[ObservableProperty] private string _statusMessage` and `_statusIcon` — top status bar (AC10)
- `[ObservableProperty] private string? _lastScanCameraName` — used by the optional "last scan from" indicator (verify usage; if unused, also delete)

**Add (Phase 2.f wiring):**

```csharp
/// <summary>
/// Inline sync / queue status message shown in the statistics footer.
/// Replaces the deleted bottom feedback banner for ManualSync, ClearQueue, and OnSyncCompleted messages.
/// </summary>
[ObservableProperty] private string? _syncStatusMessage;

private CancellationTokenSource? _syncStatusCts;
```

**3.b — Simplify `OnScanCompleted` (lines 543–739):**

Replace the entire ~200-line method body with:

```csharp
private void OnScanCompleted(object? sender, ScanResult result)
{
    // For optimistic results, defer history/stats to OnScanUpdated (when server confirms).
    if (!result.IsOptimistic)
        _ = LogScanToHistoryAsync(result);

    // Per-station card flashing — USB-sourced scans go to the USB slot card.
    // Camera-sourced scans are flashed by OnMultiCameraScanCompleted (separate event handler).
    if (result.Source == ScanSource.UsbScanner && IsUsbMode)
        MainThread.BeginInvokeOnMainThread(() => TriggerUsbSlotFlash(result));

    // Audio + statistics. Audio waits for server confirmation (OnScanUpdated) for
    // optimistic Accepted, so duplicates don't fire a false success beep.
    if (!result.IsOptimistic)
    {
        _ = _soundService.PlayResultSoundAsync(result.Status);
        _ = UpdateStatisticsAsync(result.Status);
    }
}
```

The 7-case switch (Accepted / Duplicate / Rejected / Queued / Error / RateLimited / DebouncedLocally) goes away — each station card now displays the result via its own `LastScanStatus` / `FlashColor` / `FlashIcon` bindings.

**3.c — Simplify `OnScanUpdated` (lines 744–805):**

Replace with:

```csharp
private void OnScanUpdated(object? sender, ScanResult result)
{
    _ = LogScanToHistoryAsync(result);

    MainThread.BeginInvokeOnMainThread(() =>
    {
        _ = UpdateStatisticsAsync(result.Status);

        // Optimistic-→-confirmed correction sound: if the server downgraded an
        // Accepted optimistic result to Duplicate / Rejected / Error, play the corrected sound.
        if (result.Status != ScanStatus.Accepted)
            _ = _soundService.PlayResultSoundAsync(result.Status);

        // The per-slot card re-paint happens via OnMultiCameraScanUpdated → FlashSourceSlot
        // (camera path) or — for USB updates — TriggerUsbSlotFlash (called above for all USB scans).
    });
}
```

The `if (!ShowFeedback || _currentOptimisticScanAt != result.ScannedAt) return;` early-exit goes away (no central card to gate). The 4-case switch goes away.

**3.d — Refactor `TriggerSlotFlash` to take `ScanResult` and populate full detail fields:**

Current signature: `TriggerSlotFlash(int cameraIndex, ScanStatus status, string? subjectName, string? message)` — called from `FlashSourceSlot`.

New signature: `TriggerSlotFlash(int cameraIndex, ScanResult result)`. Inline `FlashSourceSlot` into `OnMultiCameraScanCompleted` and `OnMultiCameraScanUpdated` call sites (each line becomes `TriggerSlotFlash(e.CameraIndex, e.Result);`).

```csharp
private void TriggerSlotFlash(int cameraIndex, ScanResult result)
{
    if (cameraIndex < 0 || cameraIndex >= CameraSlots.Count) return;

    if (_flashTimers.TryGetValue(cameraIndex, out var existing))
    {
        existing.Cancel();
        existing.Dispose();
    }

    var cts = new CancellationTokenSource();
    _flashTimers[cameraIndex] = cts;

    if (cameraIndex < _cameraGated.Length)
        _cameraGated[cameraIndex] = true;

    var slot = CameraSlots[cameraIndex];
    var subjectName = result.IsVisitorScan
        ? $"Visitor Pass #{result.PassNumber}"
        : result.StudentName ?? result.StudentId ?? string.Empty;

    slot.LastScanStatus = result.Status;
    slot.LastScanMessage = ToFriendlyMessage(result);
    slot.FlashStudentName = subjectName;
    slot.LastStudentId = result.StudentId;
    slot.LastLrn = result.IsVisitorScan ? null : result.Lrn;
    slot.LastGrade = result.IsVisitorScan ? null : result.Grade;
    slot.LastSection = result.IsVisitorScan ? null : result.Section;
    slot.LastProgram = result.IsVisitorScan ? null : result.Program;
    slot.LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
    slot.IsVisitorScan = result.IsVisitorScan;
    slot.ShowFlash = true;

    _ = Task.Delay(1000, cts.Token).ContinueWith(t =>
    {
        if (t.IsCanceled) return;

        if (cameraIndex < _cameraGated.Length)
            _cameraGated[cameraIndex] = false;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            slot.ShowFlash = false;
            slot.FlashStudentName = null;
            slot.LastScanMessage = null;
            slot.LastScanStatus = null;
            slot.LastStudentId = null;
            slot.LastLrn = null;
            slot.LastGrade = null;
            slot.LastSection = null;
            slot.LastProgram = null;
            slot.LastScanTime = null;
            slot.IsVisitorScan = false;
        });
    });
}
```

**3.e — Refactor `TriggerUsbSlotFlash` to populate full detail fields:**

Same shape as 3.d but on `UsbScannerSlot` instead of an indexed slot. Already takes `ScanResult` — just expand to populate the new fields.

**3.f — Sync status routing:**

`ManualSync`, `ClearQueue`, `OnSyncCompleted`, `TestValidQr` no-secret path each currently set `LastScanMessage` + `FeedbackColor` + `ShowFeedback` + start a `Task.Delay(...)`. Replace with:

```csharp
SetSyncStatus("✓ Synced 12 scans"); // example
```

```csharp
private void SetSyncStatus(string message, int autoclearMs = 3000)
{
    SyncStatusMessage = message;
    _syncStatusCts?.Cancel();
    _syncStatusCts?.Dispose();
    var cts = _syncStatusCts = new CancellationTokenSource();
    _ = Task.Delay(autoclearMs, cts.Token).ContinueWith(t =>
    {
        if (t.IsCanceled) return;
        MainThread.BeginInvokeOnMainThread(() => SyncStatusMessage = null);
    });
}
```

`DisposeAsync` cleans up `_syncStatusCts`.

**3.g — Wire `IsVisible` subscription so `CardWidth` recomputes when slot visibility flips:**

`CardWidth` reads `CameraSlots.Count(s => s.IsVisible)` but the cascade (`OnBodyWidthChanged` → `OnPropertyChanged(nameof(CardWidth))`) only fires when `BodyWidth` changes. If a camera slot toggles `IsVisible` post-init (e.g., a delayed enumeration result, hot-add, or recovery flow flipping a slot back online), `CardWidth` becomes stale and cards render at the wrong width.

In `InitializeAsync`, after `ApplyCameraConfigsToSlots(...)`:

```csharp
// US0124: subscribe to IsVisible flips so CardWidth recomputes when a slot's visibility toggles.
foreach (var slot in CameraSlots)
{
    slot.PropertyChanged += OnCameraSlotPropertyChanged;
}
```

Handler:

```csharp
private void OnCameraSlotPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
{
    if (e.PropertyName == nameof(CameraSlotState.IsVisible))
        OnPropertyChanged(nameof(CardWidth));
}
```

In `DisposeAsync`, unsubscribe to avoid lingering handlers (singleton VM lifetime makes this mostly cosmetic, but keep symmetric):

```csharp
foreach (var slot in CameraSlots)
{
    slot.PropertyChanged -= OnCameraSlotPropertyChanged;
}
```

**3.h — Final grep:**

`grep -n 'LastStudentId\|HasScannedStudent\|LastStudentName\|LastGradeSection\|CardBorderColor\|FeedbackColor\|ShowFeedback\|_currentOptimisticScanAt\|_centralCardCts' SmartLog.Scanner/ViewModels/MainViewModel.cs` — expect zero matches.

### Phase 4 — Tests

**File:** `SmartLog.Scanner.Tests/ViewModels/CameraSlotStateTests.cs` (new)

```csharp
using SmartLog.Scanner.Core.Models;
using SmartLog.Scanner.ViewModels;
using Xunit;

namespace SmartLog.Scanner.Tests.ViewModels;

public class CameraSlotStateTests
{
    [Fact]
    public void LastGradeSection_With_All_Three_Fields_Joins_With_Middle_Dot()
    {
        var slot = new CameraSlotState(0);
        slot.LastGrade = "Grade 11";
        slot.LastProgram = "STEM";
        slot.LastSection = "A";
        Assert.Equal("Grade 11 · STEM · A", slot.LastGradeSection);
    }

    [Fact]
    public void LastGradeSection_Without_Program_Omits_Middle_Segment()
    {
        var slot = new CameraSlotState(0);
        slot.LastGrade = "Grade 11";
        slot.LastSection = "A";
        // LastProgram null
        Assert.Equal("Grade 11 · A", slot.LastGradeSection);
    }

    [Fact]
    public void LastGradeSection_Returns_Null_When_Both_Grade_And_Section_Empty()
    {
        var slot = new CameraSlotState(0);
        Assert.Null(slot.LastGradeSection);
    }

    [Fact]
    public void Setting_Student_Detail_Fields_Raises_Property_Changed_For_LastGradeSection()
    {
        var slot = new CameraSlotState(0);
        var raised = new List<string>();
        slot.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        slot.LastGrade = "Grade 12";
        slot.LastProgram = "ABM";
        slot.LastSection = "B";

        Assert.Contains(nameof(CameraSlotState.LastGradeSection), raised);
        Assert.Equal(3, raised.Count(n => n == nameof(CameraSlotState.LastGradeSection)));
    }

    [Fact]
    public void IsVisitorScan_Defaults_False()
    {
        var slot = new CameraSlotState(0);
        Assert.False(slot.IsVisitorScan);
    }
}
```

**File:** `SmartLog.Scanner.Tests/ViewModels/UsbScannerSlotStateTests.cs` (extend existing)

Add the same `LastGradeSection_*` tests against `UsbScannerSlotState` (the shape is identical — copy-paste with type swap is fine; the duplication is cheap and the formatter logic is independent per ViewModel).

### Phase 5 — Manual verification

**5.a — macOS dev build:** `dotnet run --project SmartLog.Scanner -f net8.0-maccatalyst`

1. **`Camera`-only mode, 1 camera configured:**
   - Body shows camera 0 preview at top (full width within body), 1 wide station card below
   - Card idle state shows camera name + ENTRY badge + "● Ready to Scan" + neutral green border
   - Trigger a scan via `TestValidQr` → card flashes green for 1 s with full student details (number, LRN, grade · section, time)
   - Card returns to idle cleanly
2. **`Camera`-only mode, 3 cameras configured (use setup wizard to add 3 cameras):**
   - 3 station cards laid out in a row (or wrapped to 2+1 on narrow displays — verify the wrap)
   - Cards are visually equivalent in size; widths are similar
   - Trigger scans on multiple cameras in quick succession (use `TestValidQr` repeatedly — it round-robins?) — verify each card flashes independently with its own student data; no cross-contamination
3. **`USB`-only mode:**
   - No camera preview visible
   - USB card shown alone (or with the placeholder camera cards hidden — verify `IsVisible="false"` slots are not laid out at all)
   - Trigger USB scan via `TestValidQr` → USB card flashes with full details, indigo accent during idle
4. **`Both` mode, 2 cameras + USB:**
   - Camera preview + 2 camera cards + USB card visible
   - Camera scan flashes camera card; USB card stays idle
   - USB scan flashes USB card; camera cards stay idle
   - Cross-source dedup: scan via webcam, then via USB within 3 s — first scan flashes its source card with Accepted, second scan flashes its source card with `Status = DebouncedLocally` (amber)
5. **Visitor pass scan:**
   - Card flashes blue border, header shows "Visitor Pass #5", LRN and grade rows hidden
6. **Rejected / Error scan:**
   - Card flashes red border, name row hidden (only icon + message visible per Q3 default)
7. **Per-camera gate:** hold a QR in front of camera 1 — camera 1 flashes once, then card remains idle until 1-second flash ends (no double-flash). Other camera cards are unaffected.
8. **Sync messages:** trigger ManualSync → message appears in the statistics footer for ~3 s, then auto-clears. Verify it does NOT cover the body station cards.
9. **Top status bar regression:** verify date/time, scan-type toggle, navigation buttons, and "Scanning Status" text all still work.

**5.b — Windows hardware verification (separate session):**

Run on a Windows scanner PC (per CLAUDE.md, XAML changes force Windows-host build):

1. Build: `dotnet publish SmartLog.Scanner -f net8.0-windows10.0.19041.0 -c Release -o ./publish/win-x64` on a Windows machine.
2. Repeat 5.a steps 1, 2, 4, 5 on Windows. Pay attention to:
   - Card density at common Windows resolutions (1366×768, 1920×1080)
   - Border colour transitions on `Border.Stroke` during flash (some WinUI versions flicker — note in Risks)
   - Live preview placement under camera mode
3. Real concurrent test: 2 webcams + USB scanner. Have one operator scan via webcam while another scans via USB. Verify cards flash independently.
4. Check log: `ScanLogsPage` — both scan types appear with correct `ScanMethod` ("Camera" / "USB").

---

## Risks & Considerations

- **Layout density on 1366×768 laptops.** Three full-detail station cards in a row need ~960 px of horizontal space (320 × 3 + margins). On a 1366 × 768 display with side chrome, the cards may compress below 320 px MinimumWidth and force text truncation. Mitigation: set `MinimumWidthRequest="320"` and rely on FlexLayout `Wrap="Wrap"` to spill to a second row. Verify in Phase 5.b step 2.
- **Bottom banner removal regret risk.** The big coloured banner is currently visible from across a room. Per-card borders are smaller. If UAT reports operators can't tell at a glance whether a scan worked, reverse: keep a slim 30-px banner showing only the most recent scan's status colour (no text). Not in scope for PL0022 but easy to add later.
- **`StringNotEmptyConverter` may not exist.** Phase 2.f assumes a converter to bind `IsVisible` to "is this string non-empty". If absent, either add it (3-line `IValueConverter`) or use a `bool` mirror property `HasSyncMessage`. Decided in Phase 3.
- **`InvertedBoolConverter` reuse audit.** The new station card template uses `InvertedBoolConverter` for several rows. The converter is registered in `App.xaml` already. After deleting the central card (which was the heaviest user), grep to confirm it's still needed; usually yes, but worth a 30-second check.
- **Sound double-play after refactor.** Current `OnScanCompleted` plays sound inside the switch statement (Accepted / Duplicate / etc. each call `PlayResultSoundAsync`). After refactor, sound plays once unconditionally for non-optimistic scans, plus once in `OnScanUpdated` for status-corrected results. Verify no double-beep on a normal Accepted server-confirmed flow. If yes, gate `OnScanUpdated`'s sound on a status-changed check.
- **Camera 0 preview attachment timing.** The preview's platform handler is attached in `MainPage.xaml.cs.AttachCameraPreview()` after `InitializeAsync`. Moving the `<Border>` inside the body grid changes its visual tree position but the `x:Name="CameraPreview0"` still resolves to the same instance. Should work without code-behind changes — verify in Phase 5.a step 1.
- **Optimistic Accepted flicker.** Today's flow: optimistic Accepted fires `OnMultiCameraScanCompleted` (slot flashes green with student data from local cache, no LRN/grade), then server `ScanUpdated` re-fires `OnMultiCameraScanUpdated` (slot re-flashes with full details). With the new layout the user may see a 100-200 ms window where LRN/grade rows are blank then populate. Acceptable — same behaviour exists today on the central card. If jarring, add a `WidthRequest` on the LRN/grade labels so layout doesn't shift when text fills in.
- **`LastScanCameraName` deletion candidate.** Used today to show "Last scan from: Camera 2" somewhere. If it's only used by markup we're deleting, remove it. Verify with a grep.
- **`ToFriendlyMessage` location.** Lives in `MainViewModel`. Both `TriggerSlotFlash` and `TriggerUsbSlotFlash` call it — both still on `MainViewModel`, so no relocation needed. Stays as-is.
- **Cross-build limitation:** PL0022 modifies XAML — cross-build from macOS will fail on the XAML compile step (per CLAUDE.md). Defer Windows build to Phase 5.b.

---

## Out of Scope

- Embedding camera preview per station card (per US0124 Out of Scope).
- Animated transitions, dark mode, drag-rearranging cards.
- Schema or scan log changes — UI only.
- Tablet / phone form factor — desktop-only as today.

---

## Estimated Effort

| Phase | Time |
|-------|------|
| 1 — ViewModel field additions (`CameraSlotState` + `UsbScannerSlotState`) | ~30 min |
| 2 — XAML rewrite (delete right column, delete bottom banner, expand station card template, expand USB card, relocate preview, inline sync label) | ~2 h |
| 3 — `MainViewModel` simplification (delete properties, gut `OnScanCompleted` / `OnScanUpdated`, refactor `TriggerSlotFlash` / `TriggerUsbSlotFlash`, sync status routing) | ~1.5 h |
| 4 — Tests (`CameraSlotStateTests` new, `UsbScannerSlotStateTests` extension) | ~45 min |
| 5.a — macOS dev manual verification (1, 2, 3 cameras × Camera/USB/Both modes) | ~1 h |
| 5.b — Windows hardware verification (separate session) | ~1 h |
| **Total** | **~7 hours** |

5-point story; aligns with one focused day.

---

## Rollout Plan

1. **Branch:** continue on `dev`. No new feature branch — small enough scope.
2. Phase 1 — ViewModel additions; build green, tests still green.
3. Phase 2 — XAML rewrite in one commit (intermediate states are broken). Build green; visual smoke test on macOS.
4. Phase 3 — `MainViewModel` simplification in one commit. Build + tests green.
5. Phase 4 — extend tests; full suite green (`dotnet test`).
6. Phase 5.a — macOS verification of all layout permutations.
7. **Pause for user review** before Windows hardware verification.
8. Phase 5.b — Windows verification on the target scanner PC.
9. Update US0124 and PL0022 to `Done`; update `_index.md` files; commit.
10. Push to `dev`. PR to `main` when EP0012 is fully ready (or land separately if ahead of schedule).

---

## Open Questions

All resolved 2026-04-28 (MarkMarmeto):

- **Q1 (preview placement):** fixed top of body. → AC7 + Phase 2.b.
- **Q2 (null student name):** fall back to `LastStudentId`. → Phase 3.d (`subjectName` fallback chain).
- **Q3 (Rejected/Error rows):** hide student-only rows; show only icon + message. → card template (Phase 2.c) gates non-visitor detail rows via `IsVisible` bindings; the fallback flow naturally hides empty rows for `Rejected` / `Error` / `RateLimited` because `LastStudentId` / `LastLrn` / etc. are not populated for those statuses.
- **Q4 (bottom banner):** removed at the page level, **reborn per-card** as a coloured banner row at the bottom of every station card. → AC8 + Phase 2.c (bottom banner row) + Phase 2.e (delete the page-level banner).
- **Q5 (sync messages):** inline label below statistics card row, auto-clear via `_syncStatusCts`. → Phase 2.f + Phase 3.f.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-28 | Claude (Sonnet 4.6) + MarkMarmeto | Initial plan drafted; verified central-card markup at lines 360–586, bottom banner at 591–699, `_centralCardCts` call sites in `MainViewModel`. Estimated 7 h. Defaults proposed for all five open questions; pending user review before Phase 1. |
| 2026-04-28 | MarkMarmeto | All Q1–Q5 defaults approved. Two refinements that updated Phase 2.c: (1) **adaptive width strategy** — each card binds `WidthRequest` to a new `MainViewModel.CardWidth` computed property that divides body width by the active card count (1+USB → 50/50; 2+USB → 33×3; 3+USB → 25×4); body width fed via `SizeChanged` event handler in code-behind; clamped to 260 px minimum so the FlexLayout wraps gracefully on narrow displays. (2) **per-card bottom banner** — the deleted shared bottom banner is reborn as a coloured banner row at the bottom of every station card (icon + message + time on `FlashColor` background, white text), preserving the long-distance "did it work?" cue per lane. Plan ready for Phase 1 execution. |
| 2026-04-28 | Claude (Sonnet 4.6) | Phase 1 executed: ViewModel field additions on `CameraSlotState` + `UsbScannerSlotState` + `MainViewModel.CardWidth` computed prop. Build green, 217/217 tests pass. Plan amendments during review: (a) renamed `StationCardWidth` → `CardWidth` per user; (b) added Phase 3.g — subscribe to each `CameraSlot.PropertyChanged` in `InitializeAsync` so `CardWidth` recomputes when a slot's `IsVisible` flips post-init (delayed enumeration / hot-add / recovery). |
