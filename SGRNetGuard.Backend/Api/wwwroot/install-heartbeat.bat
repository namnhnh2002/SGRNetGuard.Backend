@echo off
REM SGR NetGuard Heartbeat Agent - Auto Installer (Batch Version)
REM Right-click as Administrator and run, or run from cmd as Administrator
REM This script downloads and installs heartbeat service automatically

setlocal enabledelayedexpansion

REM Check if running as Administrator
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo ╔════════════════════════════════════════════════════╗
    echo ║  ERROR: This script must run as Administrator!    ║
    echo ╚════════════════════════════════════════════════════╝
    echo.
    echo Please right-click this file and select:
    echo   "Run as Administrator"
    echo.
    pause
    exit /b 1
)

echo.
echo ╔════════════════════════════════════════════════════╗
echo ║  SGR NetGuard Heartbeat - Auto Installer        ║
echo ╚════════════════════════════════════════════════════╝
echo.

REM Settings
set "INSTALL_PATH=C:\SGRNetGuard\Heartbeat"
set "PACKAGE_FILE=%~dp0SGRNetGuard-Heartbeat-Agent-v20260915.1.0.3.zip"
set "TASK_NAME=SGRNetGuard-Heartbeat"

REM Check if package file exists in current directory
if exist "%PACKAGE_FILE%" (
    echo [+] Found package: %PACKAGE_FILE%
) else (
    echo [-] ERROR: Package not found in: %PACKAGE_FILE%
    echo.
    echo Make sure SGRNetGuard-Heartbeat-Agent-v20260915.1.0.3.zip
    echo is in the same folder as this script.
    echo.
    pause
    exit /b 1
)

REM Create install directory
echo [+] Creating install directory...
if not exist "%INSTALL_PATH%" mkdir "%INSTALL_PATH%"
if not exist "%INSTALL_PATH%" (
    echo [-] Failed to create directory: %INSTALL_PATH%
    pause
    exit /b 1
)

REM Extract zip to install path
echo [+] Extracting package...
cd /d "%INSTALL_PATH%"
powershell -NoProfile -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::ExtractToDirectory('%PACKAGE_FILE%', '%INSTALL_PATH%', $true)"

if %errorlevel% neq 0 (
    echo [-] Failed to extract package
    cd /d "%~dp0"
    pause
    exit /b 1
)

echo [+] Package extracted successfully

REM Remove old scheduled task if exists
echo [+] Removing old task (if exists)...
taskkill /f /im powershell.exe 2>nul
schtasks /delete /tn "%TASK_NAME%" /f 2>nul

REM Create new scheduled task
echo [+] Creating scheduled task...
set "LAUNCHER=%INSTALL_PATH%\ClientHeartbeatLauncher.cmd"

REM Task at startup
schtasks /create /tn "%TASK_NAME%" /tr "%LAUNCHER%" /sc onstart /ru SYSTEM /f /rl highest 2>nul

REM Task at logon
schtasks /create /tn "%TASK_NAME%" /tr "%LAUNCHER%" /sc onlogon /f /rl highest 2>nul

if %errorlevel% neq 0 (
    echo [-] Failed to create scheduled task
    cd /d "%~dp0"
    pause
    exit /b 1
)

echo [+] Scheduled task created successfully

REM Run heartbeat manually to verify
echo [+] Testing heartbeat...
call "%LAUNCHER%" >nul 2>&1
timeout /t 3 /nobreak

echo.
echo ╔════════════════════════════════════════════════════╗
echo ║         ✓ INSTALLATION SUCCESSFUL!               ║
echo ╚════════════════════════════════════════════════════╝
echo.
echo [*] Next steps:
echo     • Restart Windows or log off/log on
echo     • Heartbeat will run automatically every 60 seconds
echo     • Machine will be marked as isInternal=true
echo.
echo [*] To verify:
echo     • Open Task Scheduler
echo     • Find task: %TASK_NAME%
echo     • Check log: %INSTALL_PATH%\heartbeat-error.log
echo.
pause
cd /d "%~dp0"
exit /b 0
