# PL0024: ID-Card-Style Station Cards

> **Status:** Draft
> **Story:** [US0126: ID-Card-Style Station Cards (Avatar Top, Device Strip Bottom)](../stories/US0126-id-card-style-station-cards.md)
> **Epic:** [EP0012: Concurrent Multi-Modal Scanning](../epics/EP0012-concurrent-multi-modal-scanning.md)
> **Created:** 2026-04-28
> **Language:** C# 12 / .NET 8 MAUI (Windows + macOS)
> **Drafted by:** Claude (Sonnet 4.6) + MarkMarmeto

---

## Overview

Rebuild the station card `DataTemplate` in `MainPage.xaml` from the current "header on top + bottom banner during flash" layout (US0124) to an ID-card style: avatar at top, student details (skeleton when idle, real on flash) in the middle, device identity strip at the bottom. Per-card ENTRY/EXIT badge is removed. The global ENTRY MODE pill in the top bar stays as the only scan-type display.

`CameraSlotState` and `UsbScannerSlotState` get two new computed properties (`BottomStripStatusText`, `BottomStripColor`) so the new bottom strip can bind cleanly. No existing slot fields change. `MainViewModel` is **not touched** — the same `TriggerSlotFlash`/`TriggerUsbSlotFlash` methods populate the same slot fields the new template reads.

This is functionally a XAML revamp + two small computed-property additions.

---

## Acceptance Criteria Mapping

| AC (US0126) | Phase |
|-------------|-------|
| AC1: Avatar at top | Phase 2 (XAML — stacked skeleton + active avatar `Border`s) |
| AC2: Student detail block always visible (skeleton when idle) | Phase 2 (XAML — paired skeleton + real Label rows gated by `ShowFlash` / `InvertedBoolConverter`) |
| AC3: Device identity strip at the bottom | Phase 1 (`BottomStripColor` + `BottomStripStatusText`) + Phase 2 (XAML strip) |
| AC4: Per-card scan-type badge removed | Phase 2 (XAML — header row deleted) |
| AC5: Bottom strip status reverts on flash end | Phase 1 (`BottomStripStatusText` returns to "Ready to Scan" when `!ShowFlash`) |
| AC6: Card sizing — proportional with 1080p clamp | Phase 2 (`MaximumWidthRequest="480"` on the card `Border`) |
| AC7: Camera preview unchanged | No change |
| AC8: Statistics footer unchanged | No change |
| AC9: Top status bar unchanged | No change |
| AC10: Per-camera gate + flash timer behaviour unchanged | No change (`MainViewModel` not touched) |
| AC11: ScanResult field wiring unchanged | No change |

---

## Technical Context (Verified)

### Confirmed via code read (HEAD `4c3919c`)

- **`CameraSlotState.cs`** (194 lines after US0124) — already exposes `LastStudentId`, `LastLrn`, `LastGrade`, `LastSection`, `LastProgram`, `LastScanTime`, `IsVisitorScan`, `LastScanStatus`, `LastScanMessage`, `FlashStudentName`, `ShowFlash`, `DisplayName`, computed `LastGradeSection`, `FlashColor`, `FlashIcon`, `StatusText` (which still returns "● Ready to Scan" — we'll repurpose its meaning slightly). **Missing for AC3:** `BottomStripStatusText` and `BottomStripColor` — adding both.
- **`UsbScannerSlotState.cs`** (Core, 159 lines after US0124) — same shape. Same additions needed. The default device colour is `#6A4C93` (indigo) — already encoded in the existing `DisplayColor` getter.
- **`MainPage.xaml`** (US0124 version, ~510 lines) — station card `DataTemplate` lives at lines ~169–262 (camera) and the USB card markup at lines ~284–420. Both have:
  - Outer `Border` with `WidthRequest="{Binding ... CardWidth}"`, `Stroke="{Binding DisplayBrush}"` (camera) / `Stroke="{Binding UsbScannerSlot.DisplayColor}"` (USB).
  - Header row with `FlashIcon` + `DisplayName` + `ScanType` badge (lines ~187–210 camera; ~301–328 USB).
  - Body block with idle status + flash student details (rows 0–2 of an inner Grid).
  - Bottom banner row gated by `ShowFlash` (lines ~232–262 camera; ~393–420 USB).
- **No `MainViewModel` changes required** — `TriggerSlotFlash` (lines 380–435) and `TriggerUsbSlotFlash` (lines 438–485) already populate every field the new template reads.
- **`InvertedBoolConverter`** is registered in `App.xaml` and already used elsewhere in `MainPage.xaml` — no new converter to register.

### Files to touch

| File | Change |
|------|--------|
| `SmartLog.Scanner/ViewModels/CameraSlotState.cs` | Add `BottomStripStatusText` (computed) + `BottomStripColor` (computed) + cascades for `OnShowFlashChanged`, `OnLastScanMessageChanged`, `OnLastScanStatusChanged`. ~25 lines added. |
| `SmartLog.Scanner.Core/ViewModels/UsbScannerSlotState.cs` | Same additions. ~25 lines. Default device colour is indigo (`#6A4C93`) instead of green. |
| `SmartLog.Scanner/Views/MainPage.xaml` | Rebuild camera card `DataTemplate` and USB card markup. Old structure (header + flash body + flash banner) replaced with new structure (avatar + student detail block with skeleton + bottom device strip). Net effect: ~80 lines added per card template, ~70 deleted; XAML grows by ~20 lines net. |
| `SmartLog.Scanner.Tests/ViewModels/UsbScannerSlotStateTests.cs` | Add tests for `BottomStripStatusText` (idle vs. flash transitions) and `BottomStripColor` (default indigo, flash colour for each `ScanStatus`). ~6 new tests. |

**Out of scope (deferred):**
- `CameraSlotStateTests.cs` — `CameraSlotState` lives in MAUI project, unreachable from tests (CLAUDE.md constraint). The shared computed-property pattern is validated via `UsbScannerSlotStateTests`.
- `MainViewModel` integration — unchanged.

---

## Implementation Phases

### Phase 1 — ViewModel computed properties

Pure additive change. Both ViewModels gain the same two computed properties. Build green after Phase 1; XAML still uses the old template until Phase 2 lands.

**File:** `SmartLog.Scanner/ViewModels/CameraSlotState.cs`

Add inside the existing class (after `DisplayBrush` getter, near the other computed properties):

```csharp
// US0126: Bottom strip text — device name on line 1, this on line 2.
// Idle: "Ready to Scan". Flashing: the friendly scan message ("✓ Juan Cruz — Accepted" etc.)
public string BottomStripStatusText => ShowFlash
    ? (LastScanMessage ?? "Scan complete")
    : "Ready to Scan";

// US0126: Bottom strip background colour. Default green (camera identity).
// During a flash, shifts to the result colour.
public Color BottomStripColor => ShowFlash
    ? FlashColor
    : Color.FromArgb("#4CAF50");
```

Update existing cascade methods to also notify the new properties:

```csharp
partial void OnShowFlashChanged(bool value)
{
    OnPropertyChanged(nameof(DisplayBrush));
    OnPropertyChanged(nameof(BottomStripStatusText));
    OnPropertyChanged(nameof(BottomStripColor));
}

partial void OnLastScanMessageChanged(string? value) =>
    OnPropertyChanged(nameof(BottomStripStatusText));

// (existing OnLastScanStatusChanged already raises FlashColor — extend to also raise BottomStripColor)
partial void OnLastScanStatusChanged(ScanStatus? value)
{
    OnPropertyChanged(nameof(FlashColor));
    OnPropertyChanged(nameof(FlashBrush));
    OnPropertyChanged(nameof(FlashIcon));
    OnPropertyChanged(nameof(DisplayBrush));
    OnPropertyChanged(nameof(BottomStripColor));
}
```

**File:** `SmartLog.Scanner.Core/ViewModels/UsbScannerSlotState.cs`

Same additions, but the default colour is indigo:

```csharp
public string BottomStripStatusText => ShowFlash
    ? (LastScanMessage ?? "Scan complete")
    : (IsHealthWarning ? "⚠ No recent scans (1m+)" : "Ready to Scan");

public Color BottomStripColor => ShowFlash
    ? FlashColor
    : (IsHealthWarning ? Color.FromArgb("#FF9800") : Color.FromArgb("#6A4C93"));
```

Note the USB version respects the existing 60 s no-scan health warning (`IsHealthWarning`) — when warning fires, the bottom strip flips to amber and shows the locked wording. Cascade `OnIsHealthWarningChanged` to also raise the new properties:

```csharp
partial void OnIsHealthWarningChanged(bool value)
{
    OnPropertyChanged(nameof(StatusText));
    OnPropertyChanged(nameof(DisplayColor));
    OnPropertyChanged(nameof(BottomStripStatusText));
    OnPropertyChanged(nameof(BottomStripColor));
}
```

**Verification:** `dotnet build SmartLog.Scanner` clean; `dotnet test` 225/225 still passing.

### Phase 2 — XAML card rebuild

**File:** `SmartLog.Scanner/Views/MainPage.xaml`

Replace the camera station card `DataTemplate` (current lines ~169–262) with the new ID-card layout:

```xml
<DataTemplate x:DataType="vm:CameraSlotState">
    <Border IsVisible="{Binding IsVisible}"
            WidthRequest="{Binding Source={RelativeSource AncestorType={x:Type ContentPage}}, Path=BindingContext.CardWidth}"
            MinimumWidthRequest="260"
            MaximumWidthRequest="520"
            Margin="6"
            BackgroundColor="White"
            Stroke="{Binding DisplayBrush}"
            StrokeThickness="2"
            Padding="0">
        <Border.StrokeShape>
            <RoundRectangle CornerRadius="14" />
        </Border.StrokeShape>
        <Border.Shadow>
            <Shadow Brush="Black" Opacity="0.08" Radius="10" Offset="0,3" />
        </Border.Shadow>

        <Grid RowDefinitions="Auto,*,Auto" RowSpacing="0">

            <!-- ROW 0: Avatar (always visible, two-state skeleton/active) -->
            <Grid Grid.Row="0"
                  HorizontalOptions="Center"
                  Padding="0,18,0,12">
                <!-- Skeleton avatar (idle) -->
                <Border WidthRequest="80"
                        HeightRequest="80"
                        BackgroundColor="#EEEEEE"
                        Stroke="#D0D0D0"
                        StrokeThickness="2"
                        IsVisible="{Binding ShowFlash, Converter={StaticResource InvertedBoolConverter}}">
                    <Border.StrokeShape>
                        <RoundRectangle CornerRadius="40" />
                    </Border.StrokeShape>
                    <Label Text="👤"
                           FontSize="36"
                           Opacity="0.3"
                           HorizontalOptions="Center"
                           VerticalOptions="Center" />
                </Border>
                <!-- Active avatar (during flash) -->
                <Border WidthRequest="80"
                        HeightRequest="80"
                        BackgroundColor="#E0F2F1"
                        Stroke="#4D9B91"
                        StrokeThickness="2"
                        IsVisible="{Binding ShowFlash}">
                    <Border.StrokeShape>
                        <RoundRectangle CornerRadius="40" />
                    </Border.StrokeShape>
                    <Label Text="👤"
                           FontSize="36"
                           Opacity="0.85"
                           HorizontalOptions="Center"
                           VerticalOptions="Center" />
                </Border>
            </Grid>

            <!-- ROW 1: Student detail block — 4 labelled rows matching the previously deleted
                 central card design. Skeleton when idle, real values during flash. -->
            <VerticalStackLayout Grid.Row="1"
                                 Padding="20,4,20,14"
                                 Spacing="10">

                <!-- Student Number — hidden for visitor scans (visitors have no student ID) -->
                <VerticalStackLayout Spacing="3"
                                     IsVisible="{Binding IsVisitorScan, Converter={StaticResource InvertedBoolConverter}}">
                    <Label Text="STUDENT NUMBER"
                           FontSize="9"
                           FontAttributes="Bold"
                           TextColor="#9E9E9E"
                           CharacterSpacing="1.2" />
                    <Border IsVisible="{Binding ShowFlash, Converter={StaticResource InvertedBoolConverter}}"
                            BackgroundColor="#EEEEEE"
                            HeightRequest="22"
                            StrokeThickness="0">
                        <Border.StrokeShape>
                            <RoundRectangle CornerRadius="4" />
                        </Border.StrokeShape>
                    </Border>
                    <Label IsVisible="{Binding ShowFlash}"
                           Text="{Binding LastStudentId}"
                           FontSize="18"
                           FontAttributes="Bold"
                           TextColor="#2C5F5D"
                           LineBreakMode="TailTruncation" />
                </VerticalStackLayout>

                <!-- Student Name — visible for both student and visitor scans (carries "Visitor Pass #N" for visitors) -->
                <VerticalStackLayout Spacing="3">
                    <Label Text="STUDENT NAME"
                           FontSize="9"
                           FontAttributes="Bold"
                           TextColor="#9E9E9E"
                           CharacterSpacing="1.2" />
                    <Border IsVisible="{Binding ShowFlash, Converter={StaticResource InvertedBoolConverter}}"
                            BackgroundColor="#EEEEEE"
                            HeightRequest="20"
                            StrokeThickness="0">
                        <Border.StrokeShape>
                            <RoundRectangle CornerRadius="4" />
                        </Border.StrokeShape>
                    </Border>
                    <Label IsVisible="{Binding ShowFlash}"
                           Text="{Binding FlashStudentName}"
                           FontSize="15"
                           TextColor="#37474F"
                           LineBreakMode="TailTruncation" />
                </VerticalStackLayout>

                <!-- LRN — hidden during visitor flash -->
                <VerticalStackLayout Spacing="3"
                                     IsVisible="{Binding IsVisitorScan, Converter={StaticResource InvertedBoolConverter}}">
                    <Label Text="LRN"
                           FontSize="9"
                           FontAttributes="Bold"
                           TextColor="#9E9E9E"
                           CharacterSpacing="1.2" />
                    <Border IsVisible="{Binding ShowFlash, Converter={StaticResource InvertedBoolConverter}}"
                            BackgroundColor="#EEEEEE"
                            HeightRequest="18"
                            WidthRequest="140"
                            HorizontalOptions="Start"
                            StrokeThickness="0">
                        <Border.StrokeShape>
                            <RoundRectangle CornerRadius="4" />
                        </Border.StrokeShape>
                    </Border>
                    <Label IsVisible="{Binding ShowFlash}"
                           Text="{Binding LastLrn, TargetNullValue='N/A'}"
                           FontSize="14"
                           TextColor="#546E7A"
                           LineBreakMode="TailTruncation" />
                </VerticalStackLayout>

                <!-- Grade · Program · Section — hidden during visitor flash. Program is included
                     inline via the existing LastGradeSection computed: "Grade 11 · STEM · A". -->
                <VerticalStackLayout Spacing="3"
                                     IsVisible="{Binding IsVisitorScan, Converter={StaticResource InvertedBoolConverter}}">
                    <Label Text="GRADE · PROGRAM · SECTION"
                           FontSize="9"
                           FontAttributes="Bold"
                           TextColor="#9E9E9E"
                           CharacterSpacing="1.2" />
                    <Border IsVisible="{Binding ShowFlash, Converter={StaticResource InvertedBoolConverter}}"
                            BackgroundColor="#EEEEEE"
                            HeightRequest="18"
                            WidthRequest="180"
                            HorizontalOptions="Start"
                            StrokeThickness="0">
                        <Border.StrokeShape>
                            <RoundRectangle CornerRadius="4" />
                        </Border.StrokeShape>
                    </Border>
                    <Label IsVisible="{Binding ShowFlash}"
                           Text="{Binding LastGradeSection}"
                           FontSize="14"
                           TextColor="#37474F"
                           LineBreakMode="TailTruncation" />
                </VerticalStackLayout>

            </VerticalStackLayout>

            <!-- ROW 2: Device strip (always visible — name on line 1, status on line 2) -->
            <Border Grid.Row="2"
                    BackgroundColor="{Binding BottomStripColor}"
                    StrokeThickness="0"
                    Padding="14,10">
                <Border.StrokeShape>
                    <RoundRectangle CornerRadius="0,0,12,12" />
                </Border.StrokeShape>
                <VerticalStackLayout Spacing="2" HorizontalOptions="Center">
                    <Label Text="{Binding DisplayName}"
                           FontSize="13"
                           FontAttributes="Bold"
                           TextColor="White"
                           HorizontalTextAlignment="Center"
                           LineBreakMode="TailTruncation" />
                    <Label Text="{Binding BottomStripStatusText}"
                           FontSize="11"
                           TextColor="White"
                           Opacity="0.92"
                           HorizontalTextAlignment="Center"
                           LineBreakMode="TailTruncation" />
                </VerticalStackLayout>
            </Border>
        </Grid>
    </Border>
</DataTemplate>
```

**USB card markup** uses the same structure. Differences:
- Outer `Border.Stroke` binds to `UsbScannerSlot.DisplayColor` (existing — already shifts indigo → flash result colour).
- Bottom strip carries the **vector barcode glyph** (US0123 vector `Path`) inside the device strip, left of the "USB Scanner" label, as a small decorator (16 × 12 px, white fill on the indigo background). This preserves the visual identity from US0123 in the new layout.
- Inside the avatar block, the 👤 glyph is the same as camera cards (per Q2 — generic only).
- All bindings prefix with `UsbScannerSlot.`.

USB bottom strip layout sketch:

```xml
<Border Grid.Row="2"
        BackgroundColor="{Binding UsbScannerSlot.BottomStripColor}"
        StrokeThickness="0"
        Padding="14,10">
    <Border.StrokeShape>
        <RoundRectangle CornerRadius="0,0,12,12" />
    </Border.StrokeShape>
    <Grid ColumnDefinitions="Auto,*" ColumnSpacing="8">
        <Path Grid.Column="0"
              Data="M0,0 L2,0 L2,12 L0,12 Z M4,0 L5,0 L5,12 L4,12 Z M7,0 L9,0 L9,12 L7,12 Z M11,0 L12,0 L12,12 L11,12 Z M14,0 L16,0 L16,12 L14,12 Z"
              Fill="White"
              WidthRequest="16"
              HeightRequest="12"
              VerticalOptions="Center" />
        <VerticalStackLayout Grid.Column="1" Spacing="2">
            <Label Text="{Binding UsbScannerSlot.DisplayName}"
                   FontSize="13"
                   FontAttributes="Bold"
                   TextColor="White"
                   HorizontalTextAlignment="Start"
                   LineBreakMode="TailTruncation" />
            <Label Text="{Binding UsbScannerSlot.BottomStripStatusText}"
                   FontSize="11"
                   TextColor="White"
                   Opacity="0.92"
                   HorizontalTextAlignment="Start"
                   LineBreakMode="TailTruncation" />
        </VerticalStackLayout>
    </Grid>
</Border>
```

**Phase 2.b — Outer Border colour shift retained:**

The outer card `Border.Stroke` continues to bind to `DisplayBrush` (camera) / `UsbScannerSlot.DisplayColor` (USB) and shifts colour during a flash — same behaviour as today's US0124 cards. The bottom strip ALSO carries colour signalling, so during a flash both the outer border and the bottom strip pulse the result colour together. Idle state: green outer border + green bottom strip (camera); indigo outer border + indigo bottom strip (USB).

`DisplayBrush` and `DisplayColor` remain in active use — they were *not* deleted from the ViewModels in any earlier story, and this story keeps them bound.

### Phase 3 — Tests

**File:** `SmartLog.Scanner.Tests/ViewModels/UsbScannerSlotStateTests.cs`

Append:

```csharp
// ── US0126: Bottom strip computed properties ─────────────────────────────

[Fact]
public void BottomStripStatusText_When_Idle_Reads_Ready_To_Scan()
{
    var slot = new UsbScannerSlotState();
    slot.StartListening();
    Assert.Equal("Ready to Scan", slot.BottomStripStatusText);
}

[Fact]
public void BottomStripStatusText_When_Flash_Reads_LastScanMessage()
{
    var slot = new UsbScannerSlotState
    {
        LastScanMessage = "✓ Juan Cruz — Accepted",
        ShowFlash = true
    };
    Assert.Equal("✓ Juan Cruz — Accepted", slot.BottomStripStatusText);
}

[Fact]
public void BottomStripStatusText_When_Health_Warning_Reads_Locked_Wording()
{
    var slot = new UsbScannerSlotState();
    slot.StartListening();
    slot.IsHealthWarning = true;
    Assert.Equal("⚠ No recent scans (1m+)", slot.BottomStripStatusText);
}

[Fact]
public void BottomStripColor_When_Idle_Is_Indigo()
{
    var slot = new UsbScannerSlotState();
    slot.StartListening();
    Assert.Equal(Color.FromArgb("#6A4C93"), slot.BottomStripColor);
}

[Fact]
public void BottomStripColor_When_Health_Warning_Is_Amber()
{
    var slot = new UsbScannerSlotState();
    slot.StartListening();
    slot.IsHealthWarning = true;
    Assert.Equal(Color.FromArgb("#FF9800"), slot.BottomStripColor);
}

[Fact]
public void BottomStripColor_When_Flash_Accepted_Is_Green()
{
    var slot = new UsbScannerSlotState
    {
        LastScanStatus = ScanStatus.Accepted,
        ShowFlash = true
    };
    Assert.Equal(Color.FromArgb("#4CAF50"), slot.BottomStripColor);
}

[Fact]
public void BottomStripColor_Reverts_To_Indigo_When_ShowFlash_Returns_False()
{
    var slot = new UsbScannerSlotState();
    slot.StartListening();
    slot.LastScanStatus = ScanStatus.Accepted;
    slot.ShowFlash = true;
    Assert.Equal(Color.FromArgb("#4CAF50"), slot.BottomStripColor);

    slot.ShowFlash = false;
    Assert.Equal(Color.FromArgb("#6A4C93"), slot.BottomStripColor);
}
```

7 new tests. `CameraSlotStateTests` cannot be added (project unreachable) — covered by manual verification of the matching pattern in Phase 5.

### Phase 4 — Manual verification (macOS)

Build: `dotnet run --project SmartLog.Scanner -f net8.0-maccatalyst`.

1. **1 camera, no USB:** single card centred, avatar visible, skeleton lines, bottom strip green with "Cam 1" / "Ready to Scan". `MaximumWidthRequest="480"` clamps the card; doesn't stretch full screen.
2. **2 cameras + USB:** 3 cards equal width. Two with green strips, one with purple strip.
3. **3 cameras + USB:** 4 cards equal width on a 1920×1080 display. All visible; cards remain readable (>=260 px wide).
4. **Trigger a scan via `TestValidQr`:**
   - Avatar fills with the active state (teal background instead of grey).
   - Student detail rows fill with real data (number, LRN, grade · section).
   - Bottom strip shifts to green / amber / red based on result; status text changes to the result message.
   - After 1 s, card resets — avatar back to skeleton, detail rows back to skeletons, bottom strip back to default device colour with "Ready to Scan".
5. **Visitor scan path:** during flash, the card shows "Visitor Pass #N" in the student-number slot; LRN and Grade · Section rows are hidden; bottom strip is blue.
6. **Per-card ENTRY/EXIT badge fully gone** — no badge on any card. The top bar's ENTRY MODE pill is the only place scan type is displayed.
7. **Top bar, statistics footer, navigation buttons** all unchanged.
8. **USB no-scan health warning** (let the app sit > 60 s in `Both` mode without a USB scan) — USB card's bottom strip flips to amber with "⚠ No recent scans (1m+)" wording. Recovers on the next USB scan.
9. **Resize the window** between 1366 × 768, 1280 × 800, and 1920 × 1080 — cards maintain proportional widths, never compress below `MinimumWidthRequest="240"`.

### Phase 5 — Windows hardware verification (separate session)

Build on Windows: `dotnet publish SmartLog.Scanner -f net8.0-windows10.0.19041.0 -c Release -o ./publish/win-x64`.

- Repeat Phase 4 steps 1–8 at 1366 × 768 and 1920 × 1080.
- Confirm avatar 👤 glyph renders identically to macOS.
- Confirm bottom strip rounded corner (`CornerRadius="0,0,13,13"`) renders cleanly on WinUI Composition.
- Real concurrent scans (camera + USB) → verify cards flash independently and reset cleanly.

---

## Risks & Considerations

- **Skeleton-to-real swap flicker.** Mitigated by giving skeleton `Border` and real `Label` identical reserved heights so the layout slot stays the same size when visibility flips.
- **Layout recalc when `BottomStripColor` changes.** Setting only `BackgroundColor` doesn't trigger a measure pass — should be cheap. Verified during Phase 4 — should not see flicker on 60 fps capture.
- **Avatar size 80 px on small windows.** Acceptable; if a 4-card row on 1280 × 800 forces cards to shrink toward `MinimumWidthRequest="240"`, the avatar keeps its 80 px but card height grows slightly — fine.
- **`DisplayBrush` / `DisplayColor` orphaned.** Plan keeps them as harmless dead code. If a future cleanup story removes them, the bindings are already gone in this story so removal is safe.
- **The barcode glyph from US0123** (vector `Path` in the USB card header) goes away with the header. Acceptable per the wireframe (no glyph drawn) — the purple bottom strip is the USB identity now. If UAT misses the glyph, easy add: small barcode decorator inside the USB avatar block.
- **Cross-build limitation:** XAML changes mean Windows build runs on a Windows host (per CLAUDE.md). Phase 4 macOS only; Phase 5 Windows.

---

## Out of Scope

- Server-side student photo loading.
- Animated avatar transitions / crossfades.
- Per-card ENTRY/EXIT toggle re-introduction.
- Layouts beyond 4 active cards (existing flex-wrap behaviour from US0124 handles 5–9 cards).

---

## Estimated Effort

| Phase | Time |
|-------|------|
| 1 — `BottomStripStatusText` + `BottomStripColor` on both ViewModels + cascades | ~30 min |
| 2 — Camera card + USB card `DataTemplate` rebuild in `MainPage.xaml` | ~2 h |
| 3 — Tests (7 new, `UsbScannerSlotStateTests`) | ~30 min |
| 4 — macOS verification | ~45 min |
| 5 — Windows hardware verification (separate session) | ~30 min |
| **Total** | **~4.25 hours** |

3 points; one focused half-day plus a Windows session.

---

## Rollout Plan

1. Continue on `dev`.
2. Phase 1 — ViewModel additions; build clean, all 225 tests still green.
3. Phase 2 — XAML rebuild in one commit (intermediate states are visually broken). Build clean.
4. Phase 3 — extend tests; full suite green.
5. Phase 4 — macOS verification.
6. **Pause for user review** before Windows hardware verification.
7. Phase 5 — Windows hardware.
8. Update US0126 + PL0024 status to Done. Update indexes. Commit + push.

---

## Open Questions

All resolved 2026-04-28 (MarkMarmeto):

- **Q1 (idle skeleton):** skeleton placeholders. → Phase 2 paired skeleton+label rows.
- **Q2 (avatar):** generic 👤 glyph, two-state (idle skeleton vs. active flash). → Phase 2 stacked skeleton+active `Border`s.
- **Q3 (per-card badge):** removed. → Phase 2 deletes header row entirely.
- **Q4 (bottom strip flash):** colour pulses + result message; reverts to default + "Ready to Scan" after 1 s. → Phase 1 `BottomStripColor` + `BottomStripStatusText` getters.
- **Q5 (statistics footer):** kept. → No XAML change to footer.
- **Q6 (sizing):** proportional `CardWidth` retained, `MaximumWidthRequest="480"` for 1080p readability. → Phase 2 outer `Border` attribute.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-28 | Claude (Sonnet 4.6) + MarkMarmeto | Initial plan drafted; verified slot ViewModels expose all needed fields, MainPage.xaml structure unchanged from US0124 baseline. ~4.25 h estimate. All Q1–Q6 resolved. Ready for Phase 1 execution. |
| 2026-04-28 | MarkMarmeto | Review feedback — six adjustments to Phase 2: (1) `MaximumWidthRequest=520`; (2) avatar 80 px (unchanged); (3) outer `Border.Stroke` keeps `DisplayBrush`/`DisplayColor` binding — colour-shifts on flash, identical to today's US0124; (4) USB barcode glyph kept, placed inside the bottom strip left of "USB Scanner" text (white fill, 16×12 px); (5) `DisplayBrush`/`DisplayColor` stay actively bound (no longer dead code as planned); (6) detail block now 4 labelled rows: STUDENT NUMBER, STUDENT NAME, LRN, GRADE · PROGRAM · SECTION — matching the previously deleted central card. STUDENT NUMBER + LRN + GRADE rows gated by `IsVisitorScan`/`InvertedBoolConverter` so visitor flashes only show STUDENT NAME (with "Visitor Pass #N"). All XAML examples in Phase 2 updated. |
