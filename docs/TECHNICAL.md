# SmartLog Scanner App — Technical Reference

## Stack

| Component | Technology |
|---|---|
| Framework | .NET 8 MAUI (Windows + macOS) |
| Architecture | MVVM (CommunityToolkit.Mvvm) |
| Database | EF Core + SQLite (offline queue) |
| Logging | Serilog (file + console) |
| HTTP Resilience | Polly (retry + circuit breaker) |
| QR Scanning | ZXing.Net.Maui (camera) |
| Audio | Plugin.Maui.Audio |
| Secure Storage | DPAPI (Windows) / Keychain (macOS) |

---

## Solution Structure

```
SmartLogScannerApp/
├── SmartLog.Scanner/              # MAUI app (UI + platform-specific code)
│   ├── Views/                     # XAML pages
│   │   ├── MainPage.xaml          # Scan screen (primary UI)
│   │   ├── SetupPage.xaml         # First-launch setup wizard
│   │   ├── ScanLogsPage.xaml      # Scan history viewer
│   │   ├── OfflineQueuePage.xaml  # Offline queue management
│   │   └── AboutPage.xaml
│   ├── ViewModels/                # App-level view models
│   │   ├── MainViewModel.cs       # Scan logic, feedback, statistics
│   │   └── OfflineQueueViewModel.cs
│   ├── Platforms/
│   │   ├── Windows/               # WinUI-specific (DeviceDetection, Program.cs)
│   │   └── MacCatalyst/           # macOS-specific (Camera, DeviceDetection)
│   ├── Controls/                  # CameraQrView (custom camera control)
│   ├── Converters/                # XAML value converters
│   └── Services/                  # Platform services (Sound, Navigation)
├── SmartLog.Scanner.Core/         # Portable business logic library
│   ├── Services/                  # All core services (see below)
│   ├── Models/                    # ScanResult, QueuedScan, ScanLog, etc.
│   ├── ViewModels/                # SetupViewModel, ScanLogsViewModel
│   ├── Data/                      # EF Core DbContext (SQLite)
│   ├── Constants/                 # App-wide constants
│   ├── Exceptions/                # Custom exception types
│   └── Infrastructure/            # HTTP client setup
├── SmartLog.Scanner.Tests/        # xUnit test suite (net8.0, no MAUI TFM)
├── deploy/                        # Windows deployment scripts
│   ├── Setup-SmartLogScanner.ps1  # Automated first-install wizard
│   ├── Setup-SmartLogScanner.bat  # Launcher (run as Administrator)
│   ├── Update-SmartLogScanner.ps1 # Pull + rebuild + restart
│   └── Update-SmartLogScanner.bat # Launcher
├── docs/                          # This documentation
├── sdlc-studio/                   # Product & engineering docs (PRD, TRD, stories)
└── SmartLogScanner.sln
```

---

## Architecture

### Startup Flow (`App.xaml.cs`)

1. `SecurityMigrationService` — migrates any plain-text secrets into platform secure storage
2. Initialize SQLite via EF Core (`ScannerDbContext`)
3. Start `BackgroundSyncService` — polls offline queue, syncs when online
4. Navigate to `SetupPage` (first launch) or `MainPage` (configured)

### DI Registration (`MauiProgram.cs`)

All services registered via `Microsoft.Extensions.DependencyInjection`. Platform-specific implementations (`IDeviceDetectionService`, `ICameraQrScannerService`) are registered per-platform in `Platforms/Windows/` and `Platforms/MacCatalyst/`.

### Core Services (`SmartLog.Scanner.Core/Services/`)

| Service | Role |
|---|---|
| `SecureConfigService` | API key + HMAC secret via DPAPI (Windows) / Keychain (macOS) |
| `HmacValidator` | HMAC-SHA256 QR signature verification (constant-time comparison) |
| `ScanApiService` | HTTP submission to backend; Polly retry (3× exponential) + circuit breaker (5 failures → 30s open) |
| `OfflineQueueService` | SQLite-backed queue for scans that fail to submit |
| `BackgroundSyncService` | Background worker that flushes queue when connectivity is restored |
| `HealthCheckService` | 15-second server health polling; publishes connectivity status |
| `ScanDeduplicationService` | Tiered time-window dedup: 3s (suppress) / 60s (warn) / 300s (server) |
| `PreferencesService` | Non-sensitive config (server URL, scan type, scanner mode) via MAUI Preferences |

---

## Data Layer

- **EF Core + SQLite** in `SmartLog.Scanner.Core/Data/ScannerDbContext.cs`
- Tables: `QueuedScans`, `ScanLogs`
- DB file: `{AppDataDirectory}/smartlog-scanner.db`

---

## QR Code Format & HMAC Validation

```
SMARTLOG:{studentId}:{timestamp}:{HMAC-SHA256-base64}
```

The `HmacValidator` performs local pre-validation before sending to the server:
1. Split payload on `:` — expect 4 parts
2. Verify prefix is `SMARTLOG`
3. Recompute `HMAC-SHA256(studentId:timestamp)` with the stored HMAC secret
4. Compare using constant-time algorithm to prevent timing attacks

See `SmartLog.Scanner.Core/Services/HmacValidator.cs`.

---

## Security Model

| Concern | Implementation |
|---|---|
| API key storage | Platform secure storage (DPAPI/Keychain) via `SecureConfigService` |
| HMAC secret storage | Platform secure storage — never written to config files |
| QR validation | HMAC-SHA256 constant-time comparison (local pre-validation) |
| Transport | HTTPS with self-signed certificate support (configurable thumbprint) |
| Timing attacks | `CryptographicOperations.FixedTimeEquals` for HMAC comparison |

---

## Configuration

Non-secret runtime config is in `SmartLog.Scanner/Resources/Raw/appsettings.json` (embedded resource):

```json
{
  "Logging": { "MinimumLevel": "Information" },
  "Server": {
    "BaseUrl": "",
    "AcceptSelfSignedCerts": false,
    "CertificateThumbprint": "",
    "TimeoutSeconds": 10
  },
  "OfflineQueue": {
    "HealthCheckIntervalSeconds": 15,
    "SyncBatchSize": 10
  }
}
```

Secrets (API key, HMAC secret) are never in config files — only through `SecureConfigService`.

---

## Build & Run

### macOS (development)

```bash
dotnet build SmartLog.Scanner -f net8.0-maccatalyst
dotnet run --project SmartLog.Scanner -f net8.0-maccatalyst
```

### Windows

```bash
dotnet build SmartLog.Scanner -f net8.0-windows10.0.19041.0
dotnet run --project SmartLog.Scanner -f net8.0-windows10.0.19041.0
```

### Publish (Windows release)

```bash
dotnet publish SmartLog.Scanner -f net8.0-windows10.0.19041.0 -c Release -o ./publish/win-x64
```

The published output in `publish/win-x64/` runs on any Windows 10 (build 19041+) machine — no .NET SDK required on the target.

### Run Tests

```bash
# All tests
dotnet test SmartLog.Scanner.Tests

# Single class
dotnet test SmartLog.Scanner.Tests --filter "FullyQualifiedName~HmacValidatorTests"

# With TRX output
dotnet test SmartLog.Scanner.Tests -c Release --logger "trx;LogFileName=test-results.trx" --results-directory ./test-results
```

---

## Cross-Platform Notes

- **Developed on macOS**, deployed to Windows. Both TFMs in the same csproj.
- `EnableWindowsTargeting` allows the Windows TFM to be built from macOS for CI validation.
- `NETSDK1202` is suppressed — the .NET 9 SDK flags `net8.0-maccatalyst` as EOL; this is expected and safe for .NET 8 targets.
- MAUI workload for source builds on Windows: `dotnet workload install maui-windows` (not `maui`, which installs iOS/Android).
- Platform-specific camera and device detection are behind interfaces and registered conditionally per platform.

---

## Testing Notes

- Tests use `xUnit` + `Moq`; no real platform APIs invoked
- Test project targets `net8.0` (not a MAUI TFM) — MAUI types are behind interfaces
- `ConnectionTestServiceTests` has a conditional assertion for HTTP vs HTTPS URL validation that differs between Debug and Release builds (see test file comment)
- Build tests with `-c Release` before using `--no-build`

---

## CI/CD

- **CI** — Builds and tests on every push/PR to `main` (GitHub Actions, Windows runner)
- **CD** — Creates GitHub Release with Windows x64 zip on `v*` tags (`.github/workflows/release.yml`)
- Release zip includes `publish/win-x64/` output + `deploy/` scripts
