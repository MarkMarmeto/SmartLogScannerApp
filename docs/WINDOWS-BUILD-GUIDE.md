# Windows Build Guide

## Overview

The SmartLog Scanner app now supports **Windows 10/11** in addition to macOS. However, **Windows builds must be performed on a Windows machine** due to platform-specific build tools that are not available on macOS.

---

## Prerequisites

### 1. Windows 10 or 11
- **Minimum Version:** Windows 10, version 2004 (build 19041) or later
- **Recommended:** Windows 11 for best compatibility

### 2. .NET 8.0 SDK
Download and install from: https://dotnet.microsoft.com/download/dotnet/8.0

```powershell
# Verify installation
dotnet --version
# Should show 8.0.x
```

### 3. Visual Studio 2022 (Recommended)
- **Edition:** Community (free), Professional, or Enterprise
- **Version:** 17.8 or later
- **Required Workloads:**
  - .NET Multi-platform App UI development
  - Windows App SDK C# Templates

**Install via Visual Studio Installer:**
1. Open Visual Studio Installer
2. Click "Modify" on your VS 2022 installation
3. Select ".NET Multi-platform App UI development"
4. Ensure "Windows App SDK C# Templates" is checked
5. Click "Modify" to install

### 4. Windows App SDK
Included with the .NET MAUI workload, but verify:

```powershell
dotnet workload list
# Should show: maui, maccatalyst (if on Mac), windows
```

If missing:
```powershell
dotnet workload install maui
```

---

## Building the App

### Option 1: Visual Studio 2022 (Recommended)

1. **Open Solution:**
   ```
   File → Open → Project/Solution
   Navigate to: SmartLog.Scanner/SmartLog.Scanner.csproj
   ```

2. **Select Windows Target:**
   - In the toolbar, change target framework to: `net8.0-windows10.0.19041.0`
   - Select build configuration: `Debug` or `Release`
   - Select platform: `x64` (for 64-bit Windows) or `ARM64` (for ARM-based PCs)

3. **Build:**
   ```
   Build → Build Solution (Ctrl+Shift+B)
   ```

4. **Run:**
   ```
   Debug → Start Debugging (F5)
   ```

### Option 2: Command Line

1. **Open PowerShell or Command Prompt**

2. **Navigate to Project:**
   ```powershell
   cd SmartLog.Scanner
   ```

3. **Build for Windows (Debug):**
   ```powershell
   dotnet build SmartLog.Scanner.csproj --framework net8.0-windows10.0.19041.0 --configuration Debug
   ```

4. **Build for Windows (Release):**
   ```powershell
   dotnet build SmartLog.Scanner.csproj --framework net8.0-windows10.0.19041.0 --configuration Release
   ```

5. **Run the App:**
   ```powershell
   dotnet run --project SmartLog.Scanner.csproj --framework net8.0-windows10.0.19041.0
   ```

---

## Output Locations

After building, find the executable at:

**Debug Build:**
```
SmartLog.Scanner/bin/Debug/net8.0-windows10.0.19041.0/win10-x64/SmartLog.Scanner.exe
```

**Release Build:**
```
SmartLog.Scanner/bin/Release/net8.0-windows10.0.19041.0/win10-x64/SmartLog.Scanner.exe
```

---

## Publishing for Distribution

### Create Self-Contained Deployment

This creates a standalone package that doesn't require .NET to be installed on the target machine:

```powershell
dotnet publish SmartLog.Scanner.csproj `
  --framework net8.0-windows10.0.19041.0 `
  --configuration Release `
  --runtime win10-x64 `
  --self-contained true `
  --output ./publish/windows-x64
```

**Output:** `./publish/windows-x64/SmartLog.Scanner.exe`

### Create MSIX Package (Windows Store)

For Microsoft Store distribution or enterprise deployment:

1. **In Visual Studio:**
   - Right-click project → Publish → Create App Packages
   - Follow wizard to create MSIX package

2. **Via Command Line:**
   ```powershell
   # Update project to use MSIX packaging
   # Edit SmartLog.Scanner.csproj:
   # <WindowsPackageType>MSIX</WindowsPackageType>

   dotnet publish SmartLog.Scanner.csproj `
     --framework net8.0-windows10.0.19041.0 `
     --configuration Release `
     --runtime win10-x64
   ```

---

## Platform-Specific Features

### Windows Implementation

The following Windows-specific features are implemented:

1. **Device Detection:**
   - `Platforms/Windows/DeviceDetectionService.cs`
   - Uses `Windows.Devices.Enumeration` for camera detection
   - Supports USB barcode scanners (keyboard wedge mode)

2. **Camera Access:**
   - Uses `Windows.Media.Capture` APIs
   - Automatically requests camera permissions
   - Supports front/back/external cameras

3. **Secure Storage:**
   - Uses **Windows DPAPI** (Data Protection API) for credential storage
   - API keys and HMAC secrets encrypted at rest
   - Tied to Windows user account

### Security Features on Windows

All security fixes are active on Windows:

- ✅ **CRITICAL-01:** Secrets stored in DPAPI (not plain text files)
- ✅ **CRITICAL-02:** Preferences fallback disabled in Release builds
- ✅ **HIGH-01:** HTTPS enforced in Release builds
- ✅ **HIGH-02:** Certificate pinning with thumbprint validation
- ✅ **HIGH-03:** Server response validation (XSS/injection/DoS prevention)

---

## Troubleshooting

### Build Error: "Windows SDK not found"

**Solution:**
```powershell
# Install Windows SDK
dotnet workload install windows
```

### Build Error: "WindowsAppSDK not found"

**Solution:**
Install Visual Studio with .NET MAUI workload (includes WindowsAppSDK)

### Camera Permission Denied

**Solution:**
1. Open Windows Settings → Privacy & Security → Camera
2. Enable "Let apps access your camera"
3. Enable for "SmartLog Scanner"

### DPAPI/SecureStorage Fails

**Issue:** Windows DPAPI requires user to be logged in with a Windows account.

**Solution:**
- Ensure user is logged in with Microsoft account or local account
- Debug builds: Fallback to Preferences (insecure but functional)
- Release builds: Will fail fast (secure by design)

### App Won't Start: "This app can't run on your PC"

**Solution:**
- Ensure Windows 10 version 2004 (19041) or later
- Check Windows Update for latest updates
- Verify correct architecture (x64 vs ARM64)

---

## Cross-Platform Development Workflow

### Developing on macOS, Building for Windows

**Recommended Approach:**

1. **Develop on Mac:**
   - Write code on macOS using Visual Studio Code or Visual Studio for Mac
   - Test with macOS build (`net8.0-maccatalyst`)
   - All core logic is platform-agnostic

2. **Build on Windows:**
   - Push code to Git repository
   - Clone on Windows machine
   - Build Windows version
   - Test Windows-specific features

3. **CI/CD Pipeline:**
   - Use GitHub Actions or Azure DevOps
   - macOS runner for Mac builds
   - Windows runner for Windows builds
   - Automated multi-platform releases

### Example GitHub Actions Workflow

```yaml
name: Multi-Platform Build

on: [push]

jobs:
  build-windows:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: 8.0.x
      - name: Build Windows
        run: dotnet build SmartLog.Scanner/SmartLog.Scanner.csproj --framework net8.0-windows10.0.19041.0 --configuration Release

  build-macos:
    runs-on: macos-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: 8.0.x
      - name: Build macOS
        run: dotnet build SmartLog.Scanner/SmartLog.Scanner.csproj --framework net8.0-maccatalyst --configuration Release
```

---

## Platform Comparison

| Feature | Windows | macOS |
|---------|---------|-------|
| **Camera Access** | Windows.Media.Capture | AVFoundation |
| **Device Detection** | Windows.Devices.Enumeration | AVCaptureDeviceDiscoverySession |
| **Secure Storage** | DPAPI (Data Protection API) | Keychain |
| **USB Scanner** | HID (Human Interface Device) | IOKit |
| **Build Tools** | Visual Studio 2022 | Xcode + VS for Mac |
| **Min OS Version** | Windows 10 (19041) | macOS 14.0 |

---

## Support

For issues specific to Windows builds:

1. Check Visual Studio installation (`.NET Multi-platform App UI` workload)
2. Verify .NET 8.0 SDK installed
3. Ensure Windows 10/11 is up to date
4. Review logs: `SmartLog.Scanner/bin/Debug/net8.0-windows10.0.19041.0/`

For general app issues, see main README.md

---

**Last Updated:** March 9, 2026
**Compatible with:** .NET 8.0, Windows 10 (19041+), Windows 11
