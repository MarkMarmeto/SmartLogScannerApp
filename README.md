# SmartLog Scanner App

A **.NET MAUI** desktop application for scanning student QR codes at school entry/exit gates. Part of the **SmartLog** school attendance tracking system for Philippine K-12 schools.

## Overview

SmartLog Scanner reads student QR codes (via camera or USB barcode scanner), validates HMAC-SHA256 signatures locally, submits scans to the [SmartLog Web App](https://github.com/markmarmeto/SmartLogWebApp) server, and displays real-time feedback to the gate guard — all with full offline resilience.

### Key Features

- **Dual scanning modes** — Camera-based QR scanning (AVFoundation/WinUI) and USB keyboard-wedge barcode scanner
- **HMAC-SHA256 validation** — Local QR signature verification before server submission
- **Offline resilience** — SQLite queue with automatic background sync when connectivity is restored
- **Real-time feedback** — Color-coded results (green/amber/red/blue) with audio cues
- **Health monitoring** — 15-second server health checks with visual connection status
- **Scan deduplication** — Smart tiered time windows to prevent duplicate entries
- **Secure configuration** — DPAPI-encrypted storage on Windows, Keychain on macOS

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Framework | .NET 8 MAUI |
| Architecture | MVVM (CommunityToolkit.Mvvm) |
| Database | EF Core + SQLite (offline queue) |
| Logging | Serilog (file + console) |
| HTTP Resilience | Polly (retry + circuit breaker) |
| QR Scanning | ZXing.Net.Maui |
| Audio | Plugin.Maui.Audio |

## Project Structure

```
SmartLogScannerApp/
├── SmartLog.Scanner/              # MAUI app (UI + platform-specific code)
│   ├── Views/                     # XAML pages (Main, Setup, ScanLogs, OfflineQueue)
│   ├── ViewModels/                # Page view models
│   ├── Platforms/
│   │   ├── Windows/               # Windows-specific (DeviceDetection, Program)
│   │   └── MacCatalyst/           # macOS-specific (Camera, DeviceDetection)
│   ├── Controls/                  # Custom controls (CameraQrView)
│   ├── Converters/                # XAML value converters
│   └── Services/                  # Platform services (Sound, Navigation)
├── SmartLog.Scanner.Core/         # Business logic library
│   ├── Services/                  # Core services (API, HMAC, Sync, Config, etc.)
│   ├── Models/                    # Data models (ScanResult, QueuedScan, etc.)
│   ├── ViewModels/                # Shared view models (Setup, ScanLogs)
│   └── Data/                      # EF Core DbContext
├── SmartLog.Scanner.Tests/        # xUnit test suite
├── deploy/                        # Deployment scripts
│   └── Setup-SmartLogScanner.ps1  # Windows automated setup wizard
├── sdlc-studio/                   # SDLC documentation (PRD, TRD, epics, stories)
├── setup-guide.html               # Windows setup documentation
└── documentation.html             # Technical documentation
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- .NET MAUI workload: `dotnet workload install maui`
- **Windows:** Visual Studio 2022 with MAUI workload, or Windows App SDK
- **macOS:** Xcode 15+ (for Mac Catalyst builds)

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

## Run Tests

```bash
dotnet test SmartLog.Scanner.Tests
```

## First Launch Setup

The app opens a Setup Wizard on first launch. You'll need:

1. **Server URL** — The SmartLog Web App address (e.g., `http://192.168.1.100:5050`)
2. **API Key** — From the Web App's Device Management page (`sk_live_xxx`)
3. **HMAC Secret** — Shared QR signing key (from the Web App administrator)

See `setup-guide.html` for detailed Windows deployment instructions, or run `deploy/Setup-SmartLogScanner.ps1` for automated setup.

## Integration with SmartLog Web App

| Endpoint | Purpose |
|----------|---------|
| `POST /api/v1/scans` | Submit QR scan (authenticated via `X-API-Key` header) |
| `GET /api/v1/health` | Server connectivity check (no auth) |

### QR Code Format

```
SMARTLOG:{studentId}:{timestamp}:{HMAC-SHA256 signature}
```

The scanner validates the HMAC signature locally, then forwards the full payload to the server for processing.

## CI/CD

- **CI** — Builds and tests on every push/PR to `main` (GitHub Actions, Windows runner)
- **CD** — Creates GitHub Releases with Windows x64 zip on version tags (`v*`)

## License

Private — All rights reserved.
