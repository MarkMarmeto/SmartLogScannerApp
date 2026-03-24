#Requires -RunAsAdministrator
<#
.SYNOPSIS
    SmartLog Scanner App -- Update Script for Windows

.DESCRIPTION
    Automates the update process for an existing SmartLog Scanner App installation:
    - Backs up current installation
    - Pulls latest code from GitHub
    - Rebuilds and publishes the application
    - Relaunches the app (if it was running)

    Safe to run multiple times. Creates timestamped backups.

.NOTES
    Run this script as Administrator in PowerShell.
    Usage: .\Update-SmartLogScanner.ps1

    Optional parameters:
      -SkipBackup    Skip creating a backup before updating
      -Branch        Git branch to pull from (default: main)
#>

[CmdletBinding()]
param(
    [switch]$SkipBackup,
    [string]$Branch = "main"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ============================================================
# Configuration
# ============================================================
$Script:AppName         = "SmartLog Scanner"
$Script:InstallDir      = "C:\SmartLogScanner"
$Script:BackupDir       = "C:\SmartLogScannerBackups"
$Script:ExeName         = "SmartLog.Scanner.exe"
$Script:TargetFramework = "net8.0-windows10.0.19041.0"
$Script:Configuration   = "Release"

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
    Write-Host "  [X]  $Message" -ForegroundColor Red
}

function Write-Detail {
    param([string]$Message)
    Write-Host "    $Message" -ForegroundColor Gray
}

# ============================================================
# Banner
# ============================================================
Clear-Host
Write-Host ""
Write-Host "  ======================================================" -ForegroundColor Cyan
Write-Host "       SmartLog Scanner App -- Update Tool               " -ForegroundColor Cyan
Write-Host "  ======================================================" -ForegroundColor Cyan
Write-Host ""

$totalSteps = 5

# ============================================================
# Step 1: Validate Existing Installation
# ============================================================
Write-StepHeader -Step 1 -Total $totalSteps -Title "Validating Installation"

# Check install directory
if (-not (Test-Path $Script:InstallDir)) {
    Write-Fail "Install directory not found: $($Script:InstallDir)"
    Write-Host "  Run Setup-SmartLogScanner.ps1 for first-time installation." -ForegroundColor Yellow
    Read-Host "  Press Enter to exit"
    exit 1
}
Write-Success "Install directory exists: $($Script:InstallDir)"

# Check executable
$exePath = Join-Path $Script:InstallDir $Script:ExeName
if (-not (Test-Path $exePath)) {
    Write-Warn "Executable not found at expected location: $exePath"
    # Try to find it
    $exeFiles = Get-ChildItem -Path $Script:InstallDir -Filter "*.exe" -Recurse | Where-Object { $_.Name -like "SmartLog*" } | Select-Object -First 1
    if ($exeFiles) {
        $exePath = $exeFiles.FullName
        $Script:ExeName = $exeFiles.Name
        Write-Detail "Found executable at: $exePath"
    }
    else {
        Write-Fail "No SmartLog executable found in $($Script:InstallDir)"
        Read-Host "  Press Enter to exit"
        exit 1
    }
}
Write-Success "Executable found: $($Script:ExeName)"

# Find repo root
$repoRoot = $null
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$possibleRoot = Split-Path -Parent $scriptDir

if (Test-Path (Join-Path $possibleRoot "SmartLog.Scanner\SmartLog.Scanner.csproj")) {
    $repoRoot = $possibleRoot
}
elseif (Test-Path "C:\SmartLogScannerApp\SmartLog.Scanner\SmartLog.Scanner.csproj") {
    $repoRoot = "C:\SmartLogScannerApp"
}

if (-not $repoRoot) {
    Write-Warn "Could not auto-detect repository location."
    $repoRoot = Read-Host "  Enter the path to SmartLogScannerApp repository"
    if (-not (Test-Path (Join-Path $repoRoot "SmartLog.Scanner\SmartLog.Scanner.csproj"))) {
        Write-Fail "SmartLog.Scanner.csproj not found at $repoRoot\SmartLog.Scanner\"
        Read-Host "  Press Enter to exit"
        exit 1
    }
}
Write-Success "Repository found: $repoRoot"

# ============================================================
# Step 2: Check for Updates
# ============================================================
Write-StepHeader -Step 2 -Total $totalSteps -Title "Checking for Updates"

Push-Location $repoRoot
try {
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = "Continue"

    Write-Detail "Fetching from remote..."
    git fetch origin $Branch 2>&1 | Out-Null

    $localHash = (git rev-parse HEAD 2>&1) | Select-Object -Last 1
    $remoteHash = (git rev-parse "origin/$Branch" 2>&1) | Select-Object -Last 1

    $ErrorActionPreference = $prevEAP

    if ($localHash -eq $remoteHash) {
        Write-Success "Already up to date (commit: $($localHash.Substring(0, 7)))"
        Write-Host ""
        $continue = Read-Host "  No new changes found. Rebuild anyway? (y/N)"
        if ($continue -notmatch '^[Yy]') {
            Write-Host ""
            Write-Host "  No update needed. Exiting." -ForegroundColor Gray
            Pop-Location
            Read-Host "  Press Enter to exit"
            exit 0
        }
    }
    else {
        $ErrorActionPreference = "Continue"
        $commitCount = (git rev-list --count "$localHash..$remoteHash" 2>&1) | Select-Object -Last 1
        Write-Success "$commitCount new commit(s) available"
        Write-Host ""
        Write-Host "  Recent changes:" -ForegroundColor Gray
        git log --oneline "$localHash..$remoteHash" 2>&1 | Select-Object -First 10 | ForEach-Object {
            Write-Host "    $_" -ForegroundColor White
        }
        $ErrorActionPreference = $prevEAP
        Write-Host ""
    }
}
catch {
    $ErrorActionPreference = $prevEAP
    Write-Warn "Could not check for updates: $_"
    Write-Detail "Continuing with rebuild..."
}

# ============================================================
# Step 3: Backup Current Installation
# ============================================================
Write-StepHeader -Step 3 -Total $totalSteps -Title "Backing Up Current Installation"

if ($SkipBackup) {
    Write-Detail "Backup skipped (--SkipBackup flag)"
}
else {
    if (-not (Test-Path $Script:BackupDir)) {
        New-Item -ItemType Directory -Path $Script:BackupDir -Force | Out-Null
    }

    $timestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
    $backupPath = Join-Path $Script:BackupDir "scanner-backup-$timestamp"

    Write-Detail "Creating backup at: $backupPath"

    New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
    Get-ChildItem $Script:InstallDir | Copy-Item -Destination $backupPath -Recurse -Force

    $backupSize = [math]::Round((Get-ChildItem $backupPath -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
    Write-Success "Backup created ($($backupSize) MB)"

    # Clean up old backups (keep last 5)
    $oldBackups = Get-ChildItem $Script:BackupDir -Directory | Sort-Object Name -Descending | Select-Object -Skip 5
    if ($oldBackups) {
        $oldBackups | Remove-Item -Recurse -Force
        Write-Detail "Cleaned up $($oldBackups.Count) old backup(s)"
    }
}

# ============================================================
# Step 4: Close App, Pull Updates & Rebuild
# ============================================================
Write-StepHeader -Step 4 -Total $totalSteps -Title "Updating & Rebuilding"

# Check if the app is running and close it
$wasRunning = $false
$runningProcess = Get-Process -Name "SmartLog.Scanner" -ErrorAction SilentlyContinue
if ($runningProcess) {
    $wasRunning = $true
    Write-Detail "Closing running Scanner App..."
    $runningProcess | Stop-Process -Force
    Start-Sleep -Seconds 2
    Write-Success "Scanner App closed"
}
else {
    Write-Detail "Scanner App is not running"
}

# Pull latest code
Write-Detail "Pulling latest changes from '$Branch'..."
$prevEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$pullOutput = git pull origin $Branch 2>&1
$pullExitCode = $LASTEXITCODE
$ErrorActionPreference = $prevEAP
if ($pullExitCode -ne 0) {
    Write-Fail "Git pull failed:"
    Write-Host $pullOutput -ForegroundColor Red
    Write-Host ""
    if ($wasRunning) {
        Write-Warn "Restarting Scanner App..."
        Start-Process -FilePath $exePath -WorkingDirectory $Script:InstallDir
    }
    Pop-Location
    Read-Host "  Press Enter to exit"
    exit 1
}
Write-Success "Code updated"

# Build & Publish
$projectDir = Join-Path $repoRoot "SmartLog.Scanner"
Write-Detail "Building in Release mode (this may take a minute)..."

$publishOutput = dotnet publish $projectDir `
    -f $Script:TargetFramework `
    -c $Script:Configuration `
    -o $Script:InstallDir `
    --nologo 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Fail "Build failed!"
    Write-Host ($publishOutput | Out-String) -ForegroundColor Red
    Write-Host ""
    if (-not $SkipBackup -and (Test-Path $backupPath)) {
        Write-Warn "Restoring from backup..."
        Copy-Item "$backupPath\*" $Script:InstallDir -Recurse -Force
        Write-Detail "Previous version restored from backup"
    }
    if ($wasRunning) {
        Write-Warn "Restarting Scanner App with previous version..."
        Start-Process -FilePath $exePath -WorkingDirectory $Script:InstallDir
    }
    Pop-Location
    Read-Host "  Press Enter to exit"
    exit 1
}
Write-Success "Application published to $($Script:InstallDir)"

# ============================================================
# Step 5: Relaunch App
# ============================================================
Write-StepHeader -Step 5 -Total $totalSteps -Title "Relaunching App"

if ($wasRunning) {
    Write-Detail "Relaunching Scanner App..."
    Start-Process -FilePath $exePath -WorkingDirectory $Script:InstallDir
    Start-Sleep -Seconds 3
    $newProcess = Get-Process -Name "SmartLog.Scanner" -ErrorAction SilentlyContinue
    if ($newProcess) {
        Write-Success "Scanner App is running"
    }
    else {
        Write-Warn "Scanner App may still be starting up"
        Write-Detail "Check the app window or logs at: %LOCALAPPDATA%\SmartLog.Scanner\logs\"
    }
}
else {
    $launch = Read-Host "  Launch Scanner App now? (Y/n)"
    if ($launch -notmatch '^[Nn]') {
        Start-Process -FilePath $exePath -WorkingDirectory $Script:InstallDir
        Write-Success "Scanner App launched"
    }
    else {
        Write-Detail "You can launch it later from the desktop shortcut or:"
        Write-Detail "  $exePath"
    }
}

Pop-Location

# ============================================================
# Summary
# ============================================================
$prevEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$currentHash = (git -C $repoRoot rev-parse --short HEAD 2>&1) | Select-Object -Last 1
$ErrorActionPreference = $prevEAP

Write-Host ""
Write-Host "  ======================================================" -ForegroundColor Green
Write-Host "       Update Complete!                                  " -ForegroundColor Green
Write-Host "  ======================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Version:    $currentHash" -ForegroundColor White
Write-Host "  Install:    $($Script:InstallDir)" -ForegroundColor White
Write-Host "  Executable: $exePath" -ForegroundColor White
Write-Host ""
Write-Host "  Logs:       %LOCALAPPDATA%\SmartLog.Scanner\logs\" -ForegroundColor Gray
Write-Host ""

if (-not $SkipBackup) {
    Write-Host "  Rollback (if needed):" -ForegroundColor Gray
    Write-Host "    1. Close the Scanner App" -ForegroundColor DarkGray
    Write-Host "    2. Copy-Item '$backupPath\*' '$($Script:InstallDir)' -Recurse -Force" -ForegroundColor DarkGray
    Write-Host "    3. Relaunch the app" -ForegroundColor DarkGray
    Write-Host ""
}

Read-Host "  Press Enter to exit"
