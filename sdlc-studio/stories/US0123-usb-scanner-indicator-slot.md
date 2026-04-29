# US0123: USB Scanner Indicator Slot with Health Heuristic

> **Status:** Done
> **Epic:** [EP0012: Concurrent Multi-Modal Scanning](../epics/EP0012-concurrent-multi-modal-scanning.md)
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-04-28

## User Story

**As** Guard Gary
**I want** the USB scanner to appear as its own indicator card on the main page (alongside the camera cards) showing whether it's listening, when it last fired, and a warning if it's been silent for too long
**So that** I can see at a glance that the handheld scanner is alive and ready, and notice immediately if it's been disconnected or knocked off the desk

## Context

### Persona Reference

**Guard Gary** — School security guard, novice technical proficiency. Glances at the gate display while managing student flow; needs visual confirmation that all input devices are healthy without reading text or troubleshooting.
[Full persona details](../personas.md#guard-gary)

### Background

Camera slots already render as status cards on `MainPage` showing health (Scanning / Error / Offline), the ENTRY/EXIT badge, the per-card flash on scan, and a Restart button when relevant (see `CameraSlotState.cs` and `MainPage.xaml`). The USB scanner currently has no equivalent — it's silent until it fires a scan, and there's no visual confirmation it's connected. Operators have no way to tell a working-but-quiet scanner from an unplugged one.

This story adds a sibling indicator card for the USB scanner. It uses the same scan-flash mechanism as camera cards (3-second flash with student name and result icon) but has different idle-state semantics: it shows "Listening" while alive and a 60-second no-scan warning if no scan has arrived in that window. Visually it's distinct from camera cards (different icon, accent color, condensed layout) so operators don't confuse it with a camera.

It also persists scan logs identically to camera scans — same `ScanLogEntry` fields populated — so admin dashboards see the USB source as a peer "device", not a separate stream.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| EP0012 | UX | Visual differentiation from camera cards (icon, color, layout) | New `UsbScannerSlotState` ViewModel and dedicated XAML template |
| EP0012 | Behavior | Scan log parity with camera scans | All `ScanLogEntry` fields populated for USB scans (handled in US0121 — this story consumes the result) |
| US0068 | Architecture | `MainPage` renders camera slots in a `FlexLayout` with `BindableLayout.ItemsSource` | USB indicator card is a peer element rendered alongside (or below) the FlexLayout, gated by `IsUsbMode` |
| US0070 | Pattern | Per-card flash animation uses `CancellationTokenSource` to prevent timer leaks | USB card reuses the same flash timer pattern from `MainViewModel` |

---

## Acceptance Criteria

### AC1: USB Slot State ViewModel

- **Given** the project's ViewModel layer
- **When** EP0012 is built
- **Then** a new `UsbScannerSlotState` ViewModel exists in `SmartLog.Scanner/ViewModels/`
- **And** it exposes observable properties: `DisplayName` (default "USB Scanner"), `ScanType`, `IsListening` (true while pipeline active), `LastScanAt` (DateTimeOffset?), `LastScanAgeSeconds` (computed), `ShowFlash`, `FlashStudentName`, `LastScanStatus`, `LastScanMessage`, `IsHealthWarning` (true when no scan in 60s)
- **And** computed display properties for `StatusText`, `FlashColor`, `FlashIcon`, `DisplayBrush`, `IsVisible` (gated by `IsUsbMode`)

### AC2: Card Visible Only in USB-Active Modes

- **Given** the main page renders
- **When** `Scanner.Mode` is `"USB"` or `"Both"`
- **Then** the USB indicator card is visible on the page
- **And** when `Scanner.Mode = "Camera"`, the card is hidden

### AC3: Listening State (Idle, Healthy)

- **Given** the USB pipeline is active and a scan was received less than 60 seconds ago (or the app just started)
- **When** the card renders
- **Then** the status text reads "● Listening" (or similar)
- **And** the border accent color is indigo/purple (`#6A4C93`-ish) to differentiate from camera teal/red
- **And** the icon shown is a **vector barcode glyph** rendered via SVG or `Microsoft.Maui.Controls.Shapes.Path` — not an emoji (avoids cross-platform rendering inconsistency)
- **And** no warning indicator is visible

### AC4: Scan Flash Mirrors Camera Behavior

- **Given** the USB pipeline is active
- **When** a USB scan completes (success, duplicate, rejected, etc.)
- **Then** the card flashes for 3 seconds showing the result icon (✓ / ⚠ / ✗ / 📥), student name (or "Visitor Pass #N"), and friendly message
- **And** the flash color matches the result status using the same palette as camera cards (green for accepted, amber for duplicate, red for rejected)
- **And** the previous flash timer is cancelled if a new scan arrives within the 3s window (no stale clear)

### AC5: Silence Warning During Active Session

- **Given** the USB pipeline is active AND at least one scan has arrived in the current app session AND the last scan was more than 60 seconds ago
- **When** the 1-second tick fires (the same ticker used by camera frame-rate updates)
- **Then** `IsHealthWarning` flips to true
- **And** the card displays an amber accent stroke (`#FF9800`)
- **And** the status text reads exactly "⚠ No recent scans (1m+)" — generic, no live age update (per Q9 decision)
- **And** the warning persists until the next scan arrives — it does not auto-clear on a timer
- **And** the warning never fires if no scan has arrived since app start (pre-session idle is normal, not an error)

### AC6: Warning Auto-Clears on Next Scan

- **Given** the USB indicator is in the warning state
- **When** any USB scan arrives
- **Then** `IsHealthWarning` flips to false immediately
- **And** the flash sequence runs (per AC4)
- **And** the warning state does not return until 60 more seconds of silence

### AC7: Scan Log Parity

- **Given** US0121 is implemented and a USB scan completes
- **When** `LogScanToHistoryAsync` persists the entry
- **Then** the resulting `ScanLogEntry` has the same set of fields populated as a camera scan (`Timestamp`, `RawPayload`, `StudentId`, `StudentName`, `ScanType`, `Status`, `Message`, `ScanId`, `NetworkAvailable`, `ProcessingTimeMs`, `GradeSection`, `ErrorDetails`, `ScanMethod = "USB"`)
- **And** when viewed in the scan logs page, USB scans appear in the same table as camera scans, distinguishable only by `ScanMethod`

### AC8: First-Run Behavior (Pipeline Just Started)

- **Given** the app has just started in `Both` or `USB` mode
- **When** the USB indicator card first renders before any scan has occurred
- **Then** the card shows "● Listening" (not warning) indefinitely until the first scan arrives
- **And** the warning state per AC5 never fires until after the first scan of the session has been recorded
- **And** this covers both the morning pre-session idle and any mid-day idle window between scanning sessions

### AC9: Card Hidden When USB Mode Off

- **Given** `Scanner.Mode = "Camera"`
- **When** the main page renders
- **Then** the USB indicator card is not in the visual tree (or has `IsVisible = false`)
- **And** no 1-second timer is running for the USB card
- **And** the camera slot grid layout adjusts to fill the available space

### AC10: Restart Button Absent

- **Given** the USB indicator card is visible
- **When** the card is in any state (Listening, Flashing, Warning)
- **Then** no Restart button is rendered
- **And** the card layout is more condensed than camera cards (no fps display, no restart row)

### AC11: Card Placement Below Camera FlexLayout

- **Given** the main page renders in `Both` or `USB` mode
- **When** the left column lays out
- **Then** the USB indicator card is positioned **below** the camera FlexLayout (still inside the same `ScrollView` in the left column)
- **And** the order from top to bottom of the left column is: camera 0 live preview → camera slot grid → USB indicator card
- **And** in `USB`-only mode where the camera grid is empty/hidden, the USB card sits directly below the (also-hidden) camera area, naturally rising to the top of the column

---

## Scope

### In Scope

- New `UsbScannerSlotState` ViewModel in `SmartLog.Scanner/ViewModels/`
- New `DataTemplate` (or inline `Border`) in `MainPage.xaml` for the USB indicator card
- Visual styling distinct from camera cards (icon, accent color, condensed layout)
- Integration with `MainViewModel` flash mechanism (route USB `OnScanCompleted` to update `UsbScannerSlotState.ShowFlash`, `LastScanStatus`, etc.)
- 1-second ticker integration to update `LastScanAgeSeconds` and toggle `IsHealthWarning`
- Visibility binding to `IsUsbMode` (depends on US0121's helpers)
- Unit tests for `UsbScannerSlotState` warning state transitions
- Manual UAT script: idle for 60s → warning, scan → cleared, etc.

### Out of Scope

- Plug/unplug detection via Windows USB device APIs
- Configurable warning threshold (hard-coded 60s)
- Audio cue for warning state
- Multiple USB scanner cards (only one supported)
- Reuse of `CameraSlotState` — separate type for clarity

---

## Technical Notes

### UsbScannerSlotState Sketch

```csharp
public partial class UsbScannerSlotState : ObservableObject
{
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

    public string StatusText => IsHealthWarning
        ? "⚠ No recent scans"
        : (IsListening ? "● Listening" : "○ Idle");

    public Brush DisplayBrush => ShowFlash
        ? new SolidColorBrush(FlashColor)
        : new SolidColorBrush(IsHealthWarning
            ? Color.FromArgb("#FF9800")
            : Color.FromArgb("#6A4C93"));

    public Color FlashColor => /* same palette as CameraSlotState */;
    public string FlashIcon => /* same icons as CameraSlotState */;

    /// <summary>Called by 1s timer in MainViewModel — toggles warning state.</summary>
    public void Tick()
    {
        if (!IsListening || !LastScanAt.HasValue) return; // no warning before first scan of session
        var ageSeconds = (DateTimeOffset.UtcNow - LastScanAt.Value).TotalSeconds;
        IsHealthWarning = ageSeconds >= 60;
    }
}
```

### MainViewModel Integration

Add a single `UsbScannerSlot` property (not a collection — only one):

```csharp
public UsbScannerSlotState UsbScannerSlot { get; } = new();
```

In the existing 1-second `_frameRateTimer.Tick`, also call `UsbScannerSlot.Tick()` if `IsUsbMode`.

In `OnScanCompleted` (USB path), trigger flash on `UsbScannerSlot` analogous to `TriggerSlotFlash` for cameras. Reset `LastScanAt = DateTimeOffset.UtcNow`.

### Barcode Vector Icon

Use `Microsoft.Maui.Controls.Shapes.Path` (or an SVG file imported via `MauiImage`) for a small vertical-bar barcode glyph. Concrete approach — add a styled `Path` resource:

```xml
<!-- In Resources/Styles or inline -->
<Path Data="M0,0 L2,0 L2,16 L0,16 Z M4,0 L5,0 L5,16 L4,16 Z M7,0 L9,0 L9,16 L7,16 Z M11,0 L12,0 L12,16 L11,16 Z M14,0 L16,0 L16,16 L14,16 Z"
      Fill="#6A4C93"
      WidthRequest="16"
      HeightRequest="16" />
```

Renders consistently across macOS and Windows (no emoji font dependency, no platform-specific rendering inconsistencies). Color picks up the indigo accent.

If a vector asset is preferred over inline `Path`, drop a `barcode.svg` into `Resources/Images/` and reference via `Source="barcode.svg"` on an `Image`.

### XAML Sketch

In `MainPage.xaml`, **at the bottom of the left column ScrollView, after the camera FlexLayout** (per AC11 placement decision):

```xml
<Border IsVisible="{Binding UsbScannerSlot.IsVisible}"
        BackgroundColor="White"
        Stroke="{Binding UsbScannerSlot.DisplayBrush}"
        StrokeThickness="2"
        Padding="14,12"
        Margin="6,12,6,6">
    <Border.StrokeShape>
        <RoundRectangle CornerRadius="10" />
    </Border.StrokeShape>
    <Grid RowDefinitions="Auto,Auto,Auto,Auto" RowSpacing="4">
        <Grid Grid.Row="0" ColumnDefinitions="Auto,*,Auto">
            <!-- Vector barcode icon (not emoji) -->
            <Path Grid.Column="0"
                  Data="M0,0 L2,0 L2,16 L0,16 Z M4,0 L5,0 L5,16 L4,16 Z M7,0 L9,0 L9,16 L7,16 Z M11,0 L12,0 L12,16 L11,16 Z M14,0 L16,0 L16,16 L14,16 Z"
                  Fill="#6A4C93"
                  WidthRequest="16"
                  HeightRequest="16"
                  Margin="0,0,6,0"
                  VerticalOptions="Center" />
            <Label Grid.Column="1" Text="{Binding UsbScannerSlot.DisplayName}"
                   FontSize="13" FontAttributes="Bold" TextColor="#6A4C93" />
            <Border Grid.Column="2" BackgroundColor="#6A4C93" Padding="7,2">
                <Border.StrokeShape><RoundRectangle CornerRadius="4" /></Border.StrokeShape>
                <Label Text="{Binding UsbScannerSlot.ScanType}"
                       FontSize="10" FontAttributes="Bold" TextColor="White" />
            </Border>
        </Grid>
        <Label Grid.Row="1" Text="{Binding UsbScannerSlot.StatusText}" FontSize="12" TextColor="#666666" />
        <Label Grid.Row="2" Text="{Binding UsbScannerSlot.FlashStudentName}"
               FontSize="12" FontAttributes="Bold"
               TextColor="{Binding UsbScannerSlot.FlashColor}"
               IsVisible="{Binding UsbScannerSlot.ShowFlash}" />
        <Label Grid.Row="3" Text="{Binding UsbScannerSlot.LastScanMessage}"
               FontSize="11" TextColor="#555555"
               IsVisible="{Binding UsbScannerSlot.ShowFlash}" />
    </Grid>
</Border>
```

### Files Likely Touched

- `SmartLog.Scanner/ViewModels/UsbScannerSlotState.cs` — new
- `SmartLog.Scanner/ViewModels/MainViewModel.cs` — add `UsbScannerSlot` property, route USB scans into it, drive 60s tick
- `SmartLog.Scanner/Views/MainPage.xaml` — new card in left column
- `SmartLog.Scanner.Tests/ViewModels/UsbScannerSlotStateTests.cs` — new unit tests for warning transitions
- `SmartLog.Scanner.Tests/ViewModels/MainViewModelTests.cs` — verify USB scan triggers slot flash + clears warning

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Mode is `Camera` only | Card not rendered; no timer; no warning logic runs |
| App started in `Both` mode but USB scanner never plugged in | Card shows "Listening" indefinitely — no warning fires until at least one scan arrives. Operator should notice missing scans contextually (no students logging in); the card does not false-alarm. |
| USB scanner emits payload that fails HMAC validation | Same flash flow as camera reject — red accent, ✗ icon, friendly message; warning timer resets |
| Many rapid USB scans (< 3s apart) | Each scan cancels previous flash timer and starts new 3s flash; warning timer resets on each |
| App in foreground but USB scanner not focused (e.g., on dialog) | Keystrokes dropped silently; no scan arrives; warning fires after 60s |
| Mode changed at runtime (rare — typically requires restart) | Card visibility updates via binding; if mode flips Camera→Both, card appears with "Listening" state and fresh 60s timer |
| Tick fires while flash is showing | Independent — tick only toggles warning; flash has own 3s timer |

---

## Test Scenarios

- [ ] Card renders when `Scanner.Mode = "USB"`
- [ ] Card renders when `Scanner.Mode = "Both"`
- [ ] Card hidden when `Scanner.Mode = "Camera"`
- [ ] On app start, card shows "Listening" indefinitely until first scan — no warning fires before first scan
- [ ] After first scan + 60 seconds of silence, `IsHealthWarning` becomes true
- [ ] Status text shows "⚠ No recent scans (1m+)" in warning state
- [ ] Border accent is amber (`#FF9800`) in warning state
- [ ] Border accent is indigo (`#6A4C93`) in normal listening state
- [ ] USB scan within first 60s after a prior scan does not trigger warning
- [ ] USB scan in warning state immediately clears `IsHealthWarning`
- [ ] Warning persists through mid-day idle — clears only when a scan arrives, not on a timer
- [ ] Flash on accepted scan: green accent, ✓ icon, student name visible
- [ ] Flash on rejected scan: red accent, ✗ icon
- [ ] Flash auto-clears after 3 seconds
- [ ] Two scans within 3s: second scan cancels first flash timer cleanly
- [ ] Card has no Restart button (regression check)
- [ ] Card icon is a vector barcode glyph rendered via `Path` or SVG (not an emoji)
- [ ] Card is positioned below the camera FlexLayout in the left column (visual regression check)
- [ ] Warning text reads exactly "⚠ No recent scans (1m+)" — does not include live age
- [ ] USB scan persists `ScanLogEntry` with `ScanMethod = "USB"` and same field set as camera scan

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0121 | Blocked By | Concurrent runtime support — `OnScanCompleted` routing for USB scans, `IsUsbMode` helper | Draft |
| US0068 | Blocked By | Main page camera grid layout (USB card lives in same column) | Done |
| US0070 | Blocked By | Per-card flash animation pattern with cancellation tokens | Done |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| MAUI 1-second `IDispatcherTimer` | Platform API | Available |
| Existing `_frameRateTimer` ticker | Internal | Available |

---

## Estimation

**Story Points:** 5
**Complexity:** Medium

**Rationale:** Adds a new ViewModel, new XAML, and integration with the existing flash and tick infrastructure. The visual styling and warning state machine are straightforward but introduce new edge cases (first-run, mode switching, flash-vs-warning interaction) that need explicit tests. The card itself is simpler than a camera card (no fps, no restart) which trims complexity.

---

## Open Questions

- [x] **Resolved 2026-04-28:** Warning text is generic with `1m+` suffix only — exactly "⚠ No recent scans (1m+)". No live age update (avoids per-second re-render and visual noise). Precise age available on the heartbeat dashboard via `usbScannerLastScanAge`.
- [x] **Resolved 2026-04-28:** Warning fires after 60 seconds of silence **only if at least one scan has arrived this session**. No auto-clear on a timer — warning persists until the next scan. Pre-session idle (before first scan) always shows "Listening". Mid-day, the warning will show on the gate display — that is intentional and honest: it clears the moment the afternoon session starts and a scan arrives. An auto-clear timer was considered but rejected because it masks real hardware failures (scanner kicked out mid-rush would eventually clear itself with no operator action).
- [x] **Resolved 2026-04-28:** Card placement is **bottom of the left column**, below the camera FlexLayout. Keeps cameras as the primary visual focus and matches a natural top-down reading order (live preview → camera grid → USB indicator).
- [x] **Resolved 2026-04-28:** Icon is a **vector barcode glyph** rendered via `Microsoft.Maui.Controls.Shapes.Path` (or an SVG asset) — not an emoji. Renders consistently across macOS and Windows; no emoji-font dependency or weaponry connotation issues.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-28 | SDLC Studio | Initial story created under EP0012 |
| 2026-04-28 | SDLC Studio | Open questions resolved — AC3 specifies vector barcode glyph (not emoji); AC5 locks warning text wording; new AC11 for card placement; XAML sketch and Technical Notes updated to match |
| 2026-04-28 | MarkMarmeto | **Flash duration revised to 1 second** (was 3 s in original AC4, reduced to 500 ms, settled at 1 s after field UX review — fast enough for one-scan-per-second throughput, long enough for operators to read the result). **Per-camera scan gate added:** while a camera slot is flashing (`ShowFlash = true`), new scans from that camera are dropped in `MainViewModel.OnMultiCameraScanCompleted` — each camera gates independently via `_cameraGated[cameraIndex]`. **Central card CTS:** `_centralCardCts` replaces all fire-and-forget 3 s reset timers on the shared student card; Camera 2 scanning at T=2.9 s now cancels Camera 1's T+3 s timer, ensuring every scan gets its full display window. **Restart button removed from camera cards** — auto-recovery handles camera errors silently; simplified `CameraSlotState.StatusText` to two states: `● Ready to Scan` (operational) and error/offline copy. |
