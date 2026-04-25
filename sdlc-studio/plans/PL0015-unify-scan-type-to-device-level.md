# PL0015: Unify Scan Type to Device-Level (Deprecate Per-Camera Scan Type)

> **Status:** Complete
> **Story:** [US0089: Unify Scan Type to Device-Level](../stories/US0089-unify-scan-type-to-device-level.md)
> **Epic:** EP0011: Multi-Camera Scanning
> **Created:** 2026-04-25
> **Language:** C# 12 / .NET MAUI 8.0

## Overview

Collapse the per-camera `ScanType` preference (US0069) to a single device-level `Scanner.ScanType`. Remove per-camera Scan Type pickers from the Setup page. Run a one-time migration at startup to carry forward existing per-camera values. Simplify `MultiCameraManager.UpdateScanTypes()` to propagate the single device value. The `SetScanTypeOverride` runtime mechanism on each worker is retained — only the per-camera *storage* and *per-camera picker UI* are removed.

---

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | Device-level only | Only `Scanner.ScanType` preference read/written; no `MultiCamera.{n}.ScanType` |
| AC2 | Single toggle updates all | ENTRY/EXIT toggle on main page updates `Scanner.ScanType` and propagates to all workers |
| AC3 | Setup page — no per-camera picker | Single device-level Scan Type picker; no per-camera rows |
| AC4 | Migration | Existing per-camera prefs migrated to device-level on first launch |
| AC5 | Scan payload correct | Submission uses device-level type regardless of which camera decoded the QR |
| AC6 | Persists across restarts | `Scanner.ScanType` persisted; app reopens in the last-used mode |
| AC7 | Deprecate old code paths | Per-camera Preferences reads/writes removed; `SetScanTypeOverride` retained |

---

## Technical Context

### Language & Framework
- **Primary Language:** C# 12 / .NET MAUI 8.0
- **Architecture:** MVVM; `MultiCameraManager` orchestrates workers; `Preferences` API for persistence
- **Test Framework:** xUnit + NSubstitute/Moq

### Key Existing Files
- `SmartLog.Scanner/ViewModels/MainViewModel.cs` — `ToggleScanType` command; writes per-camera prefs in US0069
- `SmartLog.Scanner.Core/Services/MultiCameraManager.cs` — `UpdateScanTypes()` iterates cameras and calls `SetScanTypeOverride`
- `SmartLog.Scanner/Pages/SetupPage.xaml(.cs)` — per-camera Scan Type picker rows
- `SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs` — per-camera `ScanType` properties
- `SmartLog.Scanner.Core/ViewModels/CameraSlotViewModel.cs` — scan type badge display
- `App.xaml.cs` — startup sequence

### Preferences Key Convention
- Old keys: `MultiCamera.0.ScanType`, `MultiCamera.1.ScanType`, …
- New key: `Scanner.ScanType` (values: `"ENTRY"` or `"EXIT"`, default `"ENTRY"`)

---

## Recommended Approach

**Strategy:** Test-After  
**Rationale:** Code simplification + migration. Tests cover migration scenarios and device-level propagation logic.

---

## Implementation Phases

### Phase 1: Migration Service

**Goal:** One-shot, idempotent migration that converts any existing per-camera values.

- [ ] Create `SmartLog.Scanner.Core/Services/ScanTypeMigrationService.cs`:
  ```csharp
  public class ScanTypeMigrationService {
      private const string DeviceKey = "Scanner.ScanType";
      private const string MigrationDoneKey = "Migration.ScanTypeUnified";

      public void MigrateIfNeeded() {
          if (Preferences.Get(MigrationDoneKey, false)) return;

          // Collect all per-camera values
          var perCamera = new List<string>();
          for (int i = 0; i < 8; i++) {
              var key = $"MultiCamera.{i}.ScanType";
              if (Preferences.ContainsKey(key)) {
                  perCamera.Add(Preferences.Get(key, "ENTRY"));
                  Preferences.Remove(key);
              }
          }

          // Determine device-level value
          if (!Preferences.ContainsKey(DeviceKey)) {
              var distinct = perCamera.Distinct().ToList();
              var unified = distinct.Count == 1 ? distinct[0] : "ENTRY";
              Preferences.Set(DeviceKey, unified);

              if (distinct.Count > 1) {
                  // Notify user once — store a one-time notification flag
                  Preferences.Set("Migration.ScanTypeUnifiedNotify", true);
              }
          }

          Preferences.Set(MigrationDoneKey, true);
      }
  }
  ```
- [ ] Register as singleton in `MauiProgram.cs` (or DI setup).

**Files:** `SmartLog.Scanner.Core/Services/ScanTypeMigrationService.cs`, `MauiProgram.cs`

### Phase 2: Startup — Run Migration

**Goal:** Migration runs before `MultiCameraManager.InitializeAsync`.

- [ ] In `App.xaml.cs` (or `AppShell` startup):
  ```csharp
  _scanTypeMigration.MigrateIfNeeded();
  await _multiCameraManager.InitializeAsync();
  ```
- [ ] If `Migration.ScanTypeUnifiedNotify` is set, surface a one-time toast/snackbar: "Scan Type unified to device-level — verify in Setup."

**Files:** `App.xaml.cs`

### Phase 3: MainViewModel — Device-Level Toggle

**Goal:** Toggle reads and writes only `Scanner.ScanType`.

- [ ] In `MainViewModel.cs`, simplify `ToggleScanType`:
  ```csharp
  [RelayCommand]
  private void ToggleScanType() {
      var current = Preferences.Get("Scanner.ScanType", "ENTRY");
      var next = current == "ENTRY" ? "EXIT" : "ENTRY";
      Preferences.Set("Scanner.ScanType", next);
      _multiCameraManager.UpdateScanTypes(next);
      OnPropertyChanged(nameof(ScanTypeLabel));
  }
  ```
- [ ] Remove any loop writing `MultiCamera.{n}.ScanType`.

**Files:** `SmartLog.Scanner/ViewModels/MainViewModel.cs`

### Phase 4: MultiCameraManager — Simplified UpdateScanTypes

**Goal:** Propagate single device value to all workers.

- [ ] In `MultiCameraManager.cs`, simplify `UpdateScanTypes`:
  ```csharp
  public void UpdateScanTypes(string scanType) {
      foreach (var worker in _activeWorkers.Values)
          worker.SetScanTypeOverride(scanType);
  }
  ```
- [ ] Remove any per-camera Preferences reads inside this method.
- [ ] `InitializeAsync`: read `Scanner.ScanType` once and pass to each worker on startup.

**Files:** `SmartLog.Scanner.Core/Services/MultiCameraManager.cs`

### Phase 5: Setup Page — Remove Per-Camera Picker

**Goal:** No per-camera Scan Type picker; single device-level picker.

- [ ] In `SetupPage.xaml`:
  - Remove `ScanType` picker from each camera row template.
  - Add a single "Scan Type" picker in the device-level / gateway settings section.
- [ ] In `SetupViewModel.cs`:
  - Remove per-camera `ScanType` properties.
  - Add `DeviceScanType` property bound to `Scanner.ScanType` preference.
- [ ] `CameraSlotViewModel`: scan type badge derives from `DeviceScanType` (or subscribe to a shared observable); no per-slot override.

**Files:** `SmartLog.Scanner/Pages/SetupPage.xaml(.cs)`, `SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs`, `CameraSlotViewModel.cs`

### Phase 6: Tests

| AC | Test | File |
|----|------|------|
| AC4 | Migration from uniform per-camera values → carries value forward | `ScanTypeMigrationServiceTests.cs` |
| AC4 | Migration from mixed per-camera values → defaults to ENTRY + notification flag | same |
| AC4 | Migration is idempotent (re-run doesn't change result) | same |
| AC2 | `UpdateScanTypes` propagates to all active workers | `MultiCameraManagerTests.cs` |
| AC5 | Scan submission uses device-level value | `CameraQrScannerServiceTests.cs` |

- [ ] Run `dotnet test`; confirm zero regressions.
- [ ] Drop obsolete per-camera scan type tests from the test suite.

---

## Edge Case Handling

| # | Edge Case | Handling |
|---|-----------|----------|
| 1 | Fresh install, no prior prefs | Default `Scanner.ScanType = "ENTRY"` |
| 2 | Mixed per-camera values | Migration defaults to ENTRY + sets notification flag |
| 3 | `Scanner.ScanType` already set before migration | Migration does not overwrite it; only removes per-camera keys |
| 4 | Scan in flight during toggle | Scan submits with value current at decode time — acceptable |

---

## Definition of Done

- [ ] `ScanTypeMigrationService` created; migration runs at startup
- [ ] Per-camera Preferences keys removed from all code paths
- [ ] `UpdateScanTypes` simplified to single device value propagation
- [ ] Setup page shows single device-level picker; no per-camera pickers
- [ ] `MainViewModel.ToggleScanType` writes only `Scanner.ScanType`
- [ ] One-time migration notification shown when values were mixed
- [ ] Tests passing; `dotnet test` clean

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-25 | Claude | Initial plan |
