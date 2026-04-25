@echo off
REM ============================================================
REM SmartLog Scanner App - Reset Installation Launcher
REM Removes shortcuts, install dir, and (optionally) app data
REM for a fresh setup.
REM Right-click this file and select "Run as administrator".
REM ============================================================

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: This script must be run as Administrator.
    echo Right-click and select "Run as administrator".
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Reset-SmartLogScanner.ps1"
pause
