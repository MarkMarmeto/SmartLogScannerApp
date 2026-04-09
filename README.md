# SmartLog Scanner App

A **.NET MAUI** desktop application for scanning student QR codes at school entry/exit gates. Part of the **SmartLog** school attendance tracking system for Philippine K-12 schools.

Reads student QR codes (via camera or USB barcode scanner), validates HMAC-SHA256 signatures locally, submits scans to the SmartLog Web App server, and displays real-time color-coded feedback — with full offline resilience.

**Stack:** .NET 8 MAUI · MVVM · SQLite · Polly · ZXing  
**Platforms:** Windows (production) · macOS (development)

---

## Documentation

| Doc | Description |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | System architecture — how ScannerApp and WebApp communicate |
| [docs/FEATURES.md](docs/FEATURES.md) | Full feature list |
| [docs/TECHNICAL.md](docs/TECHNICAL.md) | Architecture, services, data layer, build & test commands |
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | Windows deployment, automated setup scripts, troubleshooting |

---

## Quick Start

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

### Run Tests

```bash
dotnet test SmartLog.Scanner.Tests
```

### Deploy to Windows

Right-click `deploy/Setup-SmartLogScanner.bat` → **Run as administrator**. See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for full instructions.

---

## First Launch

The app opens a Setup Wizard on first launch. You'll need:

1. **Server URL** — SmartLog Web App address (e.g., `http://192.168.1.10:5050`)
2. **API Key** — From the Web App's Device Management page (`sk_live_xxx`)
3. **HMAC Secret** — Shared QR signing key from the Web App administrator

---

## Project Structure

```
SmartLogScannerApp/
├── SmartLog.Scanner/       # MAUI UI layer (XAML, ViewModels, platform code)
├── SmartLog.Scanner.Core/  # Business logic library (services, models, EF Core)
├── SmartLog.Scanner.Tests/ # xUnit test suite
├── deploy/                 # Windows deployment scripts
└── docs/                   # Technical documentation
```

---

## Integration with SmartLog Web App

| Endpoint | Purpose |
|---|---|
| `POST /api/v1/scans` | Submit QR scan (authenticated via `X-API-Key` header) |
| `GET /api/v1/health` | Server connectivity check (no auth) |

See the Web App's [API reference](https://github.com/markmarmeto/SmartLogWebApp) for full documentation.

---

## CI/CD

- **CI** — Builds and tests on every push/PR to `main` (GitHub Actions, Windows runner)
- **CD** — Creates GitHub Releases with Windows x64 zip on `v*` tags

---

## License

Private — All rights reserved.
