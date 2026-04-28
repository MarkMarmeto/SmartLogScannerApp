# PL0020: Setup Wizard — Concurrent Scanner Mode Configuration

> **Status:** Draft
> **Story:** [US0122: Setup Wizard — Concurrent Scanner Mode Configuration](../stories/US0122-setup-wizard-concurrent-mode-config.md)
> **Epic:** [EP0012: Concurrent Multi-Modal Scanning](../epics/EP0012-concurrent-multi-modal-scanning.md)
> **Created:** 2026-04-28
> **Language:** C# 12 / .NET 8 MAUI (Windows + macOS)
> **Drafted by:** Claude (Opus 4.7)

---

## Overview

Add an **opt-in checkbox** ("Also accept USB scanner input alongside cameras") to the existing Scanner Configuration card in `SetupPage.xaml`. When ticked and saved, the persisted `Scanner.Mode` becomes `"Both"`; otherwise the existing collapse to `"Camera"` or `"USB"` is preserved (no silent mode migration on upgrade).

The UI work is small — one new bindable property on `SetupViewModel`, one `<CheckBox>` block in XAML, and a tiny resolution helper for the save path. Most of the effort is the table-driven test that locks AC4 behavior for every (detected method × checkbox) combination.

This plan depends on **PL0019** — the runtime support for `Scanner.Mode = "Both"` must exist before the setting becomes meaningful.

---

## Acceptance Criteria Mapping

| AC (US0122) | Phase |
|-------------|-------|
| AC1: Observable property `EnableUsbScannerInput` (bool, default false) | Phase 1 |
| AC2: Checkbox in Scanner Configuration card with helper text | Phase 2 (XAML) |
| AC3: Pre-populated from existing `Scanner.Mode = "Both"` | Phase 1 — `InitializeAsync` |
| AC4: Save resolution table | Phase 1 — `ResolveScanMode` static helper |
| AC5: Hint banner when both detected (opt-in default) | Phase 1 — `OnDetectedScanMethodChanged` updates banner only, does NOT auto-check |
| AC6: Checkbox disabled when only USB detected | Phase 1 — `CanEnableUsb` computed property + `IsEnabled` binding |
| AC7: No hidden mode migration | Verified by `InitializeAsync` not auto-flipping the checkbox; `SaveAsync` only writes when user explicitly clicks Save |
| AC8: Edit mode preserves other settings | No code change — existing save logic preserved; new field is additive |

---

## Technical Context (Verified)

### Confirmed via code read

- `SetupViewModel` (`SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs`) uses `[ObservableProperty]` source-generators for all fields — adding a new one follows the established pattern.
- `IPreferencesService.GetScanMode()` and `SetScanMode(string)` already exist (verified in `PreferencesService.cs:29-37`).
- `SetupViewModel.InitializeAsync` already has an edit-mode branch (`if (_preferences.GetSetupCompleted())`) that pre-fills form fields. The new `EnableUsbScannerInput` initialization slots in there.
- `SetupViewModel.SaveAsync` line 230-237 currently does the `CameraWithUsbFallback → "Camera"` collapse via a `switch`. That stays — the new resolution wraps it.
- Existing detected-devices banner is bound to `DetectedDevicesMessage` (line 38). New banner text in `Both`-detected state writes to that same property.
- The Scanner Configuration card in `SetupPage.xaml` (lines 252-343) contains the existing Camera Picker (line 298-321) and Default Scan Type picker (line 323-341). The new checkbox slots between or after these — placement decided in Phase 2.

### Files to touch

| File | Change |
|------|--------|
| `SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs` | New `EnableUsbScannerInput` property, `CanEnableUsb` computed, `OnDetectedScanMethodChanged` banner update, `ResolveScanMode` helper, `InitializeAsync` pre-populate, `SaveAsync` use resolved mode |
| `SmartLog.Scanner/Views/SetupPage.xaml` | New `<CheckBox>` block in Scanner Configuration card |
| `SmartLog.Scanner.Tests/ViewModels/SetupViewModelTests.cs` | Table-driven tests for `ResolveScanMode`, init/save behavior |

---

## Implementation Phases

### Phase 1 — `SetupViewModel` changes

**File:** `SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs`

**1.a — Add observable property**

Near the existing camera picker properties (line ~62-65):

```csharp
// EP0012 (US0122): Concurrent scanner mode opt-in
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(CanEnableUsb))]
private bool _enableUsbScannerInput;

/// <summary>
/// True when both cameras are detected (or are detectable) — required to enable
/// concurrent USB input. False when only USB or no devices detected.
/// </summary>
public bool CanEnableUsb =>
    DetectedScanMethod is ScanningMethod.Camera or ScanningMethod.CameraWithUsbFallback;
```

The `[NotifyPropertyChangedFor(nameof(CanEnableUsb))]` attribute on `_enableUsbScannerInput` is a no-op (the checkbox doesn't drive `CanEnableUsb`); the actual driver is `DetectedScanMethod`. We need to notify `CanEnableUsb` when `DetectedScanMethod` changes:

```csharp
partial void OnDetectedScanMethodChanged(ScanningMethod value)
{
    OnPropertyChanged(nameof(CanEnableUsb));

    // AC5: When both detected, update banner to invite the admin to opt in (no auto-check).
    if (value == ScanningMethod.CameraWithUsbFallback)
    {
        DetectedDevicesMessage = "Detected: webcam + USB scanner. Tick the option below to use both at the same time.";
    }
    // Other detection states keep whatever message _deviceDetection.GetDetectionSummary() set.
}
```

**1.b — Static resolution helper**

```csharp
/// <summary>
/// EP0012/US0122 AC4: Resolves the persisted Scanner.Mode value from the detected
/// scanner method and the admin's opt-in checkbox.
/// </summary>
internal static string ResolveScanMode(ScanningMethod detected, bool enableUsb) => detected switch
{
    ScanningMethod.Camera or ScanningMethod.CameraWithUsbFallback when enableUsb => "Both",
    ScanningMethod.Camera or ScanningMethod.CameraWithUsbFallback => "Camera",
    ScanningMethod.UsbScanner => "USB",
    _ => "USB" // None — preserves existing default
};
```

`internal` + `[InternalsVisibleTo("SmartLog.Scanner.Tests")]` (already present in csproj) so tests can call directly.

**1.c — `InitializeAsync` — pre-populate from saved mode**

In the existing edit-mode branch (`if (_preferences.GetSetupCompleted())`, around line 94), add:

```csharp
EnableUsbScannerInput = _preferences.GetScanMode() == "Both";
```

Place after the existing `AcceptSelfSignedCerts = _preferences.GetAcceptSelfSignedCerts();` line for grouping.

**1.d — `SaveAsync` — use resolved mode**

Current (line 230-237):

```csharp
_preferences.SetScanMode(DetectedScanMethod switch
{
    ScanningMethod.Camera => "Camera",
    ScanningMethod.CameraWithUsbFallback => "Camera",
    ScanningMethod.UsbScanner => "USB",
    _ => "USB"
});
```

Replace with:

```csharp
_preferences.SetScanMode(ResolveScanMode(DetectedScanMethod, EnableUsbScannerInput));
```

This single-line replacement preserves the legacy collapse behavior (`CameraWithUsbFallback → "Camera"` when checkbox is unchecked) and adds the `"Both"` path when ticked.

### Phase 2 — XAML checkbox in Scanner Configuration card

**File:** `SmartLog.Scanner/Views/SetupPage.xaml`

**Placement:** after the existing Camera Picker block (line 321) but before the Default Scan Type picker (line 323) — keeps the "what device" decisions grouped together.

```xml
<!-- EP0012 (US0122): Concurrent scanner mode opt-in -->
<VerticalStackLayout Spacing="8" IsVisible="{Binding CanEnableUsb}" Margin="0,8,0,0">
    <HorizontalStackLayout Spacing="10">
        <CheckBox IsChecked="{Binding EnableUsbScannerInput, Mode=TwoWay}"
                  Color="#4D9B91"
                  VerticalOptions="Center" />
        <VerticalStackLayout Spacing="2" VerticalOptions="Center">
            <Label Text="Also accept USB scanner input alongside cameras"
                   FontSize="14"
                   FontAttributes="Bold"
                   TextColor="#333333" />
            <Label Text="Enable this if you have both webcam(s) and a handheld barcode scanner at this gate"
                   FontSize="12"
                   TextColor="#666666"
                   LineBreakMode="WordWrap" />
        </VerticalStackLayout>
    </HorizontalStackLayout>
</VerticalStackLayout>
```

The wrapping `IsVisible="{Binding CanEnableUsb}"` hides the entire block when only USB is detected (per AC6) — cleaner than `IsEnabled=false` greying.

### Phase 3 — Tests

**File:** `SmartLog.Scanner.Tests/ViewModels/SetupViewModelTests.cs`

**3.a — Table-driven `ResolveScanMode` tests**

```csharp
[Theory]
[InlineData(ScanningMethod.Camera, false, "Camera")]
[InlineData(ScanningMethod.Camera, true, "Both")]
[InlineData(ScanningMethod.CameraWithUsbFallback, false, "Camera")]
[InlineData(ScanningMethod.CameraWithUsbFallback, true, "Both")]
[InlineData(ScanningMethod.UsbScanner, false, "USB")]
[InlineData(ScanningMethod.UsbScanner, true, "USB")]
[InlineData(ScanningMethod.None, false, "USB")]
[InlineData(ScanningMethod.None, true, "USB")]
public void ResolveScanMode_Returns_Expected_Mode(
    ScanningMethod detected, bool enableUsb, string expected)
{
    Assert.Equal(expected, SetupViewModel.ResolveScanMode(detected, enableUsb));
}
```

**3.b — Init pre-population tests**

- `InitializeAsync_With_Mode_Both_Sets_EnableUsbScannerInput_True`
- `InitializeAsync_With_Mode_Camera_Sets_EnableUsbScannerInput_False`
- `InitializeAsync_With_Mode_Usb_Sets_EnableUsbScannerInput_False`

Use Moq for `IPreferencesService.GetScanMode()` returning the seeded value, plus the existing setup-complete mock pattern.

**3.c — Save behavior**

- `SaveAsync_With_DetectedCamera_And_CheckboxTrue_Persists_Both` — verify `SetScanMode("Both")` called on the mock
- `SaveAsync_With_DetectedCamera_And_CheckboxFalse_Persists_Camera`
- `SaveAsync_With_DetectedUsb_Persists_Usb_Regardless_Of_Checkbox`

**3.d — `OnDetectedScanMethodChanged` banner update**

- `Setting_DetectedScanMethod_To_CameraWithUsbFallback_Updates_Banner_Without_Checking_Checkbox` — the key opt-in regression test

**3.e — `CanEnableUsb` flip**

- `CanEnableUsb_True_For_Camera`
- `CanEnableUsb_True_For_CameraWithUsbFallback`
- `CanEnableUsb_False_For_UsbScanner`
- `CanEnableUsb_False_For_None`

### Phase 4 — Manual verification

**4.a — macOS dev build:** `dotnet run --project SmartLog.Scanner -f net8.0-maccatalyst`

1. **Fresh install (no setup completed):**
   - Setup wizard appears
   - Verify checkbox starts unchecked
   - Save without ticking → `Scanner.Mode = "Camera"` (or `"USB"` per detection)
2. **Fresh install with simulated `CameraWithUsbFallback`:**
   - (May require manual override in `IDeviceDetectionService` mock — or just check banner text against AC5 wording)
   - Verify banner reads "Detected: webcam + USB scanner. Tick the option below..."
   - Verify checkbox is unchecked (opt-in default)
   - Tick + save → `Scanner.Mode = "Both"` persisted; restart app → both pipelines start (assumes PL0019 shipped)
3. **Edit mode upgrade path:**
   - Existing install with `Scanner.Mode = "Camera"` → open settings → checkbox unchecked → save → `Scanner.Mode = "Camera"` (no migration)
   - Existing install with `Scanner.Mode = "Both"` → open settings → checkbox checked → save unchanged → `Scanner.Mode = "Both"` preserved
4. **Disable path:**
   - Install with `Scanner.Mode = "Both"` → settings → untick → save → `Scanner.Mode = "Camera"` (per AC8 + ResolveScanMode logic)
5. **USB-only detected:**
   - Verify checkbox block is hidden (not greyed) per AC6 + `CanEnableUsb` binding

**4.b — Cross-build limitation (per CLAUDE.md):** `dotnet build SmartLog.Scanner -f net8.0-windows10.0.19041.0` from macOS will FAIL on the XAML compilation step because `XamlCompiler.exe` is Windows-only and PL0020 modifies `SetupPage.xaml`. **This is expected** — defer Windows build verification to a Windows host.

**4.c — Windows hardware verification (in scope for PL0020 acceptance):**

Build on a Windows machine: `dotnet publish SmartLog.Scanner -f net8.0-windows10.0.19041.0 -c Release -o ./publish/win-x64`. Run on a target scanner PC.

1. **CheckBox renders correctly on Windows.** Verify the new checkbox in the Scanner Configuration card looks consistent with the existing TLS Certificate Security checkbox (already verified across platforms by US0004). Color teal (`#4D9B91`) renders as expected.
2. **Helper text wraps correctly at the column width** (320 px max per existing layout) — no clipping at common Windows DPI scales (100%/125%/150%).
3. **Detection summary banner:** With both webcam and USB scanner connected to the Windows PC, run setup; verify `IDeviceDetectionService` returns `CameraWithUsbFallback` and the banner shows the opt-in invite copy from AC5.
4. **End-to-end on Windows:** tick the checkbox, save, restart app, confirm `Both` mode runs both pipelines (depends on PL0019 having shipped first).

---

## Risks & Considerations

- **Detection accuracy.** `IDeviceDetectionService` reports `CameraWithUsbFallback` only when both are detected. If the USB scanner is plugged in but not recognized by detection (e.g., admin tested without aiming a key into the focused window), `DetectedScanMethod` may report `Camera` only — `CanEnableUsb` is still true since `Camera` qualifies, so the checkbox still appears. Banner text fallback in that case stays the device-detection summary. **Acceptable** — the checkbox is the source of truth, not the banner.
- **Race in init pre-population.** `InitializeAsync` reads `_preferences.GetScanMode()` synchronously before detection completes. The pre-population happens early; later when detection updates `DetectedScanMethod`, `OnDetectedScanMethodChanged` fires but does NOT touch `EnableUsbScannerInput` (per opt-in behavior). Check confirms the partial method only updates banner text. **Verified safe.**
- **First-run with Mode=None saved as USB.** Existing default — preserved. New checkbox is hidden when detection returns `None` since `CanEnableUsb` is false. Save still goes to `"USB"` per ResolveScanMode.
- **Wording drift.** AC5's banner wording and the helper sub-line are locked verbatim in this plan. Tests assert the banner text. If product later wants to change copy, they update the test and the constant.
- **CheckBox styling on macOS.** MAUI `<CheckBox Color="#4D9B91" />` renders correctly on both platforms — verified in existing Setup page (TLS Certificate Security section uses `<CheckBox Color="#FF9800" />`).

---

## Out of Scope

- Runtime support for `Scanner.Mode = "Both"` (PL0019).
- USB indicator slot UI (PL0021).
- Per-USB-scanner profile / scanner model dropdown.
- Migration script to flip existing installs to `"Both"` (explicitly opt-in only per AC7).
- Admin-side WebApp config to push `Scanner.Mode` remotely.

---

## Estimated Effort

| Phase | Time |
|-------|------|
| 1 — `SetupViewModel` property + helper + init/save | ~45 min |
| 2 — XAML checkbox block | ~15 min |
| 3 — Tests (table + behavior) | ~1 h |
| 4.a — macOS dev manual verification | ~30 min |
| 4.c — Windows hardware verification (separate session on Windows host) | ~30 min |
| **Total** | **~3 hours** |

Aligns with US0122's 3-pt estimate.

---

## Rollout Plan

1. **Wait for PL0019 to land** — the `Both` mode setting is meaningless without runtime support.
2. Phase 1 + 2 — implement; build clean.
3. Phase 3 — tests green (`dotnet test`).
4. Phase 4 — manual verification, particularly the opt-in/no-migration regression on an existing-install simulation.
5. Confirm with user before commit.
6. Commit on `dev`; PR to `main` together with PL0019 + PL0021 as the EP0012 ship.

---

## Open Questions

> All resolved. Plan is ready for execution.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-28 | Claude (Opus 4.7) | Initial plan drafted; verified `IPreferencesService` API and existing setup wizard structure |
| 2026-04-28 | Claude (Opus 4.7) | Code-plan review — added Phase 4.b cross-build limitation note (XAML compile from macOS fails per CLAUDE.md); split Phase 4 into macOS dev (4.a) + dedicated Windows hardware verification (4.c) covering CheckBox rendering, DPI scaling, banner copy, end-to-end on Windows. Effort 2.5 h → 3 h. |
