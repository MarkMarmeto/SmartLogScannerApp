#Requires -RunAsAdministrator
<#
.SYNOPSIS
    SmartLog Scanner App - Automated Setup for Windows

.DESCRIPTION
    Interactive setup script that automates the installation of SmartLog Scanner App:
    - Checks prerequisites (.NET 8+ SDK, MAUI workload)
    - Installs MAUI workload if missing
    - Builds and publishes the app for Windows (or copies pre-built files)
    - Creates a desktop shortcut
    - Optionally adds to Windows startup (auto-launch on boot)
    - Optionally launches the app for initial configuration

    The app's in-app setup wizard handles server configuration
    (API key, HMAC secret, server URL) on first launch.

.NOTES
    Run this script as Administrator in PowerShell.
    Usage: .\Setup-SmartLogScanner.ps1
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ============================================================
# Configuration
# ============================================================
$Script:AppName         = "SmartLog Scanner"
$Script:InstallDir      = "C:\SmartLogScanner"
$Script:TargetFramework = "net8.0-windows10.0.19041.0"
$Script:Configuration   = "Release"
$Script:ExeName         = "SmartLog.Scanner.exe"

# ============================================================
# Helper Functions
# ============================================================
function Write-StepHeader {
    param([int]$Step, [int]$Total, [string]$Title)
    Write-Host ""
    Write-Host "  [$Step/$Total] $Title" -ForegroundColor Cyan
    Write-Host "  $('=' * 50)" -ForegroundColor DarkGray
}

function Write-Success {
    param([string]$Message)
    Write-Host "  [OK] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "  [!!] $Message" -ForegroundColor Yellow
}

function Write-Fail {
    param([string]$Message)
    Write-Host "  [FAIL] $Message" -ForegroundColor Red
}

function Write-Detail {
    param([string]$Message)
    Write-Host "    $Message" -ForegroundColor Gray
}

function Read-Input {
    param([string]$Prompt, [string]$Default = "")
    if ($Default) {
        $result = Read-Host -Prompt "  $Prompt [$Default]"
        if ([string]::IsNullOrWhiteSpace($result)) { return $Default }
        return $result
    }
    return Read-Host -Prompt "  $Prompt"
}

function Read-YesNo {
    param([string]$Prompt, [bool]$Default = $true)
    $defaultText = if ($Default) { "Y/n" } else { "y/N" }
    $result = Read-Host -Prompt "  $Prompt ($defaultText)"
    if ([string]::IsNullOrWhiteSpace($result)) { return $Default }
    return $result -match '^[Yy]'
}

# ============================================================
# Banner
# ============================================================
Clear-Host
Write-Host ""
Write-Host "  +==================================================+" -ForegroundColor Magenta
Write-Host "  |                                                    |" -ForegroundColor Magenta
Write-Host "  |    SmartLog Scanner App -- Setup Wizard            |" -ForegroundColor Magenta
Write-Host "  |    Windows Desktop Installation                    |" -ForegroundColor Magenta
Write-Host "  |                                                    |" -ForegroundColor Magenta
Write-Host "  +==================================================+" -ForegroundColor Magenta
Write-Host ""
Write-Host "  This wizard will build and install the SmartLog Scanner App." -ForegroundColor Gray
Write-Host "  Server configuration (API key, HMAC secret) is done in the" -ForegroundColor Gray
Write-Host "  app's built-in setup wizard on first launch." -ForegroundColor Gray
Write-Host ""

$totalSteps = 6

# ============================================================
# Step 1: Check Prerequisites
# ============================================================
Write-StepHeader -Step 1 -Total $totalSteps -Title "Checking Prerequisites"

# Check .NET SDK
$dotnetVersion = $null
try {
    $dotnetVersion = (dotnet --version 2>$null)
}
catch { }

$sdkMajor = 0
if ($dotnetVersion) { [int]::TryParse(($dotnetVersion -split '\.')[0], [ref]$sdkMajor) | Out-Null }

if ($sdkMajor -ge 8) {
    Write-Success ".NET SDK $dotnetVersion installed"
}
elseif ($dotnetVersion) {
    Write-Warn ".NET SDK $dotnetVersion found, but 8.0+ is required"
    Write-Host ""
    Write-Host "  Download .NET 8 SDK from:" -ForegroundColor Yellow
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan
    Write-Host ""
    if (-not (Read-YesNo "Continue anyway?" $false)) { exit 1 }
}
else {
    Write-Fail ".NET SDK not found"
    Write-Host ""
    Write-Host "  Download .NET 8 SDK from:" -ForegroundColor Yellow
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Install it and run this script again." -ForegroundColor Yellow
    Read-Host "  Press Enter to exit"
    exit 1
}

# Check Git
$gitVersion = $null
try {
    $gitVersion = (git --version 2>$null)
}
catch { }

if ($gitVersion) {
    Write-Success "Git installed ($gitVersion)"
}
else {
    Write-Warn "Git not found (optional - only needed for updates via git pull)"
}

# ============================================================
# Pre-built detection
# When the script ships inside a release ZIP, the compiled
# SmartLog.Scanner.exe is already present alongside it.
# In that case we skip the MAUI workload check, source-code
# lookup, and build steps.
# ============================================================
$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$isPreBuilt = Test-Path (Join-Path $scriptDir $Script:ExeName)

if ($isPreBuilt) {
    Write-Host ""
    Write-Host "  [INFO] Pre-built package detected - build step will be skipped." -ForegroundColor DarkCyan
}
else {
    # Check MAUI workload (only needed when building from source)
    Write-Detail "Checking MAUI workload..."
    $workloads    = dotnet workload list 2>$null
    $mauiInstalled = $workloads -match 'maui'

    if ($mauiInstalled) {
        Write-Success "MAUI workload installed"
    }
    else {
        Write-Warn "MAUI workload not installed"
        if (Read-YesNo "Install MAUI workload now? (required to build the app)" $true) {
            Write-Detail "Installing maui-windows workload (this may take several minutes)..."
            dotnet workload install maui-windows 2>&1 | ForEach-Object {
                if ($_ -match 'Successfully|installed') { Write-Detail $_ }
            }
            $installResult = $LASTEXITCODE
            if ($installResult -eq 0) {
                Write-Success "MAUI workload installed"
            }
            else {
                Write-Fail "MAUI workload installation failed"
                Write-Host "  Try running manually: dotnet workload install maui-windows" -ForegroundColor Yellow
                if (-not (Read-YesNo "Continue anyway?" $false)) { exit 1 }
            }
        }
        else {
            Write-Fail "MAUI workload is required. Install it and try again."
            exit 1
        }
    }
}

# ============================================================
# Step 2: Locate Source Code
# ============================================================
Write-StepHeader -Step 2 -Total $totalSteps -Title "Locating Source Code"

if ($isPreBuilt) {
    Write-Success "Running from pre-built release package: $scriptDir"
    Write-Detail "Skipping source code lookup - will install from this folder."
    $repoRoot    = $null
    $projectDir  = $null
    $projectFile = $null
}
else {
    # Try to find the project relative to the script (script lives in deploy\ inside the repo)
    $repoRoot    = Split-Path -Parent $scriptDir
    $projectDir  = Join-Path $repoRoot "SmartLog.Scanner"
    $projectFile = Join-Path $projectDir "SmartLog.Scanner.csproj"

    if (Test-Path $projectFile) {
        Write-Success "Found project at: $projectDir"
    }
    else {
        Write-Warn "Project not found at expected location"
        $repoRoot    = Read-Input "Enter the path to the SmartLogScannerApp folder" "C:\SmartLogScannerApp"
        $projectDir  = Join-Path $repoRoot "SmartLog.Scanner"
        $projectFile = Join-Path $projectDir "SmartLog.Scanner.csproj"

        if (-not (Test-Path $projectFile)) {
            Write-Fail "SmartLog.Scanner.csproj not found at $projectDir"
            Write-Host ""
            Write-Host "  Clone the repo first:" -ForegroundColor Yellow
            Write-Host "  git clone https://github.com/MarkMarmeto/SmartLogScannerApp.git C:\SmartLogScannerApp" -ForegroundColor Cyan
            Write-Host ""
            Read-Host "  Press Enter to exit"
            exit 1
        }
        Write-Success "Found project at: $projectDir"
    }

    Write-Detail "Repository root: $repoRoot"
}

# ============================================================
# Step 3: Configure Installation
# ============================================================
Write-StepHeader -Step 3 -Total $totalSteps -Title "Installation Options"

$Script:InstallDir     = Read-Input "Installation directory" $Script:InstallDir
$createDesktopShortcut = Read-YesNo "Create a desktop shortcut?" $true
$addToStartup          = Read-YesNo "Auto-launch on Windows startup?" $false

# ============================================================
# Step 4: Install Application
# ============================================================
Write-StepHeader -Step 4 -Total $totalSteps -Title "Installing Application"

# Create install directory
if (-not (Test-Path $Script:InstallDir)) {
    New-Item -ItemType Directory -Path $Script:InstallDir -Force | Out-Null
    Write-Detail "Created directory: $($Script:InstallDir)"
}

if ($isPreBuilt) {
    # Pre-built mode: copy files from the script's directory
    Write-Detail "Copying pre-built files to $($Script:InstallDir)..."
    Get-ChildItem -Path $scriptDir | Copy-Item -Destination $Script:InstallDir -Recurse -Force
    Write-Success "Files copied to $($Script:InstallDir)"
}
else {
    # Source mode: restore, build, publish
    Write-Detail "Restoring NuGet packages..."
    $restoreOutput = dotnet restore $projectFile 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "Package restore failed"
        Write-Host ($restoreOutput | Out-String) -ForegroundColor Red
        exit 1
    }
    Write-Success "Packages restored"

    Write-Detail "Building and publishing for Windows (this may take a minute)..."
    $publishOutput = dotnet publish $projectDir `
        -f $Script:TargetFramework `
        -c $Script:Configuration `
        -o $Script:InstallDir `
        --nologo 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Fail "Build failed!"
        Write-Host ($publishOutput | Out-String) -ForegroundColor Red
        Write-Host ""
        Write-Host "  Common fixes:" -ForegroundColor Yellow
        Write-Host "    - Run: dotnet workload install maui-windows" -ForegroundColor Gray
        Write-Host "    - Run: dotnet workload repair" -ForegroundColor Gray
        Write-Host "    - Ensure Windows 10 SDK is installed" -ForegroundColor Gray
        exit 1
    }
}

$exePath = Join-Path $Script:InstallDir $Script:ExeName
if (Test-Path $exePath) {
    Write-Success "Application installed to $($Script:InstallDir)"
    Write-Detail "Executable: $exePath"
}
else {
    Write-Fail "Executable not found at $exePath"
    Write-Detail "Checking what was copied/published..."
    $exeFiles = Get-ChildItem -Path $Script:InstallDir -Filter "*.exe" -Recurse | Select-Object -First 5
    if ($exeFiles) {
        Write-Detail "Found executables:"
        $exeFiles | ForEach-Object { Write-Detail "  $($_.FullName)" }
        $exePath           = $exeFiles[0].FullName
        $Script:ExeName    = $exeFiles[0].Name
        Write-Warn "Using: $exePath"
    }
    else {
        Write-Fail "No executables found in install directory"
        exit 1
    }
}

# ============================================================
# Step 5: Create Shortcuts
# ============================================================
Write-StepHeader -Step 5 -Total $totalSteps -Title "Creating Shortcuts"

if ($createDesktopShortcut) {
    $desktopPath  = [Environment]::GetFolderPath("CommonDesktopDirectory")
    $shortcutPath = Join-Path $desktopPath "$($Script:AppName).lnk"

    try {
        $shell    = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath       = $exePath
        $shortcut.WorkingDirectory = $Script:InstallDir
        $shortcut.Description      = "SmartLog QR Attendance Scanner"
        $shortcut.Save()
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
        Write-Success "Desktop shortcut created: $shortcutPath"
    }
    catch {
        Write-Warn "Could not create desktop shortcut: $($_.Exception.Message)"
        try {
            $userDesktop  = [Environment]::GetFolderPath("Desktop")
            $shortcutPath = Join-Path $userDesktop "$($Script:AppName).lnk"
            $shell        = New-Object -ComObject WScript.Shell
            $shortcut     = $shell.CreateShortcut($shortcutPath)
            $shortcut.TargetPath       = $exePath
            $shortcut.WorkingDirectory = $Script:InstallDir
            $shortcut.Description      = "SmartLog QR Attendance Scanner"
            $shortcut.Save()
            [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
            Write-Success "Desktop shortcut created (user): $shortcutPath"
        }
        catch {
            Write-Warn "Could not create shortcut. You can create one manually."
        }
    }
}

if ($addToStartup) {
    $startupFolder   = [Environment]::GetFolderPath("CommonStartup")
    $startupShortcut = Join-Path $startupFolder "$($Script:AppName).lnk"

    try {
        $shell    = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($startupShortcut)
        $shortcut.TargetPath       = $exePath
        $shortcut.WorkingDirectory = $Script:InstallDir
        $shortcut.Description      = "SmartLog Scanner - Auto Start"
        $shortcut.Save()
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
        Write-Success "Startup shortcut created (launches on boot)"
        Write-Detail "Location: $startupShortcut"
    }
    catch {
        Write-Warn "Could not create startup shortcut: $($_.Exception.Message)"
        try {
            $userStartup     = [Environment]::GetFolderPath("Startup")
            $startupShortcut = Join-Path $userStartup "$($Script:AppName).lnk"
            $shell           = New-Object -ComObject WScript.Shell
            $shortcut        = $shell.CreateShortcut($startupShortcut)
            $shortcut.TargetPath       = $exePath
            $shortcut.WorkingDirectory = $Script:InstallDir
            $shortcut.Description      = "SmartLog Scanner - Auto Start"
            $shortcut.Save()
            [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
            Write-Success "Startup shortcut created (current user only)"
        }
        catch {
            Write-Warn "Could not add to startup. You can do this manually via shell:startup"
        }
    }
}

# ============================================================
# Step 6: Launch
# ============================================================
Write-StepHeader -Step 6 -Total $totalSteps -Title "Setup Complete"

Write-Host ""
Write-Host "  +==================================================+" -ForegroundColor Green
Write-Host "  |                                                    |" -ForegroundColor Green
Write-Host "  |    SmartLog Scanner App Installed!                 |" -ForegroundColor Green
Write-Host "  |                                                    |" -ForegroundColor Green
Write-Host "  +==================================================+" -ForegroundColor Green
Write-Host ""
Write-Host "  Install Path : $($Script:InstallDir)" -ForegroundColor White
Write-Host "  Executable   : $exePath" -ForegroundColor White
Write-Host "  Auto-Start   : $(if ($addToStartup) { 'Enabled' } else { 'Disabled' })" -ForegroundColor White
Write-Host ""
Write-Host "  FIRST LAUNCH - have these ready from the Web App admin:" -ForegroundColor Yellow
Write-Host "    1. Server URL   (e.g., http://192.168.1.100:8080)" -ForegroundColor White
Write-Host "    2. API Key      (from Device Management)" -ForegroundColor White
Write-Host "    3. HMAC Secret  (shared QR signing key)" -ForegroundColor White
Write-Host ""
Write-Host "  Logs     : $env:LOCALAPPDATA\SmartLog.Scanner\logs\" -ForegroundColor Gray
Write-Host "  Database : $env:LOCALAPPDATA\SmartLog.Scanner\" -ForegroundColor Gray
Write-Host ""

if (Read-YesNo "Launch SmartLog Scanner now?" $true) {
    Write-Detail "Launching $($Script:AppName)..."
    Start-Process -FilePath $exePath -WorkingDirectory $Script:InstallDir
    Write-Success "App launched! Complete the setup wizard in the app window."
}
else {
    Write-Detail "You can launch the app later from the desktop shortcut or:"
    Write-Detail "  $exePath"
}

Write-Host ""
Read-Host "  Press Enter to exit"
