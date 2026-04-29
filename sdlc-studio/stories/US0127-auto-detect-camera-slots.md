# US0127: Auto-Detect Camera Slots with Device-Appended Names

> **Status:** Done
> **Epic:** [EP0012: Concurrent Multi-Modal Scanning](../epics/EP0012-concurrent-multi-modal-scanning.md)
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-04-29

## User Story

**As** IT Admin Ian and Guard Gary
**I want** the scanner app to automatically detect and create one camera slot per connected camera device, named "Camera N – {Device Name}"
**So that** no one needs to manually configure how many cameras to use or which device maps to which slot — the gate is ready the moment the app opens

## Context

### Persona Reference

**IT Admin Ian** — Deploys and maintains scanner hardware across multiple school gates. Today's setup wizard requires him to: (1) set a camera count via a Stepper, (2) open each slot's device Picker and select the right camera. On a 3-camera gate that's 4 manual interactions before scanning can begin. He wants to plug in cameras and walk away.
[Full persona details](../personas.md#it-admin-ian)

**Guard Gary** — Operates the scanner during student arrival. If Ian hasn't configured the wizard perfectly, Gary sees blank Picker dropdowns and must navigate the setup wizard himself. This story removes that failure mode entirely.
[Full persona details](../personas.md#guard-gary)

### Background

US0126 established the ID-card style station cards. US0122 added the camera count Stepper (1–8, later capped at 3 by a recent fix). Currently the setup wizard requires:

1. A **Stepper** to manually pick how many cameras to use
2. A **device Picker** per camera slot to assign which physical device maps to that slot

Both steps are unnecessary because the OS already knows which cameras are connected. The app should enumerate them, assign in detection order, and name them automatically. The Stepper and per-slot device Picker should be removed entirely from the setup wizard.

Auto-name format: `"Camera N – {device.Name}"` where N is 1-based slot index and `device.Name` is `CameraDeviceInfo.Name` (macOS: `AVCaptureDevice.LocalizedName`; Windows: `DeviceInformation.Name`). Example: `"Camera 1 – FaceTime HD Camera"`.

The display name remains user-editable in the setup wizard so guards can rename stations to meaningful labels (e.g., `"Entrance Cam 1"`).

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| EP0012 | Architecture | Camera slots driven by `CameraSlots: ObservableCollection<CameraSlotState>` in `MainViewModel` | Auto-created slots must still produce valid `CameraSlotState` instances — same as manually-created ones |
| PRD | UX | Zero decision-making during scanning for Guard Gary | After this story, Guard Gary should never need to touch the setup wizard for camera configuration |
| TRD | Platform | `CameraDeviceInfo(Id, Name)` is the enumeration primitive on both platforms | Auto-name uses `Name`; `Id` is still used internally for the `MultiCameraManager` worker binding |
| US0126 | UI | Station cards sized by `CardWidth`/`CardHeight` from `MainViewModel` | No change — auto-created slots reuse the same card layout |

---

## Acceptance Criteria

### AC1: Slots auto-created from detected devices

- **Given** the app starts or the setup wizard opens
- **When** `LoadCamerasAsync()` runs and N cameras are detected (N ≤ 3)
- **Then** exactly N `CameraSlotViewModel` instances are created, each bound to the Nth detected `CameraDeviceInfo` in enumeration order — no user action required

### AC2: Auto-name includes slot number and device model

- **Given** detected cameras are `["FaceTime HD Camera", "Logitech C920"]`
- **When** slots are auto-created
- **Then** slot display names default to `"Camera 1 – FaceTime HD Camera"` and `"Camera 2 – Logitech C920"` respectively, and these names appear in the card bottom strip on the main page

### AC3: Display name is user-editable and persists

- **Given** a slot's auto-name is `"Camera 1 – FaceTime HD Camera"`
- **When** IT Admin Ian edits the name to `"Entrance Cam 1"` in the setup wizard and saves
- **Then** the main page card shows `"Entrance Cam 1"` and the same name is restored on next app launch without prompting re-entry

### AC4: Auto-name is restored when saved name is blank

- **Given** a slot has a saved display name
- **When** the user clears the name field and saves
- **Then** the name reverts to the auto-name `"Camera N – {device.Name}"` rather than leaving the strip blank

### AC5: Setup wizard shows a read-only detected-camera list

- **Given** cameras are detected
- **When** the user opens the Camera Configuration section of the setup wizard
- **Then** a read-only label shows "N camera(s) detected" and each camera row shows its auto-name with an editable display-name field — **no Stepper and no device Picker are present**

### AC6: Hard cap of 3 slots regardless of connected devices

- **Given** 4 or more cameras are detected by the OS
- **When** slots are auto-created
- **Then** only the first 3 (by detection order) are used; a note in the setup wizard informs the user that additional cameras beyond 3 are ignored

### AC7: Zero cameras → no camera slots, no crash

- **Given** no cameras are detected (USB-only setup)
- **When** the app starts
- **Then** `CameraSlots` is empty, the camera configuration section in the setup wizard shows "No cameras detected", and the app continues in USB-only mode without error

---

## Scope

### In Scope
- Remove camera count `Stepper` and `MaxCameraCount` property from `SetupViewModel`
- Remove per-slot device `Picker` from `SetupPage.xaml` DataTemplate
- Auto-create slots in `LoadCamerasAsync()` in detection order (max 3)
- Auto-name logic: `"Camera {N} – {device.Name}"`; fallback to auto-name when saved name is blank
- Save/restore per-slot display name by slot index via `PreferencesService`
- Update `SetupPage.xaml` Camera Configuration section: read-only detected count + editable name fields only
- Keep per-slot enable/disable toggle (useful for temporarily disabling a camera without unplugging)

### Out of Scope
- Manual device assignment (removed entirely)
- Changing the detection order / slot assignment (always enumeration order)
- Device health indicators in the setup wizard (existing `TestCamera` button per slot remains but Picker removal means it always tests the auto-assigned device)
- Multi-camera preview (Camera 0 preview only — unchanged from current behaviour)
- Windows-side behaviour changes beyond what is already implemented in `CameraEnumerationService`

---

## Technical Notes

### Files to change

| File | Change |
|------|--------|
| `SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs` | Remove `CameraCount`, `MaxCameraCount`; rewrite `LoadMultiCameraConfig` → inline into `LoadCamerasAsync`; add auto-name logic |
| `SmartLog.Scanner.Core/ViewModels/CameraSlotViewModel.cs` | Remove `AvailableDevices` collection and `PopulateDevices()` (no longer needed); keep `DisplayName`, `IsEnabled`, `SelectedDevice` (read-only, set by VM) |
| `SmartLog.Scanner/Views/SetupPage.xaml` | Remove Stepper block; replace device Picker in DataTemplate with a read-only device label; keep name entry + enable toggle |
| `SmartLog.Scanner/Views/SetupPage.xaml.cs` | Remove `ForceRefreshSelections` call (Picker gone) |
| `SmartLog.Scanner.Core/Services/PreferencesService` | Verify `GetCameraName` / `SetCameraName` keyed by slot index still works; `GetCameraDeviceId` / `SetCameraDeviceId` no longer written by setup (can be kept for `MultiCameraManager` but not user-set) |

### Auto-name logic

```csharp
// In LoadCamerasAsync(), after enumeration:
for (var i = 0; i < Math.Min(cameras.Count, 3); i++)
{
    var device = cameras[i];
    var savedName = _preferences.GetCameraName(i);
    var autoName = $"Camera {i + 1} – {device.Name}";  // en-dash
    var slot = new CameraSlotViewModel(i, _cameraEnumeration, logger)
    {
        SelectedDevice = device,
        IsEnabled = _preferences.GetCameraEnabled(i),
        DisplayName = string.IsNullOrWhiteSpace(savedName) ? autoName : savedName,
    };
    CameraSlots.Add(slot);
}
```

### Save logic on "Save Changes"

```csharp
// In SaveMultiCameraConfig():
for (var i = 0; i < CameraSlots.Count; i++)
{
    var slot = CameraSlots[i];
    var autoName = $"Camera {i + 1} – {slot.SelectedDevice?.Name}";
    // Persist blank-resistant: if user clears name, store empty so auto-name is reapplied on next load
    _preferences.SetCameraName(i, string.IsNullOrWhiteSpace(slot.DisplayName) ? string.Empty : slot.DisplayName);
    _preferences.SetCameraEnabled(i, slot.IsEnabled);
    // Device ID is always detection-order — still save for MultiCameraManager
    _preferences.SetCameraDeviceId(i, slot.SelectedDevice?.Id ?? string.Empty);
}
```

### Setup wizard Camera Configuration section (revised layout)

```
Camera Configuration
────────────────────────────────────────────
2 camera(s) detected                          ← read-only label

  Camera 1  [toggle: Enabled]
  Device:   FaceTime HD Camera                ← read-only device label
  Name:     [Entrance Cam 1          ]        ← editable Entry
  [Test]

  Camera 2  [toggle: Enabled]
  Device:   Logitech C920                     ← read-only device label
  Name:     [Camera 2 – Logitech C920]        ← editable Entry
  [Test]
────────────────────────────────────────────
```

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|--------------------|
| 0 cameras detected | `CameraSlots` empty; setup wizard shows "No cameras detected"; USB-only mode continues normally |
| 4+ cameras detected | First 3 used; setup wizard shows "3 of N camera(s) used (max 3)" |
| Saved name is blank or whitespace | Auto-name `"Camera N – {device.Name}"` applied on load |
| Camera present at setup but absent at next launch | `MultiCameraManager` handles missing device (existing auto-recovery); slot is still created but worker fails gracefully |
| All camera names are identical (two same-model webcams) | Slot number differentiates: `"Camera 1 – Logitech C920"` vs `"Camera 2 – Logitech C920"` |
| Device name is unusually long (>40 chars) | Bottom strip uses existing `TailTruncation`; no special truncation needed in ViewModel |

---

## Test Scenarios

- [ ] 1 camera detected → 1 slot created, named `"Camera 1 – {name}"`, no Stepper visible
- [ ] 2 cameras detected → 2 slots created in enumeration order, names correct
- [ ] 3 cameras detected → 3 slots, no 4th slot created
- [ ] 4 cameras detected → 3 slots (cap enforced), setup wizard note shown
- [ ] 0 cameras detected → 0 slots, no crash, USB-only mode active
- [ ] User edits display name → persists across setup wizard re-open
- [ ] User clears display name → auto-name restored on re-open
- [ ] Card bottom strip shows saved display name (not auto-name) after rename
- [ ] `ForceRefreshSelections` removed from `OnAppearing` (no Picker to refresh)
- [ ] `AvailableDevices` / `PopulateDevices` no longer exist in `CameraSlotViewModel`

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| [US0126](US0126-id-card-style-station-cards.md) | Predecessor | ID-card layout for station cards (bottom strip DisplayName) | Done |
| [US0122](US0122-setup-wizard-concurrent-mode-config.md) | Predecessor | Camera slot ViewModel + SetupViewModel structure | Done |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| `ICameraEnumerationService.GetAvailableCamerasAsync()` | Platform API | Available (macOS + Windows) |
| `IPreferencesService.GetCameraName / SetCameraName` | Persistence | Available |

---

## Estimation

**Story Points:** 3
**Complexity:** Low — removes more code than it adds; main work is pruning the Stepper/Picker from XAML and simplifying `SetupViewModel`

---

## Open Questions

- [x] Should the enable/disable toggle per slot be kept or also removed? **Decision: keep** — IT Admin may want to disable a camera that's physically attached but pointed the wrong way.
- [x] Should `TestCamera` button remain in the setup wizard without a device Picker? **Decision: keep** — still works since device is auto-assigned.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-29 | AI Assistant | Initial draft |
