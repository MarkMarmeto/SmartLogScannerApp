# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SmartLog Scanner is a .NET 8 MAUI desktop application (Windows + macOS) that scans QR codes to log student attendance and submit records to a SmartLog Web App backend. It supports both camera and USB keyboard-wedge barcode scanners, with offline queuing and background sync when the server is unavailable.

## Solution Structure

- **SmartLog.Scanner** — MAUI UI layer (XAML pages, ViewModels, platform-specific controls)
- **SmartLog.Scanner.Core** — Portable business logic library (services, models, shared ViewModels, EF Core DbContext)
- **SmartLog.Scanner.Tests** — xUnit test suite targeting `net8.0`

## Development Workflow

**Developed on macOS, deployed to Windows.** Both platforms are supported as target frameworks in the same project.

### Build

```bash
# macOS (local development)
dotnet build SmartLog.Scanner -f net8.0-maccatalyst

# Windows (deployment target — can also be cross-compiled from macOS)
dotnet build SmartLog.Scanner -f net8.0-windows10.0.19041.0

# Core library only
dotnet build SmartLog.Scanner.Core -c Release
```

`EnableWindowsTargeting` is set in the csproj so the Windows TFM can be built from macOS (useful for CI validation). `NETSDK1202` is suppressed via `<NoWarn>` in the csproj because the .NET 9 SDK flags `net8.0-maccatalyst` as EOL — this is expected and safe.

### Run locally (macOS)

```bash
dotnet run --project SmartLog.Scanner -f net8.0-maccatalyst
```

### Test

```bash
# Run all tests
dotnet test SmartLog.Scanner.Tests

# Run tests with TRX output
dotnet test SmartLog.Scanner.Tests -c Release --logger "trx;LogFileName=test-results.trx" --results-directory ./test-results

# Run a single test class
dotnet test SmartLog.Scanner.Tests --filter "FullyQualifiedName~HmacValidatorTests"
```

> Build tests with `-c Release` before using `--no-build`.

### Publish to Windows

```bash
dotnet publish SmartLog.Scanner -f net8.0-windows10.0.19041.0 -c Release -o ./publish/win-x64
```

The published output in `publish/win-x64/` can be copied to any Windows 10 (build 19041+) machine and run directly — no SDK required on the target machine.

### Deploying to Windows machines

Use the scripts in `deploy/`:
- **First-time install:** Run `Setup-SmartLogScanner.bat` as Administrator on the target PC. The script auto-detects whether it's running from a pre-built release ZIP (skips build) or the source repo (builds from source).
- **Updates:** Run `Update-SmartLogScanner.bat` as Administrator — pulls latest code, rebuilds, restarts the app.
- MAUI workload needed for source builds: `dotnet workload install maui-windows` (Windows only; NOT `maui` which installs iOS/Android too).

Releases are triggered by `v*` tags via `.github/workflows/release.yml`, which zips `publish/win-x64/` with the deploy scripts.

## Architecture

### Startup Flow (`App.xaml.cs`)
1. Run `SecurityMigrationService` — migrates any plain-text secrets into platform secure storage
2. Initialize SQLite database via EF Core (`ScannerDbContext`)
3. Start `BackgroundSyncService` — polls offline queue and syncs when online
4. Navigate to `SetupPage` (first launch) or `MainPage` (normal launch)

### DI Registration (`MauiProgram.cs`)
All services are registered via `Microsoft.Extensions.DependencyInjection`. Platform-specific implementations (e.g., `IDeviceDetectionService`, `ICameraQrScannerService`) are registered conditionally per target platform in `Platforms/Windows/` and `Platforms/MacCatalyst/`.

### Core Services (SmartLog.Scanner.Core/Services/)

| Service | Role |
|---------|------|
| `SecureConfigService` | API key + HMAC secret via DPAPI (Windows) / Keychain (macOS) |
| `HmacValidator` | HMAC-SHA256 QR signature verification with constant-time comparison |
| `ScanApiService` | HTTP submission to backend; Polly retry (3x exponential) + circuit breaker (5 failures → 30s open) |
| `OfflineQueueService` | SQLite-backed queue for failed submissions |
| `BackgroundSyncService` | Background worker that flushes queue when connectivity is restored |
| `HealthCheckService` | 15-second server health polling; publishes connectivity status |
| `ScanDeduplicationService` | Tiered time-window dedup: 3s / 60s / 300s |
| `PreferencesService` | Non-sensitive config (server URL, scan type, scanner mode, multi-camera config) via MAUI Preferences |

#### Multi-Camera Services (EP0011)

| Service | Role |
|---------|------|
| `IMultiCameraManager` / `MultiCameraManager` | Orchestrates 1–8 concurrent camera workers; starts/stops per-camera decode loops; broadcasts unified scan events |
| `ICameraEnumerationService` | Platform-specific camera discovery (Windows MediaFoundation / macOS AVFoundation) |
| `ICameraWorker` / `ICameraWorkerFactory` | Per-camera decode worker with isolated lifecycle; factory creates workers bound to a camera slot index |
| `AdaptiveDecodeThrottle` | Dynamically adjusts per-worker decode frame rate based on CPU/decode pressure to prevent thermal throttling |
| `CameraQrScannerService` | Single-camera decode primitive; still used as prototype by `MultiCameraManager` workers |

### Data Layer
- **EF Core + SQLite** in `SmartLog.Scanner.Core/Data/ScannerDbContext.cs`
- Tables: `QueuedScans`, `ScanLogs`
- DB file: `{AppDataDirectory}/smartlog-scanner.db`

### UI Layer (SmartLog.Scanner)
MVVM via `CommunityToolkit.Mvvm`. Shared ViewModels (`SetupViewModel`, `ScanLogsViewModel`, `CameraSlotViewModel`) live in Core; app-specific ones (`MainViewModel`, `OfflineQueueViewModel`) are in the Scanner project.

Pages: `MainPage` (multi-camera scan grid + statistics footer), `SetupPage` (wizard — includes multi-camera configuration), `ScanLogsPage` (history), `OfflineQueuePage` (queue management), `AboutPage`.

`MainPage` renders a responsive grid of `CameraSlotViewModel` instances (one per active camera, 1–8 configurable). Each slot has its own ENTRY/EXIT toggle and isolated error state — a crashed camera worker does not affect other slots.

### QR Code Format
QR payloads are HMAC-SHA256 signed. The `HmacValidator` in Core is responsible for verification — see `SmartLog.Scanner.Core/Services/HmacValidator.cs` for the expected format.

Two payload families are supported:
- **Student QR:** `SMARTLOG:{studentId}:{timestamp}:{hmac}` — produces a student `ScanResult` with `Lrn`, `Grade`, `Section`.
- **Visitor Pass QR:** `SMARTLOG-V:{passCode}:{hmac}` — produces a visitor `ScanResult` where `IsVisitorScan = true` and `PassCode` is set. The UI branches on `IsVisitorScan` to display a neutral visitor card (no student metadata, no SMS).

## Configuration

Non-secret runtime config lives in `SmartLog.Scanner/Resources/Raw/appsettings.json` (embedded resource). Secrets (API key, HMAC secret) are never stored in config files — they go through `SecureConfigService` only.

Key config sections: `Logging.MinimumLevel`, `Server` (BaseUrl, AcceptSelfSignedCerts, CertificateThumbprint, TimeoutSeconds), `OfflineQueue` (HealthCheckIntervalSeconds, SyncBatchSize).

## Testing Notes

- Tests use `xUnit` + `Moq`; no real platform APIs are invoked
- `ConnectionTestServiceTests` has a conditional assertion for HTTP vs HTTPS URL validation that differs between Debug and Release builds — see the test file comment
- The test project targets `net8.0` (not a MAUI TFM), so MAUI-specific types must be abstracted behind interfaces to be testable
