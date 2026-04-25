#Requires -RunAsAdministrator
<#
.SYNOPSIS
    SmartLog Scanner App -- Reset Installation

.DESCRIPTION
    Removes the SmartLog Scanner installation so you can run
    Setup-SmartLogScanner.ps1 fresh:
    - Stops any running SmartLog.Scanner.exe process
    - Removes desktop shortcut
    - Removes startup shortcut (auto-launch on boot)
    - Optionally deletes the install directory (C:\SmartLogScanner)
    - Optionally deletes app data (logs, SQLite DB, secure storage)

    Does NOT delete the source code repository.

.NOTES
    Run as Administrator: .\Reset-SmartLogScanner.ps1
    Or use: Reset-SmartLogScanner.bat
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ============================================================
# Configuration
# ============================================================
$Script:AppName     = "SmartLog Scanner"
$Script:ExeName     = "SmartLog.Scanner.exe"
$Script:InstallDir  = "C:\SmartLogScanner"
$Script:AppDataDir  = Join-Path $env:LOCALAPPDATA "SmartLog.Scanner"

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

function Write-Detail {
    param([string]$Message)
    Write-Host "    $Message" -ForegroundColor Gray
}

function Read-YesNo {
    param([string]$Prompt, [bool]$Default = $false)
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
Write-Host "  ======================================================" -ForegroundColor Red
Write-Host "       SmartLog Scanner App -- Reset Installation        " -ForegroundColor Red
Write-Host "  ======================================================" -ForegroundColor Red
Write-Host ""
Write-Host "  This will remove the Scanner installation so you can" -ForegroundColor Gray
Write-Host "  run Setup-SmartLogScanner.ps1 again for a fresh install." -ForegroundColor Gray
Write-Host ""
Write-Host "  The following will be removed:" -ForegroundColor Yellow
Write-Host "    - Running SmartLog.Scanner.exe process (if any)" -ForegroundColor Yellow
Write-Host "    - Desktop shortcut (SmartLog Scanner.lnk)" -ForegroundColor Yellow
Write-Host "    - Startup shortcut (auto-launch on boot)" -ForegroundColor Yellow
Write-Host "    - Install directory (C:\SmartLogScanner) [optional]" -ForegroundColor Yellow
Write-Host "    - App data: logs, SQLite DB, secure storage [optional]" -ForegroundColor Yellow
Write-Host ""
Write-Host "  NOT removed: source code repository" -ForegroundColor Gray
Write-Host ""

if (-not (Read-YesNo "Are you sure you want to reset the installation?" $false)) {
    Write-Host ""
    Write-Host "  Reset cancelled." -ForegroundColor Gray
    Read-Host "  Press Enter to exit"
    exit 0
}

$totalSteps = 5

# ============================================================
# Step 1: Stop Running Process
# ============================================================
Write-StepHeader -Step 1 -Total $totalSteps -Title "Stopping Running App"

$processName = [System.IO.Path]::GetFileNameWithoutExtension($Script:ExeName)
$running = Get-Process -Name $processName -ErrorAction SilentlyContinue
if ($running) {
    Write-Detail "Stopping $($running.Count) instance(s) of $processName..."
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Write-Success "Process stopped"
}
else {
    Write-Detail "App not running, skipping"
}

# ============================================================
# Step 2: Remove Desktop Shortcut
# ============================================================
Write-StepHeader -Step 2 -Total $totalSteps -Title "Removing Desktop Shortcut"

$desktopPaths = @(
    [Environment]::GetFolderPath("CommonDesktopDirectory"),
    [Environment]::GetFolderPath("Desktop")
) | Select-Object -Unique

$shortcutFile = "$($Script:AppName).lnk"
$desktopRemoved = $false
foreach ($path in $desktopPaths) {
    $shortcutPath = Join-Path $path $shortcutFile
    if (Test-Path $shortcutPath) {
        Remove-Item $shortcutPath -Force
        Write-Success "Removed: $shortcutPath"
        $desktopRemoved = $true
    }
}
if (-not $desktopRemoved) {
    Write-Detail "No desktop shortcut found, skipping"
}

# ============================================================
# Step 3: Remove Startup Shortcut
# ============================================================
Write-StepHeader -Step 3 -Total $totalSteps -Title "Removing Startup Shortcut"

$startupPaths = @(
    [Environment]::GetFolderPath("CommonStartup"),
    [Environment]::GetFolderPath("Startup")
) | Select-Object -Unique

$startupRemoved = $false
foreach ($path in $startupPaths) {
    $shortcutPath = Join-Path $path $shortcutFile
    if (Test-Path $shortcutPath) {
        Remove-Item $shortcutPath -Force
        Write-Success "Removed: $shortcutPath"
        $startupRemoved = $true
    }
}
if (-not $startupRemoved) {
    Write-Detail "No startup shortcut found, skipping"
}

# ============================================================
# Step 4: Delete Install Directory
# ============================================================
Write-StepHeader -Step 4 -Total $totalSteps -Title "Install Directory"

if (Test-Path $Script:InstallDir) {
    if (Read-YesNo "Delete install directory ($($Script:InstallDir))?" $true) {
        try {
            Remove-Item $Script:InstallDir -Recurse -Force
            Write-Success "Deleted $($Script:InstallDir)"
        }
        catch {
            Write-Warn "Could not delete $($Script:InstallDir): $($_.Exception.Message)"
            Write-Detail "Make sure no app instance is running and try again."
        }
    }
    else {
        Write-Detail "Keeping $($Script:InstallDir)"
    }
}
else {
    Write-Detail "Install directory not found, skipping"
}

# ============================================================
# Step 5: Delete App Data (logs, DB, secure storage)
# ============================================================
Write-StepHeader -Step 5 -Total $totalSteps -Title "App Data"

if (Test-Path $Script:AppDataDir) {
    Write-Host "  App data location: $($Script:AppDataDir)" -ForegroundColor Gray
    Write-Host "  Contents: logs, smartlog-scanner.db, secure storage tokens" -ForegroundColor Gray
    Write-Host "  WARNING: deleting this clears API key, HMAC secret, queued scans, and history." -ForegroundColor Yellow
    if (Read-YesNo "Delete app data?" $false) {
        try {
            Remove-Item $Script:AppDataDir -Recurse -Force
            Write-Success "Deleted $($Script:AppDataDir)"
        }
        catch {
            Write-Warn "Could not delete $($Script:AppDataDir): $($_.Exception.Message)"
            Write-Detail "Make sure no app instance is running and try again."
        }
    }
    else {
        Write-Detail "Keeping app data (existing config and history will be reused on next launch)"
    }
}
else {
    Write-Detail "App data directory not found, skipping"
}

# ============================================================
# Summary
# ============================================================
Write-Host ""
Write-Host "  ======================================================" -ForegroundColor Green
Write-Host "       Reset Complete!                                   " -ForegroundColor Green
Write-Host "  ======================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  You can now run a fresh installation:" -ForegroundColor Gray
Write-Host "    Right-click Setup-SmartLogScanner.bat -> Run as administrator" -ForegroundColor White
Write-Host ""

Read-Host "  Press Enter to exit"
