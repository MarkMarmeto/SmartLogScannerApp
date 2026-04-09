# SmartLog Scanner App — Deployment Guide

The Scanner App runs as a desktop application on Windows (and macOS for development). This guide covers production deployment to Windows machines at school gates.

---

## Prerequisites

- Windows 10 (build 19041 or later) or Windows 11
- The published SmartLog Scanner build (`.exe` + supporting files)
- Network connectivity to the SmartLog Web App server
- SmartLog Web App server URL and a registered device API key
- HMAC secret from the SmartLog Web App administrator

---

## Option A: Automated Setup (Recommended)

The `deploy/` folder contains scripts that handle everything.

### Deployment Scripts

| Script | Purpose |
|---|---|
| `Setup-SmartLogScanner.bat` | **Full automated installation wizard** — checks prereqs, installs MAUI workload if needed, builds or copies pre-built files, creates desktop shortcut, optionally adds to startup |
| `Update-SmartLogScanner.bat` | Update existing installation — backs up current install, pulls latest code, rebuilds, relaunches. Supports `-SkipBackup` and `-Branch` flags |

### First-Time Install

1. On the target Windows PC, navigate to the release folder (or cloned repository).
2. Right-click **`Setup-SmartLogScanner.bat`** → **Run as administrator**.
3. The wizard will:
   - Check for .NET MAUI workload (or use pre-built release files)
   - Install the application to `%LocalAppData%\SmartLogScanner`
   - Create a desktop shortcut
   - Optionally configure to run on startup
4. Launch the app and complete the **Setup Wizard** (server URL, API key, HMAC secret).

> The script auto-detects whether it is running from a pre-built release ZIP (skips build) or from source (builds from source). Pre-built is preferred for deployment.

### Updates

Right-click **`Update-SmartLogScanner.bat`** → **Run as administrator**.

The script backs up the current install, pulls latest code (or extracts a new release ZIP), rebuilds, and relaunches the app.

Optional flags (when running the `.ps1` directly):

```powershell
.\deploy\Update-SmartLogScanner.ps1 -SkipBackup   # Skip backup (faster)
.\deploy\Update-SmartLogScanner.ps1 -Branch dev    # Pull from a different branch
```

---

## Option B: Manual Deployment

### 1. Build (if deploying from source)

On a machine with the .NET 8 SDK and MAUI workload:

```bash
dotnet workload install maui-windows
dotnet publish SmartLog.Scanner -f net8.0-windows10.0.19041.0 -c Release -o ./publish/win-x64
```

The `publish/win-x64/` folder is self-contained — copy it to the target machine. No SDK needed on the target.

### 2. Run on Target Machine

Copy `publish/win-x64/` to the target PC (e.g., `C:\SmartLogScanner\`).

Run `SmartLog.Scanner.exe` directly. On first launch, the **Setup Wizard** opens automatically.

### 3. Complete the Setup Wizard

| Field | Where to Get It |
|---|---|
| Server URL | e.g., `http://192.168.1.10:5050` — the static IP of the SmartLog server |
| API Key | From SmartLog Web App: Admin > Register Device (shown once) |
| HMAC Secret | From the SmartLog Web App administrator |

Click **Test Connection** to verify before saving. The app proceeds to the main scan screen once configured.

---

## Releases via GitHub

Production releases are published automatically on `v*` tags:

- GitHub Actions builds the Windows x64 release
- Creates a GitHub Release with a ZIP containing `publish/win-x64/` + `deploy/` scripts
- Download the latest release ZIP, extract, and run `Setup-SmartLogScanner.bat` as Administrator

---

## Configuration Storage

After setup, credentials are stored in platform secure storage:
- **API Key** → Windows DPAPI (encrypted, bound to the Windows user account)
- **HMAC Secret** → Windows DPAPI

Non-sensitive settings (server URL, scan type, scanner mode) are stored in MAUI Preferences (the Windows registry under `HKCU`).

To reset all settings: uninstall the app (or delete the MAUI Preferences entry) and re-run the Setup Wizard.

---

## Multiple Scanner Stations

Each gate PC is a separate device registration in the SmartLog Web App. Each gets its own API key. All point to the same server URL.

| Device | Location | Server URL |
|---|---|---|
| Main Gate Scanner | Main Entrance | `http://192.168.1.10:5050` |
| Back Gate Scanner | Back Entrance | `http://192.168.1.10:5050` |

Register each device at **Admin > Register Device** in the SmartLog Web App.

---

## Network Requirements

- The scanner PC must be on the **same LAN** as the SmartLog server (same router/switch, either Ethernet or Wi-Fi)
- Verify connectivity:

```cmd
ping 192.168.1.10
curl http://192.168.1.10:5050/health
```

Expected: ping replies and `Healthy` response.

See `SmartLogWebApp/docs/DEPLOYMENT.md` (Section: Network Setup for Scanner Devices) for detailed network configuration.

---

## Troubleshooting

| Problem | Check |
|---|---|
| Setup Wizard can't connect | Correct server URL and port? `curl http://{server}/health` from this PC. |
| "Invalid API Key" after setup | Was the API key copied completely? Re-register the device and copy the full `sk_live_xxx` key. |
| Scanner not reading QR codes | Camera permissions granted? USB scanner — try a different USB port. |
| App crashes on launch | Check `%LocalAppData%\SmartLogScanner\logs\` for Serilog log files. |
| Audio not playing | System volume up? App has audio permissions? |
| Scans not reaching server | Health indicator shows offline? Server running? Firewall open on port 5050? |

---

## Uninstall

Delete the installation folder (e.g., `C:\SmartLogScanner\`) and the desktop shortcut. Credentials stored in DPAPI are automatically cleaned up when the Windows user profile is deleted.
