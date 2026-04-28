# PL0021: USB Scanner Indicator Slot with Health Heuristic

> **Status:** Completed
> **Story:** [US0123: USB Scanner Indicator Slot with Health Heuristic](../stories/US0123-usb-scanner-indicator-slot.md)
> **Epic:** [EP0012: Concurrent Multi-Modal Scanning](../epics/EP0012-concurrent-multi-modal-scanning.md)
> **Created:** 2026-04-28
> **Language:** C# 12 / .NET 8 MAUI (Windows + macOS)
> **Drafted by:** Claude (Opus 4.7)

---

## Overview

Add a USB scanner indicator card to the bottom of the left column on `MainPage`, sibling to the camera FlexLayout. The card uses a new `UsbScannerSlotState` ViewModel (peer to `CameraSlotState` but with USB-specific concerns) and a vector-rendered barcode glyph (not an emoji). It mirrors the camera scan-flash mechanism for visual feedback and adds a 60-second no-scan health heuristic that flips the card to an amber warning state ("⚠ No recent scans (1m+)") if the USB scanner has been silent for too long.

The card is gated by `IsUsbMode` from PL0019 — visible in `USB`-only and `Both` modes, hidden in `Camera`-only.

This plan depends on **PL0019** (the `IsUsbMode` helper, `ScanResult.Source` field, and the `OnScanCompleted` event subscription that drives the USB pipeline).

---

## Acceptance Criteria Mapping

| AC (US0123) | Phase |
|-------------|-------|
| AC1: `UsbScannerSlotState` ViewModel | Phase 1 |
| AC2: Card visible only in USB-active modes | Phase 1 — `IsVisible` bound to `IsUsbMode` |
| AC3: Listening state with vector barcode glyph | Phase 1 (state) + Phase 2 (XAML) |
| AC4: Scan flash mirrors camera behavior | Phase 3 — MainViewModel routes USB scans into slot flash |
| AC5: 60s no-scan warning with locked wording | Phase 3 — Tick logic |
| AC6: Warning auto-clears on next scan | Phase 3 — flash handler resets `LastScanAt` and clears warning |
| AC7: Scan log parity | No code change here — PL0019 already handles attribution |
| AC8: First-run "Listening" before warning | Phase 3 — Tick uses `_startedAt` fallback when `LastScanAt` null |
| AC9: Card hidden when USB mode off | Phase 1 + 2 — `IsVisible` binding |
| AC10: No restart button | Phase 2 — XAML omits restart row |
| AC11: Card placement below camera FlexLayout | Phase 2 — `Grid.Row` after FlexLayout in left column ScrollView |

---

## Technical Context (Verified)

### Confirmed via code read

- `CameraSlotState.cs` (`SmartLog.Scanner/ViewModels/`) is the reference template — uses `[ObservableProperty]`, computed display properties (`StatusText`, `FlashColor`, `FlashIcon`, `DisplayBrush`), `partial void OnXxxChanged` for cascading notifications, plus a per-card flash timer pattern via `CancellationTokenSource` in `MainViewModel`.
- `MainViewModel._frameRateTimer` (line ~113) — already a 1-second `IDispatcherTimer` running while in camera mode. We extend its tick handler to also call `UsbScannerSlot.Tick()` when `IsUsbMode`.
- `MainViewModel._flashTimers` (line ~110) — `Dictionary<int, CancellationTokenSource>` for camera card flashes. We add a parallel single `_usbFlashCts` field for the USB card.
- `MainPage.xaml` left column structure (lines 167-293):
  - Outer `Grid Grid.Column="0"` with `RowDefinitions="Auto,*"`
  - Row 0 = camera 0 live preview (`Border` containing `CameraPreviewView`)
  - Row 1 = `ScrollView` containing camera FlexLayout
  - **Insertion point for USB card: after the FlexLayout, still inside the ScrollView.** The ScrollView already accepts a single content child, so we wrap FlexLayout + USB card in a `VerticalStackLayout`.
- `Microsoft.Maui.Controls.Shapes.Path` is available via `<Path>` element (no extra `xmlns` needed in MAUI XAML).
- Color palette in use elsewhere: `#4D9B91` (teal accent), `#F44336` (red error), `#FF9800` (amber warning), `#4CAF50` (green success), `#2196F3` (blue visitor). USB indigo accent is new — `#6A4C93`.

### Files to touch

| File | Change |
|------|--------|
| `SmartLog.Scanner.Core/ViewModels/UsbScannerSlotState.cs` | NEW — placed in Core (not MAUI project) so the test project can reference it. Uses `Color` properties instead of `Brush`/`SolidColorBrush` (Core does not reference `Microsoft.Maui.Controls`); MAUI's XAML binding auto-converts `Color → Brush` when binding to `Border.Stroke`. |
| `SmartLog.Scanner/ViewModels/MainViewModel.cs` | Add `UsbScannerSlot` property, `TriggerUsbSlotFlash`, extend existing `_frameRateTimer` tick (timer creation gate already broadened to `IsCameraMode || IsUsbMode` by PL0019), route `OnScanCompleted` USB path to slot flash |
| `SmartLog.Scanner/Views/MainPage.xaml` | Wrap FlexLayout + USB card `Border` in `VerticalStackLayout` inside ScrollView. Bindings use `Color` properties on the new ViewModel; `Border.Stroke` accepts `Color` directly via implicit conversion. |
| `SmartLog.Scanner.Tests/ViewModels/UsbScannerSlotStateTests.cs` | NEW — warning state transitions, flash logic, computed properties. Reachable because `UsbScannerSlotState` lives in Core and Core has `[InternalsVisibleTo("SmartLog.Scanner.Tests")]`. |

**Out of scope (deferred / unreachable):**
- `MainViewModelTests.cs` extension — `MainViewModel` lives in MAUI project and is unreachable from tests (same constraint as PL0019). USB flash routing covered by Phase 5 manual verification.

---

## Implementation Phases

### Phase 1 — `UsbScannerSlotState` ViewModel (placed in Core)

**File:** `SmartLog.Scanner.Core/ViewModels/UsbScannerSlotState.cs` (new — placed in **Core**, not the MAUI project)

**Why Core:** the test project (`SmartLog.Scanner.Tests`) only references `SmartLog.Scanner.Core` — it cannot see types in the MAUI project. Putting `UsbScannerSlotState` in Core makes it reachable for unit tests of the warning state machine (`Tick()`, flash transitions). This requires using `Color` properties instead of `Brush`/`SolidColorBrush` because Core does not reference `Microsoft.Maui.Controls` (only `Microsoft.Maui.Essentials`). MAUI's XAML binding accepts `Color` for `Border.Stroke` via implicit conversion — verified by existing `CameraSlotState` patterns and MAUI documentation.

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Graphics;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.ViewModels;

/// <summary>
/// EP0012 (US0123): Observable state for the USB scanner indicator card on MainPage.
/// Peer to CameraSlotState but with USB-specific health semantics:
/// - No frame rate (event-driven, not polled)
/// - No restart button (HID device — nothing to restart)
/// - 60s no-scan warning heuristic
/// - Indigo accent color (#6A4C93) to differentiate from camera teal/red
///
/// Lives in Core (not the MAUI project) so the test project can reference it.
/// Uses Color properties instead of Brush/SolidColorBrush — MAUI's XAML binding
/// auto-converts Color → Brush for Border.Stroke, Background, etc.
/// </summary>
public partial class UsbScannerSlotState : ObservableObject
{
    private const int WarningThresholdSeconds = 60;
    private DateTime _startedAtUtc = DateTime.UtcNow;

    [ObservableProperty] private string _displayName = "USB Scanner";
    [ObservableProperty] private string _scanType = "ENTRY";
    [ObservableProperty] private bool _isListening;
    [ObservableProperty] private DateTimeOffset? _lastScanAt;
    [ObservableProperty] private bool _showFlash;
    [ObservableProperty] private string? _flashStudentName;
    [ObservableProperty] private ScanStatus? _lastScanStatus;
    [ObservableProperty] private string? _lastScanMessage;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isHealthWarning;

    /// <summary>Locked wording per US0123 AC5.</summary>
    public string StatusText => IsHealthWarning
        ? "⚠ No recent scans (1m+)"
        : (IsListening ? "● Listening" : "○ Idle");

    /// <summary>Indigo accent (#6A4C93) for normal listening, amber for warning, flash color during flash.</summary>
    public Color DisplayColor => ShowFlash
        ? FlashColor
        : (IsHealthWarning
            ? Color.FromArgb("#FF9800")
            : Color.FromArgb("#6A4C93"));

    /// <summary>Color of the scan-type badge (teal = ENTRY, red = EXIT) — same palette as camera cards.</summary>
    public Color ScanTypeBadgeColor => ScanType == "EXIT"
        ? Color.FromArgb("#F44336")
        : Color.FromArgb("#4D9B91");

    /// <summary>Flash color follows the scan result status — same palette as CameraSlotState.</summary>
    public Color FlashColor => LastScanStatus switch
    {
        ScanStatus.Accepted         => Color.FromArgb("#4CAF50"),
        ScanStatus.Duplicate        => Color.FromArgb("#FF9800"),
        ScanStatus.DebouncedLocally => Color.FromArgb("#FF9800"),
        ScanStatus.RateLimited      => Color.FromArgb("#FF9800"),
        ScanStatus.Queued           => Color.FromArgb("#4D9B91"),
        ScanStatus.Rejected         => Color.FromArgb("#F44336"),
        ScanStatus.Error            => Color.FromArgb("#F44336"),
        _                           => Color.FromArgb("#4CAF50")
    };

    public string FlashIcon => LastScanStatus switch
    {
        ScanStatus.Accepted         => "✓",
        ScanStatus.Duplicate        => "⚠",
        ScanStatus.DebouncedLocally => "⚠",
        ScanStatus.RateLimited      => "⏱",
        ScanStatus.Queued           => "📥",
        ScanStatus.Rejected         => "✗",
        ScanStatus.Error            => "✗",
        _                           => string.Empty
    };

    /// <summary>
    /// Called by the 1s timer in MainViewModel. Flips IsHealthWarning to true once
    /// no scan has been received for WarningThresholdSeconds (per AC5 + AC8).
    /// </summary>
    public void Tick()
    {
        if (!IsListening) return;
        var referenceTime = LastScanAt?.UtcDateTime ?? _startedAtUtc;
        var ageSeconds = (DateTime.UtcNow - referenceTime).TotalSeconds;
        IsHealthWarning = ageSeconds > WarningThresholdSeconds;
    }

    /// <summary>Called by MainViewModel when the pipeline starts (resets the first-run timer).</summary>
    public void StartListening()
    {
        _startedAtUtc = DateTime.UtcNow;
        IsListening = true;
        IsHealthWarning = false;
        LastScanAt = null;
    }

    /// <summary>Called by MainViewModel when the pipeline stops.</summary>
    public void StopListening()
    {
        IsListening = false;
        IsHealthWarning = false;
        ShowFlash = false;
    }

    /// <summary>
    /// Test-only seam: overrides _startedAtUtc and LastScanAt to simulate elapsed time
    /// without sleeping. Reachable from SmartLog.Scanner.Tests via [InternalsVisibleTo].
    /// </summary>
    internal void SetReferenceTimeForTest(DateTime utcReferenceTime, DateTimeOffset? lastScanAt = null)
    {
        _startedAtUtc = utcReferenceTime;
        LastScanAt = lastScanAt;
    }

    // ── Property-changed cascades ──

    partial void OnShowFlashChanged(bool value) =>
        OnPropertyChanged(nameof(DisplayColor));

    partial void OnIsHealthWarningChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DisplayColor));
    }

    partial void OnIsListeningChanged(bool value) =>
        OnPropertyChanged(nameof(StatusText));

    partial void OnLastScanStatusChanged(ScanStatus? value)
    {
        OnPropertyChanged(nameof(FlashColor));
        OnPropertyChanged(nameof(FlashIcon));
        OnPropertyChanged(nameof(DisplayColor));
    }

    partial void OnScanTypeChanged(string value) =>
        OnPropertyChanged(nameof(ScanTypeBadgeColor));
}
```

### Phase 2 — XAML card

**File:** `SmartLog.Scanner/Views/MainPage.xaml`

Restructure the left column's `ScrollView` (currently containing only the FlexLayout) to wrap both the FlexLayout and the new USB card in a `VerticalStackLayout`. Keep the FlexLayout exactly as it is; append the USB card after it.

**Namespace import update:** Since `UsbScannerSlotState` lives in `SmartLog.Scanner.Core.ViewModels` (not `SmartLog.Scanner.ViewModels`), the existing `xmlns:vm` may need a sibling alias. Concretely, `MainPage.xaml` line 4 currently has:

```xml
xmlns:vm="clr-namespace:SmartLog.Scanner.ViewModels"
```

The bindings inside the USB card don't actually need a `DataTemplate x:DataType` (they bind through `MainViewModel.UsbScannerSlot.*`), so no new namespace is required for the USB card itself. The existing camera slot template uses `vm:CameraSlotState` (which lives in the MAUI project's `ViewModels`) — that import stays valid. **No XAML namespace changes needed.**

**Replace** the current `<ScrollView Grid.Row="1">...<FlexLayout>...</FlexLayout></ScrollView>` block (lines 192-292) with:

```xml
<ScrollView Grid.Row="1"
            Margin="0,4,0,0"
            VerticalScrollBarVisibility="Default">
    <VerticalStackLayout Spacing="0">

        <!-- Camera slot status cards (existing) -->
        <FlexLayout BindableLayout.ItemsSource="{Binding CameraSlots}"
                    Wrap="Wrap"
                    Direction="Row"
                    JustifyContent="Start"
                    AlignItems="Start">
            <BindableLayout.ItemTemplate>
                <DataTemplate x:DataType="vm:CameraSlotState">
                    <!-- ... unchanged camera slot template ... -->
                </DataTemplate>
            </BindableLayout.ItemTemplate>
        </FlexLayout>

        <!-- EP0012 (US0123): USB scanner indicator card -->
        <Border IsVisible="{Binding UsbScannerSlot.IsVisible}"
                BackgroundColor="White"
                Stroke="{Binding UsbScannerSlot.DisplayColor}"
                StrokeThickness="2"
                Padding="14,12"
                Margin="6,12,6,6"
                WidthRequest="220"
                HorizontalOptions="Start">
            <Border.StrokeShape>
                <RoundRectangle CornerRadius="10" />
            </Border.StrokeShape>
            <Border.Shadow>
                <Shadow Brush="Black" Opacity="0.07" Radius="8" Offset="0,2" />
            </Border.Shadow>

            <Grid RowDefinitions="Auto,Auto,Auto,Auto" RowSpacing="4">
                <!-- Header row: barcode icon + name + ENTRY/EXIT badge -->
                <Grid Grid.Row="0" ColumnDefinitions="Auto,*,Auto" Margin="0,0,0,4">
                    <!-- Vector barcode glyph (per AC3 — not an emoji) -->
                    <Path Grid.Column="0"
                          Data="M0,0 L2,0 L2,16 L0,16 Z M4,0 L5,0 L5,16 L4,16 Z M7,0 L9,0 L9,16 L7,16 Z M11,0 L12,0 L12,16 L11,16 Z M14,0 L16,0 L16,16 L14,16 Z"
                          Fill="#6A4C93"
                          WidthRequest="16"
                          HeightRequest="16"
                          Margin="0,0,6,0"
                          VerticalOptions="Center" />
                    <Label Grid.Column="1"
                           Text="{Binding UsbScannerSlot.DisplayName}"
                           FontSize="13"
                           FontAttributes="Bold"
                           TextColor="#6A4C93"
                           VerticalOptions="Center"
                           LineBreakMode="TailTruncation" />
                    <Border Grid.Column="2"
                            BackgroundColor="{Binding UsbScannerSlot.ScanTypeBadgeColor}"
                            Padding="7,2"
                            StrokeThickness="0"
                            VerticalOptions="Center">
                        <Border.StrokeShape>
                            <RoundRectangle CornerRadius="4" />
                        </Border.StrokeShape>
                        <Label Text="{Binding UsbScannerSlot.ScanType}"
                               FontSize="10"
                               FontAttributes="Bold"
                               TextColor="White" />
                    </Border>
                </Grid>

                <!-- Status text (Listening / No recent scans) -->
                <Label Grid.Row="1"
                       Text="{Binding UsbScannerSlot.StatusText}"
                       FontSize="12"
                       TextColor="#666666" />

                <!-- Flash: student name -->
                <Label Grid.Row="2"
                       Text="{Binding UsbScannerSlot.FlashStudentName}"
                       FontSize="12"
                       FontAttributes="Bold"
                       TextColor="{Binding UsbScannerSlot.FlashColor}"
                       LineBreakMode="TailTruncation"
                       IsVisible="{Binding UsbScannerSlot.ShowFlash}" />

                <!-- Flash: friendly message -->
                <Label Grid.Row="3"
                       Text="{Binding UsbScannerSlot.LastScanMessage}"
                       FontSize="11"
                       TextColor="#555555"
                       LineBreakMode="WordWrap"
                       MaxLines="2"
                       IsVisible="{Binding UsbScannerSlot.ShowFlash}" />
            </Grid>
        </Border>

    </VerticalStackLayout>
</ScrollView>
```

The USB card mirrors the camera card layout (same widths, padding, shadow) for visual consistency, but omits the restart button row (per AC10) and uses indigo accent throughout.

### Phase 3 — `MainViewModel` integration

**File:** `SmartLog.Scanner/ViewModels/MainViewModel.cs`

**3.a — New property + flash CTS + namespace import**

Near the existing `CameraSlots` property (line ~107):

```csharp
// using SmartLog.Scanner.Core.ViewModels;  // ← add to file usings (UsbScannerSlotState lives in Core)

/// <summary>
/// EP0012 (US0123): USB scanner indicator card state. Visible only when IsUsbMode.
/// </summary>
public UsbScannerSlotState UsbScannerSlot { get; } = new();

// Single CTS for the USB card flash (parallel to _flashTimers for cameras)
private CancellationTokenSource? _usbFlashCts;
```

**3.b — Initialize visibility + listening state**

In `InitializeAsync` after the USB pipeline starts (the new `if (IsUsbMode)` block from PL0019):

```csharp
if (IsUsbMode)
{
    await _usbScanner!.StartAsync();

    // EP0012: enable indicator card and start the listening timer
    UsbScannerSlot.IsVisible = true;
    UsbScannerSlot.ScanType = CurrentScanType;
    UsbScannerSlot.StartListening();
}
```

In `DisposeAsync`:

```csharp
if (IsUsbMode)
{
    UsbScannerSlot.StopListening();
    _usbFlashCts?.Cancel();
    _usbFlashCts?.Dispose();
    _usbFlashCts = null;

    await _usbScanner!.StopAsync();
}
```

**3.c — Extend the 1-second tick**

Current `OnFrameRateTick` (line ~410):

```csharp
private void OnFrameRateTick(object? sender, EventArgs e)
{
    foreach (var slot in CameraSlots)
        slot.UpdateFrameRate();
}
```

Add USB tick:

```csharp
private void OnFrameRateTick(object? sender, EventArgs e)
{
    foreach (var slot in CameraSlots)
        slot.UpdateFrameRate();

    if (IsUsbMode)
        UsbScannerSlot.Tick();
}
```

**Note on timer creation gate:** PL0019 already broadens the `_frameRateTimer` creation gate from `IsCameraMode` to `IsCameraMode || IsUsbMode` (Phase 2.c) precisely so PL0021 doesn't have to re-edit those same lines. By the time PL0021 lands, the timer is already created in `USB`-only mode — PL0021 only needs to add the `Tick()` call inside the existing handler.

In `Camera`-only mode, `IsUsbMode` is false so the `UsbScannerSlot.Tick()` call is a no-op.

**3.d — USB scan flash routing**

The existing `OnScanCompleted` handler (which is the USB path's subscriber per PL0019) already updates the central student card. We add USB-specific slot flash logic:

In `OnScanCompleted`, immediately after the `MainThread.BeginInvokeOnMainThread(...)` block, check whether the result came from USB:

```csharp
if (result.Source == ScanSource.UsbScanner && IsUsbMode)
{
    MainThread.BeginInvokeOnMainThread(() => TriggerUsbSlotFlash(result));
}
```

New method (parallel to camera's `TriggerSlotFlash`):

```csharp
private void TriggerUsbSlotFlash(ScanResult result)
{
    // Cancel any in-flight flash timer
    _usbFlashCts?.Cancel();
    _usbFlashCts?.Dispose();
    _usbFlashCts = new CancellationTokenSource();
    var cts = _usbFlashCts;

    var subjectName = result.IsVisitorScan
        ? $"Visitor #{result.PassNumber} — {result.ScanType}"
        : result.StudentName ?? result.StudentId;

    UsbScannerSlot.LastScanStatus = result.Status;
    UsbScannerSlot.LastScanMessage = ToFriendlyMessage(result);
    UsbScannerSlot.FlashStudentName = subjectName;
    UsbScannerSlot.ShowFlash = true;
    UsbScannerSlot.LastScanAt = result.ScannedAt;
    UsbScannerSlot.IsHealthWarning = false; // AC6: scan clears warning immediately

    _ = Task.Delay(3000, cts.Token).ContinueWith(t =>
    {
        if (t.IsCanceled) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UsbScannerSlot.ShowFlash = false;
            UsbScannerSlot.FlashStudentName = null;
            UsbScannerSlot.LastScanMessage = null;
            UsbScannerSlot.LastScanStatus = null;
        });
    });
}
```

**3.e — `ToggleScanType` updates USB slot too**

In the existing `ToggleScanType` command (after PL0019's broadening), add:

```csharp
if (IsUsbMode)
    UsbScannerSlot.ScanType = CurrentScanType;
```

### Phase 4 — Tests

**File:** `SmartLog.Scanner.Tests/ViewModels/UsbScannerSlotStateTests.cs` (new)

Reachable because `UsbScannerSlotState` lives in `SmartLog.Scanner.Core.ViewModels` and Core has `[InternalsVisibleTo("SmartLog.Scanner.Tests")]` (verified `SmartLog.Scanner.Core.csproj:10`). Tests use the `internal SetReferenceTimeForTest` seam to simulate elapsed time without `Thread.Sleep`.

```csharp
public class UsbScannerSlotStateTests
{
    [Fact]
    public void Initial_State_Is_Not_Listening_Not_Warning_Not_Visible()
    {
        var slot = new UsbScannerSlotState();
        Assert.False(slot.IsListening);
        Assert.False(slot.IsHealthWarning);
        Assert.False(slot.IsVisible);
    }

    [Fact]
    public void StartListening_Sets_IsListening_True_And_Resets_Warning()
    {
        var slot = new UsbScannerSlotState();
        slot.IsHealthWarning = true;
        slot.StartListening();
        Assert.True(slot.IsListening);
        Assert.False(slot.IsHealthWarning);
        Assert.Null(slot.LastScanAt);
    }

    [Fact]
    public void Tick_Within_60_Seconds_Of_Start_Does_Not_Set_Warning()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.SetReferenceTimeForTest(DateTime.UtcNow.AddSeconds(-30)); // 30s ago — under threshold
        slot.Tick();
        Assert.False(slot.IsHealthWarning);
    }

    [Fact]
    public void Tick_After_60_Seconds_Sets_Warning()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.SetReferenceTimeForTest(DateTime.UtcNow.AddSeconds(-61)); // 61s ago — over threshold
        slot.Tick();
        Assert.True(slot.IsHealthWarning);
    }

    [Fact]
    public void StatusText_When_Warning_Reads_Locked_Wording()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.SetReferenceTimeForTest(DateTime.UtcNow.AddSeconds(-90));
        slot.Tick();
        Assert.Equal("⚠ No recent scans (1m+)", slot.StatusText);
    }

    [Fact]
    public void StatusText_When_Listening_And_No_Warning_Reads_Listening()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        Assert.Equal("● Listening", slot.StatusText);
    }

    [Fact]
    public void DisplayColor_Indigo_When_Idle_Listening()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        Assert.Equal(Color.FromArgb("#6A4C93"), slot.DisplayColor);
    }

    [Fact]
    public void DisplayColor_Amber_When_Warning()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.SetReferenceTimeForTest(DateTime.UtcNow.AddSeconds(-90));
        slot.Tick();
        Assert.Equal(Color.FromArgb("#FF9800"), slot.DisplayColor);
    }

    [Fact]
    public void DisplayColor_Green_When_Flashing_Accepted_Result()
    {
        var slot = new UsbScannerSlotState();
        slot.StartListening();
        slot.LastScanStatus = ScanStatus.Accepted;
        slot.ShowFlash = true;
        Assert.Equal(Color.FromArgb("#4CAF50"), slot.DisplayColor);
    }

    [Theory]
    [InlineData(ScanStatus.Accepted, "✓")]
    [InlineData(ScanStatus.Duplicate, "⚠")]
    [InlineData(ScanStatus.Rejected, "✗")]
    [InlineData(ScanStatus.Queued, "📥")]
    [InlineData(ScanStatus.RateLimited, "⏱")]
    public void FlashIcon_Maps_Each_ScanStatus_To_Correct_Glyph(ScanStatus status, string expected)
    {
        var slot = new UsbScannerSlotState();
        slot.LastScanStatus = status;
        Assert.Equal(expected, slot.FlashIcon);
    }
}
```

**`MainViewModel` integration tests skipped:** `MainViewModel` lives in the MAUI project and is unreachable from the test project (same constraint as PL0019). The flash routing (`OnScanCompleted` → `TriggerUsbSlotFlash`) and tick wiring (`OnFrameRateTick` → `UsbScannerSlot.Tick()`) are validated by Phase 5 manual verification rather than unit tests.

### Phase 5 — Manual verification

**5.a — macOS dev build:** `dotnet run --project SmartLog.Scanner -f net8.0-maccatalyst`

1. **`Camera`-only mode regression:** USB card is not visible; camera FlexLayout fills column normally.
2. **`USB`-only mode:**
   - Card visible in the left column (camera area is hidden in USB-only mode per existing `IsCameraMode` binding, so the USB card naturally rises to the top)
   - Status text reads "● Listening" with indigo accent
   - Vector barcode glyph renders correctly (not an emoji)
   - After 60+ seconds with no scan: status flips to "⚠ No recent scans (1m+)" with amber accent
3. **Trigger a USB scan in `USB`-only mode via `TestValidQr`:**
   - In `USB`-only mode, `TestValidQr` routes through `_usbScanner.ProcessQrCodeAsync(payload)` (per PL0019 Phase 2.f), which DOES exercise the USB pipeline → USB card flashes
   - Card flashes green for 3 seconds with student name
   - Warning state (if it had been showing) clears immediately
   - Status returns to "● Listening" after flash
4. **`Both` mode (assumes PL0019 + PL0020 shipped):**
   - USB card visible at the bottom of the left column (below camera FlexLayout)
   - Camera scan: only the camera slot flashes; USB card stays "Listening"
   - **USB scan in `Both` mode:** `TestValidQr` routes through cameras (per PL0019 Phase 2.f), so the test command does NOT exercise the USB card flash in `Both` mode. Use a real USB scanner to verify the USB card flashes.
   - Cross-source dedup: scan via webcam, then via USB within 3s — only one server submission; USB card flashes with `Status = DebouncedLocally`
5. **ENTRY/EXIT toggle:** badge color on USB card flips red/teal in sync with camera slots (verifies `ToggleScanType` propagation from Phase 3.e)
6. **Scan log:** verify USB scan in `Both` mode appears in `ScanLogsPage` with `ScanMethod = "USB"` (the `Source` property propagates through `LogScanToHistoryAsync` from PL0019)

**5.b — Cross-build limitation (per CLAUDE.md):** `dotnet build SmartLog.Scanner -f net8.0-windows10.0.19041.0` from macOS will FAIL on the XAML compilation step because PL0021 modifies `MainPage.xaml` and `XamlCompiler.exe` is Windows-only. **This is expected** — defer Windows build verification to a Windows host.

**5.c — Windows hardware verification (in scope for PL0021 sign-off):**

Build on a Windows machine: `dotnet publish SmartLog.Scanner -f net8.0-windows10.0.19041.0 -c Release -o ./publish/win-x64`. Run on a target scanner PC.

1. **Vector `Path` barcode glyph rendering parity.** The `<Path Data="..." />` element is rendered by MAUI's `Microsoft.Maui.Graphics` parser on both platforms (CoreAnimation backend on macOS, WinUI Composition backend on Windows). Compare side-by-side with a macOS screenshot — the five vertical bars should render at identical positions and weights. **If divergence is visible:** fall back to importing `barcode.svg` into `Resources/Images/` and using `<Image Source="barcode.svg" />`.
2. **High-DPI rendering.** Test the `Path` glyph at Windows display scaling 100%, 125%, 150%, 200%. Bars should remain crisp (vector scales correctly) without anti-aliasing artifacts. Card layout should not clip the glyph.
3. **Card placement** at the bottom of the left column visually matches the macOS layout. ScrollView absorbs sizing correctly with 1-8 camera cards above + USB card.
4. **60s warning timing** on Windows. Start the app in `Both` or `USB` mode without scanning; verify warning state appears at ~60s elapsed (not earlier, not significantly later — the `IDispatcherTimer` interval should be consistent across platforms).
5. **USB scan flash on Windows.** Use a real USB barcode scanner connected to the Windows PC. In `Both` mode, scan a known-valid QR — verify USB card flashes green/✓ for 3s, the scan appears in the log with `ScanMethod = "USB"`, warning state clears.
6. **Visual differentiation.** Camera cards (teal/red) and USB card (indigo, with amber warning state) should be unambiguously distinct at a glance from a normal viewing distance (~50cm from screen). If two operators or product preview indicates colors are confusable, revisit the indigo accent.

---

## Risks & Considerations

- **`Path Data` rendering parity (Windows vs macOS).** MAUI's `Path` element on macOS Catalyst and Windows uses different underlying renderers (CoreAnimation vs WinUI Composition) but shares the path-data parser via `Microsoft.Maui.Graphics`. Simple rectangular paths like ours render identically in practice; verified during Phase 5.c step 1. If rendering diverges (e.g., bar positions shift, anti-aliasing differs noticeably), fallback is to import an SVG via `Resources/Images/barcode.svg` and `<Image Source="barcode.svg" />` — MAUI's SVG loader normalizes across platforms.
- **`UsbScannerSlotState` placement in Core (not MAUI project).** Required to make it testable from the existing test project. Constraint: cannot use `Brush` / `SolidColorBrush` (those live in `Microsoft.Maui.Controls`, not `Microsoft.Maui.Graphics` which Core has). Mitigation: expose `Color` properties; MAUI XAML auto-converts `Color → Brush` for `Border.Stroke`, `Background`, etc. Verified by `CameraSlotState` already using this pattern for some properties.
- **`Tick()` testability via `internal SetReferenceTimeForTest` seam.** Works because Core has `[InternalsVisibleTo("SmartLog.Scanner.Tests")]` (verified `SmartLog.Scanner.Core.csproj:10`). Trade-off: production code carries an `internal` test method. Acceptable given the test value (warning transition coverage without `Thread.Sleep`).
- **Card position when only one camera is configured.** With 1 camera card + USB card, the column has lots of unused vertical space. The USB card sits directly below the lone camera card; ScrollView absorbs sizing. Visually fine but if it looks awkward in UAT, consider tightening `Margin` on the USB card. Defer until visual review (Phase 5.c step 3).
- **`MainViewModel` flash routing not unit-testable.** `MainViewModel` lives in the MAUI project, unreachable from tests (same constraint as PL0019). The `TriggerUsbSlotFlash` integration (USB scan → slot flash) is covered by Phase 5 manual verification. Mitigation: keep `TriggerUsbSlotFlash` short and parallel to the existing `TriggerSlotFlash` pattern (which has been working in production for camera flashes since US0070).
- **Status icon in `Both` mode header (unchanged from PL0019).** Already addressed in PL0019's risks.
- **The 3-second flash window cannot be cancelled by mode change at runtime.** If the user toggles modes (very rare — typically requires app restart) while a USB flash is running, the timer fires after mode change. Mitigation: in `DisposeAsync` we cancel `_usbFlashCts`. Edge case is acceptable (mode changes are restart-required in practice).
- **Locked exact-string AC for warning text.** "⚠ No recent scans (1m+)" — Phase 4 tests assert this exact string. Any future copy change requires updating the test alongside.
- **Windows-specific:** WinUI 3 has occasional rendering glitches on `Border.Stroke` when the bound `Color` changes during a layout pass. If the warning state's amber color flicker is visible, wrap the `IsHealthWarning` setter in `MainThread.BeginInvokeOnMainThread` (it should already be — `Tick` is called from `IDispatcherTimer.Tick` which fires on the UI thread, so this should be a non-issue). Note for Phase 5.c verification.
- **Cross-build expectation:** PL0021 modifies XAML (`MainPage.xaml`) — cross-build from macOS will fail on the XAML compile step per CLAUDE.md. Expected; verify on Windows host instead.

---

## Out of Scope

- Plug/unplug detection via USB device APIs.
- Configurable warning threshold (hard-coded 60s per Q1).
- Audio cue when warning state appears.
- Multiple USB scanner slots.
- Live age update in warning text (per Q9 — generic suffix only).
- Reuse of `CameraSlotState` — separate type for clarity per US0123 scope.

---

## Estimated Effort

| Phase | Time |
|-------|------|
| 1 — `UsbScannerSlotState` ViewModel in Core (Color properties + test seam) | ~1 h |
| 2 — XAML card + ScrollView restructure | ~45 min |
| 3 — `MainViewModel` integration (property, tick handler addition, flash routing) | ~45 min |
| 4 — Tests (`UsbScannerSlotStateTests` only — `MainViewModel` covered by manual verification) | ~1 h |
| 5.a — macOS dev manual verification | ~30 min |
| 5.c — Windows hardware verification (separate session — vector glyph parity, DPI, 60s timing, real USB scan) | ~1 h |
| **Total** | **~5 hours** |

Aligns with US0123's 5-pt estimate. Test scope is tighter than originally planned (only `UsbScannerSlotState` since `MainViewModel` is unreachable), but the Windows hardware session adds time.

---

## Rollout Plan

1. **Wait for PL0019 to land** — `IsUsbMode`, `ScanResult.Source`, and the broadened `OnScanCompleted` routing are prerequisites.
2. Phase 1 — `UsbScannerSlotState` + test scaffolding; build clean.
3. Phase 2 — XAML restructure; verify camera FlexLayout layout unchanged.
4. Phase 3 — `MainViewModel` wiring; test `Both` mode camera/USB independence.
5. Phase 4 — full test suite green.
6. Phase 5 — macOS dev verification + Windows TFM cross-build. **Visual review of card on both platforms (especially the vector `Path` glyph) before declaring done.**
7. Confirm with user before commit.
8. Commit on `dev`; PR to `main` together with PL0019 + PL0020 as the EP0012 ship.

---

## Open Questions

> All resolved. Plan is ready for execution.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-28 | Claude (Opus 4.7) | Initial plan drafted; verified `CameraSlotState` template, MainPage left-column structure, MAUI `Path` element availability |
| 2026-04-28 | Claude (Opus 4.7) | Code-plan review — five fixes applied: (1) moved `UsbScannerSlotState` from `SmartLog.Scanner/ViewModels/` to `SmartLog.Scanner.Core/ViewModels/` so the test project can reference it (test project does not reference MAUI project); (2) replaced `Brush`/`SolidColorBrush` with `Color` properties (`DisplayColor`) — Core does not reference `Microsoft.Maui.Controls`; MAUI XAML auto-converts `Color → Brush`; (3) added `internal SetReferenceTimeForTest` seam (Core has `[InternalsVisibleTo]` so this works); (4) corrected Phase 5 manual verification — `TestValidQr` exercises USB pipeline only in `USB`-only mode (in `Both` mode it routes through cameras); (5) replaced cross-build assumption with explicit limitation note + dedicated Windows hardware verification (Phase 5.c) covering vector glyph parity, DPI scaling, 60s timing, real USB scan flash. Drop `MainViewModelTests.cs` extension. Effort estimate unchanged at ~5 h. |
