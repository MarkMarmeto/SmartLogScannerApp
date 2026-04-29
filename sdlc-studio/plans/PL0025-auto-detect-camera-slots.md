# PL0025: Auto-Detect Camera Slots — Implementation Plan

> **Status:** Done
> **Story:** [US0127: Auto-Detect Camera Slots with Device-Appended Names](../stories/US0127-auto-detect-camera-slots.md)
> **Epic:** [EP0012: Concurrent Multi-Modal Scanning](../epics/EP0012-concurrent-multi-modal-scanning.md)
> **Created:** 2026-04-29
> **Language:** C# / MAUI XAML

---

## Overview

Replace the manual camera-count Stepper and per-slot device Picker with fully automatic slot creation. On `LoadCamerasAsync()`, the app enumerates physical cameras (up to 3), creates one `CameraSlotViewModel` per device in detection order, and names each slot `"Camera N – {device.Name}"`. Blank saved names fall back to the auto-name. The setup wizard's Camera Configuration section becomes a read-only detected-camera list with editable display names, an enable/disable toggle, and a Test button per slot.

Net code change is **negative** (more deletions than additions): the Stepper, `MaxCameraCount`, `CameraCount`, `OnCameraCountChanged`, `LoadMultiCameraConfig`, `CreateSlot`, `ForceRefreshSelections`, `AvailableDevices`, `PopulateDevices`, and `ForceRefreshSelection` are all removed.

---

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | Auto-create slots | N cameras detected → N slots, detection order, no user action |
| AC2 | Auto-name with device model | `"Camera N – {device.Name}"` as default display name |
| AC3 | Display name persists | Editable name saved per slot index; restored on re-open |
| AC4 | Blank name reverts to auto-name | Empty saved name → auto-name applied on load |
| AC5 | Setup wizard read-only list | Detected count label; no Stepper; no device Picker |
| AC6 | Hard cap at 3 | 4+ cameras detected → first 3 used; note shown |
| AC7 | Zero cameras → no crash | Empty `CameraSlots`, USB-only mode continues |

---

## Technical Context

### Language & Framework
- **Primary Language:** C# 12 / .NET 8
- **Framework:** .NET MAUI (net8.0-maccatalyst / net8.0-windows10.0.19041.0)
- **Test Framework:** xUnit + Moq
- **MVVM:** CommunityToolkit.Mvvm (source generators)

### Relevant Best Practices
- `[ObservableProperty]` backing fields must not be accessed directly except where bypassing `partial void OnXChanged` is intentional (document with a comment)
- Remove dead `[ObservableProperty]` fields — source generator produces warnings for unused bindings
- XAML bindings to removed properties must be deleted; leftover bindings cause runtime `BindingExpression` warnings that silently fail
- `CameraSlotViewModel` is used as `x:DataType` in a DataTemplate — any removed public property must also be removed from XAML or the XAML compiler throws at build time

### Existing Patterns
- `LoadCamerasAsync()` in `SetupViewModel` is the single entry point for camera enumeration — keep and simplify
- `SaveMultiCameraConfig()` is called from `SaveAsync()` — keep, simplify to drop `CameraCount`
- `PreferencesService.GetCameraName(i)` / `SetCameraName(i)` key by slot index — unchanged
- `NullLogger<T>.Instance` used in `CreateSlot` — retain pattern for `CameraSlotViewModel` construction

---

## Recommended Approach

**Strategy:** Test-After
**Rationale:** This is primarily a deletion + simplification. The core logic (`LoadCamerasAsync` auto-name) is straightforward; existing tests for `PreferencesService` and `OfflineQueueService` are unaffected. Writing unit tests after implementation is faster here since no new algorithmic complexity is introduced.

### Test Priority
1. `SetupViewModel` auto-slot creation: correct count, correct device assignment, correct auto-name
2. Blank/whitespace saved name → auto-name fallback
3. 4+ cameras → cap at 3, note visible

---

## Implementation Tasks

| # | Task | File | Depends On | Status |
|---|------|------|------------|--------|
| 1 | Remove `AvailableDevices`, `PopulateDevices`, `ForceRefreshSelection` from `CameraSlotViewModel` | `Core/ViewModels/CameraSlotViewModel.cs` | — | [x] |
| 2 | Add `DeviceName` computed property to `CameraSlotViewModel` | `Core/ViewModels/CameraSlotViewModel.cs` | 1 | [x] |
| 3 | Remove Stepper/picker VM properties from `SetupViewModel` | `Core/ViewModels/SetupViewModel.cs` | 1 | [x] |
| 4 | Rewrite `LoadCamerasAsync()` with auto-slot logic | `Core/ViewModels/SetupViewModel.cs` | 3 | [x] |
| 5 | Simplify `SaveMultiCameraConfig()` (drop `CameraCount`) | `Core/ViewModels/SetupViewModel.cs` | 3 | [x] |
| 6 | Remove `ForceRefreshSelections` call from `OnAppearing` | `Scanner/Views/SetupPage.xaml.cs` | 3 | [x] |
| 7 | Replace Stepper block + device Picker in `SetupPage.xaml` | `Scanner/Views/SetupPage.xaml` | 3 | [x] |
| 8 | Write unit tests for auto-slot creation | `Scanner.Tests/` | 4 | [x] |

### Parallel Execution Groups

| Group | Tasks | Prerequisite |
|-------|-------|--------------|
| A | 1, 2 | — |
| B | 3, 4, 5 | A (CameraSlotViewModel must be stable first) |
| C | 6, 7 | B |
| D | 8 | B |

---

## Implementation Phases

### Phase 1: Trim `CameraSlotViewModel`
**Goal:** Remove all picker-related members; expose `DeviceName` for the read-only label.

- [x] Delete `ObservableCollection<CameraDeviceInfo> AvailableDevices`
- [x] Delete `PopulateDevices(IEnumerable<CameraDeviceInfo>)` method
- [x] Delete `ForceRefreshSelection()` method
- [x] Add `public string DeviceName => SelectedDevice?.Name ?? "Unknown device";`
- [x] Add `[NotifyPropertyChangedFor(nameof(DeviceName))]` to `_selectedDevice` field

**Files:**
- `SmartLog.Scanner.Core/ViewModels/CameraSlotViewModel.cs`

**Before (key lines):**
```csharp
public ObservableCollection<CameraDeviceInfo> AvailableDevices { get; } = new();

public void PopulateDevices(IEnumerable<CameraDeviceInfo> devices) { ... }

public void ForceRefreshSelection() { ... }
```

**After:**
```csharp
// AvailableDevices, PopulateDevices, ForceRefreshSelection — deleted

public string DeviceName => SelectedDevice?.Name ?? "Unknown device";

[ObservableProperty]
[NotifyPropertyChangedFor(nameof(SelectedDevice))]
[NotifyPropertyChangedFor(nameof(DeviceName))]         // ← add this
private CameraDeviceInfo? _selectedDevice;
```

---

### Phase 2: Simplify `SetupViewModel`
**Goal:** Remove Stepper/picker ViewModel state; rewrite `LoadCamerasAsync` to auto-create slots.

#### 2a — Remove dead properties

Delete the following `[ObservableProperty]` fields and related members:

| Symbol | Reason |
|--------|--------|
| `_maxCameraCount` / `MaxCameraCount` | No Stepper → no max |
| `_cameraCount` / `CameraCount` / `OnCameraCountChanged` | No Stepper |
| `ShowUsb3Warning` | No Stepper (banner removed) |
| `_allAvailableCameras` | Inlined into `LoadCamerasAsync` |
| `CreateSlot()` | Inlined into `LoadCamerasAsync` |
| `LoadMultiCameraConfig()` | Replaced by `LoadCamerasAsync` logic |
| `ForceRefreshSelections()` | No Picker to refresh |

Keep (unchanged): `_availableCameras` / `AvailableCameras`, `_selectedCamera` / `SelectedCamera`, `HasCameras`, `CameraPickerMessage` — these back the **single-camera** picker in the Scanner Configuration section (still relevant for single-camera mode backward compatibility).

#### 2b — Rewrite `LoadCamerasAsync()`

```csharp
private async Task LoadCamerasAsync()
{
    if (_cameraEnumeration == null)
    {
        HasCameras = false;
        CameraPickerMessage = "Camera enumeration not available on this platform.";
        return;
    }

    try
    {
        var cameras = await _cameraEnumeration.GetAvailableCamerasAsync();
        AvailableCameras = new ObservableCollection<CameraDeviceInfo>(cameras);
        HasCameras = cameras.Count > 0;

        if (cameras.Count == 0)
        {
            CameraPickerMessage = "No cameras detected. USB scanner will be used.";
            return;
        }

        // Single-camera picker (Scanner Configuration section — backward compat)
        var savedId = _preferences.GetSelectedCameraId();
        SelectedCamera = cameras.FirstOrDefault(c => c.Id == savedId) ?? cameras[0];

        var slotCount = Math.Min(cameras.Count, 3);
        CameraPickerMessage = cameras.Count > 3
            ? $"3 of {cameras.Count} camera(s) detected (max 3)"
            : $"{cameras.Count} camera(s) detected";

        CameraSlots.Clear();
        for (var i = 0; i < slotCount; i++)
        {
            var device = cameras[i];
            var savedName = _preferences.GetCameraName(i);
            var autoName  = $"Camera {i + 1} – {device.Name}";  // en-dash

            var slot = new CameraSlotViewModel(i, _cameraEnumeration,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<CameraSlotViewModel>.Instance)
            {
                SelectedDevice = device,
                IsEnabled      = _preferences.GetCameraEnabled(i),
                DisplayName    = string.IsNullOrWhiteSpace(savedName) ? autoName : savedName,
            };
            CameraSlots.Add(slot);
        }

        _logger.LogInformation("Auto-created {Count} camera slot(s)", slotCount);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Camera enumeration failed");
        CameraPickerMessage = "Could not enumerate cameras.";
        HasCameras = false;
    }
}
```

#### 2c — Simplify `SaveMultiCameraConfig()`

```csharp
private void SaveMultiCameraConfig()
{
    // Camera count is now always derived from detection — do not persist it.
    for (var i = 0; i < CameraSlots.Count; i++)
    {
        var slot = CameraSlots[i];
        _preferences.SetCameraName(i, slot.DisplayName);          // blank = auto-name on next load
        _preferences.SetCameraDeviceId(i, slot.SelectedDevice?.Id ?? string.Empty);
        _preferences.SetCameraEnabled(i, slot.IsEnabled);
    }
}
```

**Files:**
- `SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs`

---

### Phase 3: Update `SetupPage` (XAML + code-behind)
**Goal:** Remove Stepper and device Picker from the wizard; add detected-count label and read-only device label.

#### 3a — `SetupPage.xaml.cs`

Remove the `ForceRefreshSelections` call from `OnAppearing`:

```csharp
// Before:
await Task.Delay(150);
_viewModel.ForceRefreshSelections();

// After: delete both lines
```

#### 3b — `SetupPage.xaml` — Camera Configuration section

**Remove:**
- The entire "Number of Cameras" `VerticalStackLayout` (Stepper + label block, lines ~405–423)
- The "USB 3.0 Warning" `Border` (lines ~425–438)
- The device `Picker` inside the per-camera DataTemplate (lines ~477–483)

**Add / change:**
- Replace the section header `<Label Text="🎥 Camera Configuration" .../>` with a two-line header including detected count:

```xml
<VerticalStackLayout Spacing="2">
    <Label Text="🎥 Camera Configuration"
           FontSize="18" FontAttributes="Bold" TextColor="#2C5F5D" />
    <Label Text="{Binding CameraPickerMessage}"
           FontSize="13" TextColor="#666666" />
</VerticalStackLayout>
```

- Replace device `Picker` in DataTemplate with a read-only device label:

```xml
<!-- Device label (auto-assigned, read-only) -->
<Label Text="{Binding DeviceName, StringFormat='Device: {0}'}"
       FontSize="13"
       TextColor="#666666"
       FontAttributes="Italic" />
```

**Revised DataTemplate per-camera row:**
```
Camera N  [toggle: Enabled]
Device:   FaceTime HD Camera         ← read-only label (DeviceName)
Name:     [editable Entry]           ← DisplayName TwoWay
[Test]   ← test result label
```

**Files:**
- `SmartLog.Scanner/Views/SetupPage.xaml`
- `SmartLog.Scanner/Views/SetupPage.xaml.cs`

---

### Phase 4: Tests
**Goal:** Unit tests for the auto-slot creation logic. New test class in `SmartLog.Scanner.Tests`.

Test scenarios (map to story test checklist):

| Test | Assert |
|------|--------|
| `LoadCamerasAsync_TwoCamerasDetected_CreatesTwoSlots` | `CameraSlots.Count == 2` |
| `LoadCamerasAsync_SlotsAssignedInDetectionOrder` | `CameraSlots[0].SelectedDevice == cameras[0]` etc. |
| `LoadCamerasAsync_AutoNameApplied_WhenSavedNameBlank` | `CameraSlots[0].DisplayName == "Camera 1 – FaceTime HD Camera"` |
| `LoadCamerasAsync_SavedNameRestored_WhenNotBlank` | `CameraSlots[0].DisplayName == "Entrance Cam 1"` |
| `LoadCamerasAsync_FourCamerasDetected_CapsAtThree` | `CameraSlots.Count == 3` |
| `LoadCamerasAsync_NoCamerasDetected_EmptySlotsNoException` | `CameraSlots.Count == 0`, no throw |
| `SaveMultiCameraConfig_DoesNotPersistCameraCount` | `_preferences.SetCameraCount` never called |

**Files:**
- `SmartLog.Scanner.Tests/SetupViewModelAutoDetectTests.cs` (new)

---

## Edge Case Handling Plan

| # | Edge Case (from Story) | Handling Strategy | Phase |
|---|------------------------|-------------------|-------|
| 1 | 0 cameras detected | `HasCameras = false`, `CameraSlots` empty, early return — no crash | Phase 2 |
| 2 | 4+ cameras detected | `Math.Min(cameras.Count, 3)` hard cap; `CameraPickerMessage` shows "3 of N" note | Phase 2 |
| 3 | Saved name blank/whitespace | `string.IsNullOrWhiteSpace(savedName) ? autoName : savedName` in slot init | Phase 2 |
| 4 | Camera absent at next launch | `MultiCameraManager` auto-recovery handles missing device; slot created but worker fails gracefully (pre-existing behaviour) | n/a |
| 5 | Two same-model cameras | Slot number differentiates: `"Camera 1 – Logitech C920"` vs `"Camera 2 – Logitech C920"` | Phase 2 |
| 6 | Very long device name | `TailTruncation` on card bottom strip (pre-existing); no ViewModel truncation needed | n/a |

**Coverage: 6/6 edge cases handled**

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| XAML compiler error if `AvailableDevices` or `CameraCount` binding left in XAML | Build failure | Phase 3 removes all XAML references before build |
| `SelectedCamera` / `SetSelectedCameraId` still used in `SaveAsync` — must not be broken | Single-camera backward compat broken | Keep `SelectedCamera` and its save call unchanged; only multi-camera Stepper bits removed |
| `_preferences.GetCameraCount()` no longer called — stale stored value irrelevant | None | `LoadCamerasAsync` never reads `GetCameraCount()`; old saved value harmlessly orphaned |
| MVVMTK0034 warnings from `_cameraCount` direct field access | Warning noise | `_cameraCount` field and its backing property are deleted entirely in Phase 2 |

---

## Definition of Done

- [x] All 7 ACs implemented and manually verified in running app
- [x] No `Stepper`, no device `Picker` in Camera Configuration section
- [x] Unit tests written and passing (`dotnet test SmartLog.Scanner.Tests`)
- [x] `dotnet build SmartLog.Scanner -f net8.0-maccatalyst` — 0 errors, no new warnings
- [x] `ForceRefreshSelections` and all related code deleted
- [x] `CameraSlots` populated automatically on setup wizard open, no user interaction required

---

## Notes

- The single-camera `Picker` in the **Scanner Configuration** section (`SelectedCamera` / `AvailableCameras`) is **kept** unchanged — it backs the `SetSelectedCameraId` preference used by the app's single-camera scan mode for backward compat.
- `_preferences.SetCameraCount` is no longer called from `SaveMultiCameraConfig` but the method still exists in `IPreferencesService` — do not delete it (may be referenced elsewhere).
- Auto-name uses an **en-dash** (`–`, `–`) not a hyphen-minus (`-`) to match the separator shown in the accepted design example.
