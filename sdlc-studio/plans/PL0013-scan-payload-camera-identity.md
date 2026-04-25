# PL0013: Scan Payload — Camera Identity — Implementation Plan

> **Status:** Complete
> **Story:** [US0090: Scan Payload — Include Camera Index and Camera Name](../stories/US0090-scan-payload-camera-identity.md)
> **Epic:** EP0011: Multi-Camera Scanning (cross-project)
> **Created:** 2026-04-24
> **Language:** C# 12 / .NET MAUI 8

## Overview

Wire camera identity (1-based index + user-assigned name) into the scan submission pipeline and offline queue. The WebApp wire contract was fixed in US0093 (PL0011). This plan handles the scanner side: add `cameraName` to `ScanApiService`, propagate it through `CameraQrScannerService` and `MultiCameraManager`, persist it in the offline queue (`QueuedScan`), pass it through `BackgroundSyncService`, and display it in the in-app Scan Logs.

**Pre-existing state (reduces scope):**
- `ScanApiService.SubmitScanAsync` already accepts `int? cameraIndex`.
- `CameraQrScannerService` already has `_cameraIndex` + `SetCameraIndex`.
- `PreferencesService` already has `GetCameraName(int)` / `SetCameraName(int, string)` using key `MultiCamera.{index}.Name`.
- `SetupViewModel` already reads/writes `CameraSlotViewModel.DisplayName` from/to preferences.
- `ScannerDbContext` uses `EnsureCreated` + explicit DDL in `DatabaseInitializationService` (no EF migrations runner); new columns require `ALTER TABLE` statements there.

**Wire-format 1-based index:** `CameraInstance.Index` is 0-based internally. The WebApp server expects `[1, 8]`. Conversion (0-based → 1-based) happens at the `ScanApiService` layer: `cameraIndex.HasValue ? cameraIndex + 1 : null`.

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | Setup captures camera name | Name input in Setup per camera; persists to prefs |
| AC2 | Scan payload includes both fields | `cameraIndex` (1-based) + `cameraName` sent on every camera-driven scan |
| AC3 | Single-camera + USB fallbacks | Single: index=1, name from prefs; USB: index=null, name="USB Scanner" |
| AC4 | Name changes propagate immediately | Next scan uses current name; no restart needed |
| AC5 | Index stable across slot reorders | Index is slot position, not physical device identity |
| AC6 | Offline queue carries fields | `QueuedScan` stores + `BackgroundSyncService` forwards both fields |
| AC7 | Scan Logs shows camera identity | "Camera {index} — {name}" per row in ScanLogsPage |

---

## Technical Context

### Language & Framework
- **Primary Language:** C# 12 / .NET MAUI 8
- **Architecture:** MVVM (CommunityToolkit.Mvvm); Core services in `SmartLog.Scanner.Core`
- **Test Framework:** xUnit + Moq (`SmartLog.Scanner.Tests`)
- **DB:** SQLite via EF Core (no EF migrations runner — schema changes via `DatabaseInitializationService` DDL)

### Key Existing Patterns
- **Camera scan flow:** `MultiCameraManager` → per-camera `CameraQrScannerService` → `ScanApiService.SubmitScanAsync` (online) or `OfflineQueueService.EnqueueScanAsync` (offline). The index is set on each `CameraQrScannerService` instance at init. Name needs the same treatment.
- **Single-camera mode:** `CameraQrScannerService` is used standalone. `SetCameraIndex` is NOT called (index stays null). Needs to pass `cameraIndex = 1`, name from `GetCameraName(0)` (0-based prefs key for slot 0).
- **USB path:** `MainViewModel` processes keystrokes in USB mode and calls `_scanApi.SubmitScanAsync` directly. USB: `cameraIndex = null`, `cameraName = "USB Scanner"`.
- **Offline queue DDL:** `DatabaseInitializationService` runs `CREATE TABLE IF NOT EXISTS` on startup. Adding columns to existing SQLite tables requires `ALTER TABLE ... ADD COLUMN ... ;` — safe to run on each startup when guarded by checking column existence.

---

## Recommended Approach

**Strategy:** Test-After  
**Rationale:** All changes are additive plumbing. Existing scan-submission tests cover the happy path; new tests cover the camera identity fields.

---

## Implementation Phases

### Phase 1: ScanApiService — Add `cameraName` + 1-based index conversion

**Goal:** API request body includes both fields; index is 1-based on the wire.

- [ ] `ScanApiService.SubmitScanAsync`: add `string? cameraName = null` parameter after `cameraIndex`.
- [ ] Update `requestBody` anonymous object:
  ```csharp
  var requestBody = new {
      qrPayload,
      scannedAt = scannedAt.ToString("o"),
      scanType,
      cameraIndex = cameraIndex.HasValue ? cameraIndex + 1 : (int?)null,  // 0→1-based
      cameraName
  };
  ```
- [ ] Update `IsScanApiService` interface if it exists (check `SmartLog.Scanner.Core/Services/IScanApiService.cs`).

**Files:** `SmartLog.Scanner.Core/Services/ScanApiService.cs`, `IScanApiService.cs`.

### Phase 2: CameraQrScannerService — Add `_cameraName`

**Goal:** Service carries camera name alongside index; passes both through to submit + enqueue.

- [ ] Add `private string? _cameraName;` field alongside `_cameraIndex`.
- [ ] Add `public void SetCameraName(string? cameraName) => _cameraName = cameraName;`.
- [ ] Update online submit call (line ~194):
  ```csharp
  var serverResult = await _scanApi.SubmitScanAsync(payload, scannedAt, scanType, _cameraIndex, _cameraName);
  ```
- [ ] Update offline enqueue call (line ~231):
  ```csharp
  await _offlineQueue.EnqueueScanAsync(payload, scannedAt, scanType, _cameraIndex, _cameraName);
  ```
- [ ] **Single-camera fallback** (when `_cameraIndex` is null): treat as slot 0 → pass `cameraIndex = 0` (becomes 1-based `1` on wire), `cameraName = _preferences.GetCameraName(0)`. Add a `_preferences` dependency and a `SetSingleCameraMode()` helper, or handle in `SetCameraIndex` setter.
  > Note: Simplest approach — if `_cameraIndex == null`, ScanApiService conversion gives `null` for index. For single-camera, the caller (MainViewModel or single-cam setup) should call `SetCameraIndex(0)` and `SetCameraName(prefs.GetCameraName(0))`. Document this as the expected usage pattern.

**Files:** `SmartLog.Scanner.Core/Services/CameraQrScannerService.cs`.

### Phase 3: MultiCameraManager — Propagate name at init

**Goal:** Each per-camera service instance knows its slot's name.

- [ ] In `InitializeAsync`, after `service.SetCameraIndex(cam.Index)`, add:
  ```csharp
  service.SetCameraName(cam.DisplayName);
  ```
- [ ] `cam.DisplayName` is populated from prefs in `MainViewModel.InitializeCameraSlots()` (`slot.DisplayName = _preferences.GetCameraName(i)`). The `CameraInstance` passed to `InitializeAsync` already carries the current name — no additional loading needed.
- [ ] **AC4 — live rename:** `MultiCameraManager` is initialized once. If the user renames a camera in Setup and saves, the next scan picks up the new name because `cam.DisplayName` in `CameraSlots[i]` is the live observable property. However, `CameraQrScannerService._cameraName` is set only at init. Fix: expose a `UpdateCameraName(int index, string name)` on `IMultiCameraManager` and call it from `MainViewModel` when Setup saves. Alternatively, have `CameraQrScannerService` read from a lambda/delegate that looks up the current name. **Simplest:** `MultiCameraManager.UpdateCameraName(int cameraIndex, string name)` → calls `_services[cameraIndex].SetCameraName(name)`.
- [ ] Add `UpdateCameraName(int cameraIndex, string name)` to `IMultiCameraManager` + implement in `MultiCameraManager`.
- [ ] Call it from `MainViewModel` in the Setup-save handler where `_preferences.SetCameraName(i, slot.DisplayName)` is called.

**Files:** `SmartLog.Scanner.Core/Services/MultiCameraManager.cs`, `IMultiCameraManager.cs`, `SmartLog.Scanner/ViewModels/MainViewModel.cs`.

### Phase 4: USB Scanner Path

**Goal:** USB scans carry `cameraIndex = null`, `cameraName = "USB Scanner"`.

- [ ] Find the USB submission call site in `MainViewModel` (around line 869 — "USB keyboard wedge methods"). It calls `_scanApi.SubmitScanAsync` or a USB-specific scanner service.
- [ ] Pass `cameraName: "USB Scanner"` (and leave cameraIndex as null) for USB mode submissions.

**Files:** `SmartLog.Scanner/ViewModels/MainViewModel.cs`.

### Phase 5: Offline Queue — Schema + Persistence

**Goal:** `QueuedScan` stores camera identity; queue survives as source of truth.

- [ ] `QueuedScan.cs`: add two nullable columns:
  ```csharp
  public int? CameraIndex { get; set; }
  public string? CameraName { get; set; }
  ```
- [ ] `DatabaseInitializationService.cs`: add DDL to add columns on startup (guard with column existence check or use SQLite `ALTER TABLE ... ADD COLUMN` idempotency):
  ```sql
  ALTER TABLE QueuedScans ADD COLUMN CameraIndex INTEGER;
  ALTER TABLE QueuedScans ADD COLUMN CameraName TEXT;
  ```
  SQLite `ADD COLUMN` is idempotent if the column already exists on most SQLite versions. Wrap in try/catch to swallow "duplicate column" errors, or check `PRAGMA table_info(QueuedScans)` first.
- [ ] `IOfflineQueueService.EnqueueScanAsync`: add `int? cameraIndex = null, string? cameraName = null` parameters.
- [ ] `OfflineQueueService.EnqueueScanAsync`: accept + persist:
  ```csharp
  var queuedScan = new QueuedScan {
      ...
      CameraIndex = cameraIndex,
      CameraName = cameraName
  };
  ```
- [ ] `BackgroundSyncService` (line ~218): read camera fields from queued scan and pass through:
  ```csharp
  var result = await _scanApi.SubmitScanAsync(
      scan.QrPayload,
      DateTimeOffset.Parse(scan.ScannedAt),
      scan.ScanType,
      cameraIndex: scan.CameraIndex,     // already 0-based from capture time
      cameraName: scan.CameraName);
  ```
  > Note: `CameraIndex` stored in queue is 0-based (internal). `ScanApiService` converts to 1-based on the wire. ✓

**Files:** `SmartLog.Scanner.Core/Models/QueuedScan.cs`, `SmartLog.Scanner.Core/Services/DatabaseInitializationService.cs` (or `ScannerDbContext.cs`), `SmartLog.Scanner.Core/Services/IOfflineQueueService.cs`, `SmartLog.Scanner.Core/Services/OfflineQueueService.cs`, `SmartLog.Scanner.Core/Services/BackgroundSyncService.cs`.

### Phase 6: Setup UI — Camera Name Input

**Goal:** Admin can set/edit camera name per slot on the Setup page.

- [ ] Check `SmartLog.Scanner/Views/SetupPage.xaml` — does each camera slot row have a name `Entry` field bound to `slot.DisplayName`? If not, add one alongside the camera picker.
- [ ] `CameraSlotViewModel`: confirm `DisplayName` is an `[ObservableProperty]` (it should be per existing multi-camera code). If not, add it.
- [ ] On Setup save, `SetupViewModel` already calls `_preferences.SetCameraName(i, slot.DisplayName)` — also call `_multiCameraManager.UpdateCameraName(i, slot.DisplayName)` to propagate live (AC4).

**Files:** `SmartLog.Scanner/Views/SetupPage.xaml`, `SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs`, `SmartLog.Scanner.Core/ViewModels/CameraSlotViewModel.cs`.

### Phase 7: ScanLogEntry + ScanLogsPage (AC7)

**Goal:** In-app Scan Logs shows camera identity per row.

- [ ] `ScanLogEntry.cs`: add `int? CameraIndex` and `string? CameraName`.
- [ ] Find where `ScanLogEntry` is created from scan results (in `CameraQrScannerService` or where scan results are logged). Populate `CameraIndex = _cameraIndex` and `CameraName = _cameraName`.
- [ ] `ScanLogsPage.xaml`: add "Camera" display in each log row showing `CameraIndex ?? "—" · CameraName ?? "—"` (or "Camera {n} — {name}").

**Files:** `SmartLog.Scanner.Core/Models/ScanLogEntry.cs`, `SmartLog.Scanner.Core/Services/CameraQrScannerService.cs`, `SmartLog.Scanner/Views/ScanLogsPage.xaml`.

### Phase 8: Tests

| AC | Test | File |
|----|------|------|
| AC2 | ScanApiService sends cameraIndex (1-based) + cameraName in payload | `ScanApiServiceTests.cs` |
| AC2 | 0-based index 0 → wire 1; index 7 → wire 8 | same |
| AC3 | USB path: cameraIndex=null, cameraName="USB Scanner" | `MainViewModelTests.cs` |
| AC3 | Single-cam: cameraIndex=1, cameraName from prefs | `CameraQrScannerServiceTests.cs` |
| AC6 | EnqueueScanAsync persists both fields | `OfflineQueueServiceTests.cs` |
| AC6 | BackgroundSync forwards both fields to API | `BackgroundSyncServiceTests.cs` |

- [ ] Run `dotnet test SmartLog.Scanner.Tests`; confirm zero regressions.

---

## Edge Case Handling

| # | Edge Case | Handling |
|---|-----------|----------|
| 1 | Name left blank in Setup | Fallback to `GetCameraName(index)` default `"Camera {index + 1}"`; SetupViewModel should use this default if DisplayName is empty |
| 2 | Camera renamed while scan is in flight | Captured name at scan time is used; `_cameraName` is snapshotted at start of submit |
| 3 | Offline scan from before migration (no CameraIndex/Name cols) | Columns nullable, default null; `BackgroundSync` passes null safely |
| 4 | Index > 8 on wire | `ScanApiService` just passes through; WebApp `[Range(1,8)]` will return 400; existing retry policy handles |

---

## Definition of Done

- [ ] `ScanApiService.SubmitScanAsync` sends `cameraName`; index is 1-based on wire
- [ ] `CameraQrScannerService` carries `_cameraName`; passes to both submit + enqueue paths
- [ ] `MultiCameraManager.InitializeAsync` sets name on each service; `UpdateCameraName` propagates renames live
- [ ] USB path passes `cameraIndex=null`, `cameraName="USB Scanner"`
- [ ] `QueuedScan` has `CameraIndex` + `CameraName`; migration DDL runs on startup
- [ ] `OfflineQueueService.EnqueueScanAsync` persists both fields
- [ ] `BackgroundSyncService` forwards both fields on flush
- [ ] Setup page has an editable name field per camera slot
- [ ] `ScanLogEntry` carries camera fields; ScanLogsPage shows "Camera {n} — {name}"
- [ ] Tests for all listed ACs; `dotnet test` clean

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-24 | Claude | Initial plan |
