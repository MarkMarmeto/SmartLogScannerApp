# US0122: Setup Wizard — Concurrent Scanner Mode Configuration

> **Status:** Draft
> **Epic:** [EP0012: Concurrent Multi-Modal Scanning](../epics/EP0012-concurrent-multi-modal-scanning.md)
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-04-28

## User Story

**As** IT Admin Ian
**I want** to enable USB scanner input alongside webcams in the device setup wizard
**So that** I can configure a single PC to handle a gate's mixed input hardware (multiple webcams + handheld scanner) without juggling app modes or buying duplicate scanner devices

## Context

### Persona Reference

**IT Admin Ian** — Maintains gate scanner devices remotely; values clear setup forms with sensible defaults; configures setup once and rarely revisits.
[Full persona details](../personas.md#it-admin-ian)

### Background

The existing setup wizard runs `IDeviceDetectionService.DetectDevicesAsync()` and chooses one of three values for `Scanner.Mode` based on what's connected: `"Camera"`, `"CameraWithUsbFallback"`, or `"USB"`. Today the wizard normalizes `CameraWithUsbFallback` down to `"Camera"` when saving (see `SetupViewModel.SaveAsync`) — meaning the existing detection logic *already noticed* the dual-input case but the resulting save collapsed it back to camera-only.

This story lifts that limitation: when both camera and USB scanner are detected (or when the admin explicitly opts in), `Scanner.Mode` saves as `"Both"` and US0121's runtime support kicks in.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| EP0012 | Compatibility | Default behavior for new installs unchanged unless user opts in | Auto-detect proposes `"Both"` only when both detected; never silently downgrades existing single-mode prefs |
| US0008 | Behaviour | USB scanner is not detectable as a peripheral (HID keyboard wedge looks like a generic keyboard) | The "USB scanner detected" hint is a heuristic; admin must confirm via checkbox |
| TRD | Architecture | Setup form values bound via `SetupViewModel` observables | New observable property: `EnableUsbScannerInput` |
| EP0011 | UX | Setup wizard already has multi-camera config card | New checkbox lives near the existing scanner configuration card, not as a new section |

---

## Acceptance Criteria

### AC1: New Observable Property for USB Opt-In

- **Given** the setup wizard is loaded
- **When** `SetupViewModel` initializes
- **Then** a new observable property `EnableUsbScannerInput` (bool, default false) is exposed and bindable in XAML

### AC2: UI Checkbox in Scanner Configuration Card

- **Given** the user opens the setup wizard
- **When** the page renders
- **Then** a checkbox labeled "Also accept USB scanner input alongside cameras" appears in the existing Scanner Configuration card
- **And** the checkbox is bound TwoWay to `SetupViewModel.EnableUsbScannerInput`
- **And** a help line below the checkbox explains: "Enable this if you have both webcam(s) and a USB barcode scanner at this gate"

### AC3: Checkbox Pre-Populated from Existing Preference

- **Given** the user opens the setup wizard in edit mode (`Setup.Completed = true`)
- **When** `InitializeAsync` runs
- **Then** if `Scanner.Mode = "Both"`, `EnableUsbScannerInput` is set to `true` and the checkbox renders checked
- **And** if `Scanner.Mode = "Camera"` or `"USB"`, the checkbox renders unchecked

### AC4: Save Logic Resolves to Correct Mode

- **Given** the user clicks "Save Changes" in the setup wizard
- **When** `SaveAsync` executes
- **Then** the persisted `Scanner.Mode` value is determined by the combination:

| Detected Method | `EnableUsbScannerInput` | Saved `Scanner.Mode` |
|-----------------|-------------------------|----------------------|
| Camera | false | `"Camera"` |
| Camera | true | `"Both"` |
| CameraWithUsbFallback | false | `"Camera"` |
| CameraWithUsbFallback | true | `"Both"` |
| UsbScanner | false | `"USB"` |
| UsbScanner | true | `"USB"` (no cameras to enable; checkbox effectively no-op) |
| None | (any) | `"USB"` (existing fallback default) |

- **And** the legacy collapse of `CameraWithUsbFallback` → `"Camera"` is preserved when checkbox is unchecked

### AC5: Hint Banner When Both Detected (Opt-In)

- **Given** `IDeviceDetectionService.DetectDevicesAsync` returns `CameraWithUsbFallback`
- **When** the wizard finishes detection during `InitializeAsync`
- **Then** `EnableUsbScannerInput` remains **unchecked by default** (the admin must explicitly opt in)
- **And** the detected-devices banner text reads "Detected: webcam + USB scanner. Tick the option below to use both at the same time."
- **And** ticking the checkbox is the only path that flips `Scanner.Mode` to `"Both"` — there is no silent migration of existing prefs

### AC6: Checkbox Disabled When Only USB Detected

- **Given** the device detection result is `UsbScanner` (no cameras)
- **When** the wizard renders
- **Then** the "Also accept USB scanner input" checkbox is disabled (greyed out)
- **And** a helper line clarifies: "Add a webcam to enable concurrent input"

### AC7: No Hidden Mode Migration

- **Given** an existing install with `Scanner.Mode = "Camera"` or `"USB"` (pre-EP0012)
- **When** the user opens the setup wizard but does not save
- **Then** `Scanner.Mode` is not modified (no auto-migration)
- **And** the user must explicitly check the box and save to opt into `"Both"` mode

### AC8: Edit Mode Preserves Other Settings

- **Given** an install with `Scanner.Mode = "Both"` is opened in edit mode
- **When** the user unchecks the box and saves
- **Then** `Scanner.Mode` becomes `"Camera"` (or `"USB"` if no cameras configured)
- **And** all other preferences (camera slots, scan type, server URL, secrets) are preserved unchanged

---

## Scope

### In Scope

- New `EnableUsbScannerInput` observable property in `SetupViewModel`
- XAML checkbox in `SetupPage.xaml` Scanner Configuration card with helper text
- Initialization logic in `InitializeAsync` to read existing `Scanner.Mode` and pre-populate the checkbox
- Save logic in `SaveAsync` to resolve detected method × checkbox into the correct mode value
- Auto-suggestion when both camera and USB detected (`CameraWithUsbFallback`)
- Disabled state when no cameras present
- Unit tests for the save-logic table in AC4
- Manual UAT script for setup wizard concurrent flow

### Out of Scope

- Runtime concurrent operation (US0121)
- USB indicator slot UI on main page (US0123)
- Migration script for existing `Scanner.Mode` values (none needed — `Both` is opt-in)
- Per-device USB scanner profile selection (e.g., scanner model dropdown)

---

## Technical Notes

### SetupViewModel Changes

New observable property:
```csharp
[ObservableProperty] private bool _enableUsbScannerInput;
public bool CanEnableUsb => DetectedScanMethod is ScanningMethod.Camera or ScanningMethod.CameraWithUsbFallback;
```

`partial void OnDetectedScanMethodChanged(ScanningMethod value)` — does **not** auto-check `EnableUsbScannerInput` (opt-in). Only updates the banner text when the result is `CameraWithUsbFallback` to hint at the concurrent option. Disables the checkbox when detection result is `UsbScanner` only (no cameras to combine with).

`InitializeAsync` (edit mode branch):
```csharp
EnableUsbScannerInput = _preferences.GetScanMode() == "Both";
```

`SaveAsync` mode resolution:
```csharp
_preferences.SetScanMode(ResolveScanMode(DetectedScanMethod, EnableUsbScannerInput));

private static string ResolveScanMode(ScanningMethod detected, bool enableUsb) => detected switch
{
    ScanningMethod.Camera or ScanningMethod.CameraWithUsbFallback when enableUsb => "Both",
    ScanningMethod.Camera or ScanningMethod.CameraWithUsbFallback => "Camera",
    ScanningMethod.UsbScanner => "USB",
    _ => "USB"
};
```

### XAML Changes

In the existing Scanner Configuration card on `SetupPage.xaml`, add (placed after the Camera Picker block):

```xml
<HorizontalStackLayout Spacing="10" IsVisible="{Binding CanEnableUsb}">
    <CheckBox IsChecked="{Binding EnableUsbScannerInput, Mode=TwoWay}"
              Color="#4D9B91" />
    <VerticalStackLayout>
        <Label Text="Also accept USB scanner input alongside cameras"
               FontSize="13"
               FontAttributes="Bold"
               TextColor="#333333" />
        <Label Text="Enable this if you have both webcam(s) and a USB barcode scanner at this gate"
               FontSize="12"
               TextColor="#666666" />
    </VerticalStackLayout>
</HorizontalStackLayout>
```

### Detection Hint

Update the banner text shown when both detected to invite the admin to opt in (without auto-checking):
- "Detected: 2 webcam(s) + USB scanner. Tick the option below to use both at the same time."

Wording for the checkbox itself uses "USB scanner" as the primary term (matches existing `Scanner.Mode = "USB"` preference key and persona docs):
- **Checkbox label:** "Also accept USB scanner input alongside cameras"
- **Helper sub-line:** "Enable this if you have both webcam(s) and a handheld barcode scanner at this gate"

### Files Likely Touched

- `SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs` — new property, init logic, save logic
- `SmartLog.Scanner/Views/SetupPage.xaml` — checkbox UI
- `SmartLog.Scanner.Tests/ViewModels/SetupViewModelTests.cs` — table-driven tests for save resolution

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Detection returns `CameraWithUsbFallback` and admin leaves the box unchecked (default) | Saved as `"Camera"` (admin did not opt in to concurrent mode) |
| Detection returns `CameraWithUsbFallback` and admin ticks the box | Saved as `"Both"` (concurrent mode enabled) |
| Detection returns `UsbScanner` and admin checks the box | Checkbox is disabled per AC6 — interaction not possible; if forced via debug, save still resolves to `"USB"` |
| Detection returns `None` | Save defaults to `"USB"` per existing logic; checkbox disabled |
| Admin opens edit mode, the previously-saved mode is `"Both"`, but no USB scanner is currently detected at re-open | Checkbox renders checked (preserves saved intent); saving without changes keeps `"Both"` |
| Detection runs but cameras are connected after detection | Re-running setup is required for the new camera to be enumerated; checkbox state at save time uses current `DetectedScanMethod` |
| Existing install upgrades to v2.2.0 with `Scanner.Mode = "Camera"` | Behavior unchanged; checkbox renders unchecked in edit mode; no auto-migration |
| Saving when test connection has not been run | Existing validation rules apply (URL + API key required); checkbox state has no impact on validation |

---

## Test Scenarios

- [ ] `EnableUsbScannerInput` defaults to false on fresh install
- [ ] `EnableUsbScannerInput` stays false (opt-in) when detection returns `CameraWithUsbFallback` — only the banner text changes
- [ ] `CanEnableUsb` returns true for `Camera` and `CameraWithUsbFallback` detection results
- [ ] `CanEnableUsb` returns false for `UsbScanner` and `None` detection results
- [ ] In edit mode with `Scanner.Mode = "Both"`, checkbox initializes to checked
- [ ] In edit mode with `Scanner.Mode = "Camera"`, checkbox initializes to unchecked
- [ ] Save with detection=`Camera` + checkbox=true → `Scanner.Mode = "Both"`
- [ ] Save with detection=`Camera` + checkbox=false → `Scanner.Mode = "Camera"`
- [ ] Save with detection=`CameraWithUsbFallback` + checkbox=true → `Scanner.Mode = "Both"`
- [ ] Save with detection=`CameraWithUsbFallback` + checkbox=false → `Scanner.Mode = "Camera"` (legacy collapse preserved)
- [ ] Save with detection=`UsbScanner` (any checkbox state) → `Scanner.Mode = "USB"`
- [ ] Save with detection=`None` → `Scanner.Mode = "USB"`
- [ ] Edit mode save preserves all other prefs (camera slots, server URL, secrets, scan type)

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0004 | Blocked By | Setup wizard page foundation | Done |
| US0071 | Blocked By | Multi-camera setup configuration card | Done |
| US0121 | Related | Runtime support for `Scanner.Mode = "Both"` (must ship together for the setting to actually do anything) | Draft |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| MAUI CheckBox control | Platform UI | Available |
| `IDeviceDetectionService.DetectDevicesAsync` | Internal | Available |

---

## Estimation

**Story Points:** 3
**Complexity:** Low

**Rationale:** Pure UI + ViewModel work. The resolve logic is a small switch expression with deterministic outputs. No platform-specific code, no concurrent state, no hardware dependencies. Most of the effort is writing the lookup-table tests to lock in AC4's behavior.

---

## Open Questions

- [x] **Resolved 2026-04-28:** Opt-IN default — checkbox stays unchecked even when both inputs are detected. The banner text invites the admin to enable concurrent mode, but no preference flips without an explicit tick. Preserves the principle "no hidden mode migration on upgrade."
- [x] **Resolved 2026-04-28:** Wording — "USB scanner" as primary term (matches `Scanner.Mode = "USB"` and existing setup page banner), with "handheld barcode scanner" used in the friendly helper sub-line below the checkbox label.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-28 | SDLC Studio | Initial story created under EP0012 |
| 2026-04-28 | SDLC Studio | Open questions resolved — AC5 flipped from opt-out to opt-in default; wording locked as "USB scanner" primary, "handheld barcode scanner" in helper line |
