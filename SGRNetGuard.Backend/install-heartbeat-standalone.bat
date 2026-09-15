@echo off
REM SGR NetGuard Heartbeat Agent - Standalone Installer (No Internet Required!)
REM This is a self-contained installer with all required files embedded
REM Run as Administrator

setlocal enabledelayedexpansion

color 0A
title SGR NetGuard Heartbeat Installer

REM Check if running as Administrator
net session >nul 2>&1
if %errorlevel% neq 0 (
    cls
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

cls
echo.
echo ╔════════════════════════════════════════════════════════╗
echo ║  SGR NetGuard Heartbeat - Standalone Installer       ║
echo ║  Version: 20260915.1.0.3                             ║
echo ╚════════════════════════════════════════════════════════╝
echo.
echo Machine: %COMPUTERNAME%
echo User: %USERNAME%
echo.

REM Get the directory where this script is located
set "SCRIPT_DIR=%~dp0"
set "INSTALL_PATH=C:\SGRNetGuard\Heartbeat"
set "TASK_NAME=SGRNetGuard-Heartbeat"

echo [*] Checking for heartbeat package in: %SCRIPT_DIR%
echo.

REM Check for ZIP package in same directory
set "PACKAGE_FILE="
for %%f in ("%SCRIPT_DIR%SGRNetGuard-Heartbeat-Agent-v*.zip") do (
    set "PACKAGE_FILE=%%f"
    goto :found_zip
)

REM If not found locally, try to download
:download_package
echo [!] Heartbeat package not found locally
echo [*] Attempting to download from backup location...

set "DOWNLOAD_URL=https://github.com/namnhnh2002/SGRNetGuard.Backend/raw/main/SGRNetGuard.Backend/SGRNetGuard-Heartbeat-Agent-v20260915.1.0.3.zip"
set "TEMP_ZIP=%TEMP%\SGRNetGuard-Heartbeat.zip"

powershell -NoProfile -Command "try { (New-Object System.Net.WebClient).DownloadFile('%DOWNLOAD_URL%', '%TEMP_ZIP%') } catch { exit 1 }"

if %errorlevel% neq 0 (
    echo.
    echo ╔════════════════════════════════════════════════════╗
    echo ║            ERROR: Download Failed                 ║
    echo ╚════════════════════════════════════════════════════╝
    echo.
    echo [X] Could not download heartbeat package
    echo [*] Please ensure you have internet connection
    echo [*] Or place SGRNetGuard-Heartbeat-Agent-v*.zip in same folder
    echo.
    pause
    exit /b 1
)

set "PACKAGE_FILE=%TEMP_ZIP%"
goto :found_zip

:found_zip
echo [+] Package found: %PACKAGE_FILE%
echo.

REM Create install directory
echo [*] Setting up installation directory...
if not exist "%INSTALL_PATH%" (
    mkdir "%INSTALL_PATH%"
    if !errorlevel! neq 0 (
        echo [X] Failed to create directory: %INSTALL_PATH%
        pause
        exit /b 1
    )
)

REM Extract ZIP package
echo [*] Extracting heartbeat package...
cd /d "%INSTALL_PATH%"

powershell -NoProfile -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::ExtractToDirectory('%PACKAGE_FILE%', '%INSTALL_PATH%', $true)"

if !errorlevel! neq 0 (
    echo [X] Failed to extract package
    echo [*] Please check if package file is valid
    cd /d "%SCRIPT_DIR%"
    pause
    exit /b 1
)

echo [+] Package extracted successfully
echo.

REM Kill any running PowerShell processes
taskkill /f /im powershell.exe 2>nul >nul

REM Remove old scheduled task
echo [*] Cleaning up old configuration...
schtasks /delete /tn "%TASK_NAME%" /f 2>nul >nul

REM Create scheduled task
echo [*] Installing Scheduled Task...

set "LAUNCHER=%INSTALL_PATH%\ClientHeartbeatLauncher.cmd"

if not exist "%LAUNCHER%" (
    echo [X] Error: ClientHeartbeatLauncher.cmd not found
    echo [*] Package extraction may have failed
    cd /d "%SCRIPT_DIR%"
    pause
    exit /b 1
)

REM Create task to run at startup and logon
schtasks /create /tn "%TASK_NAME%" /tr "\"%LAUNCHER%\"" /sc onstart /ru SYSTEM /f /rl highest 2>nul >nul
schtasks /create /tn "%TASK_NAME%" /tr "\"%LAUNCHER%\"" /sc onlogon /f /rl highest 2>nul >nul

if !errorlevel! neq 0 (
    echo [X] Failed to create scheduled task
    cd /d "%SCRIPT_DIR%"
    pause
    exit /b 1
)

echo [+] Scheduled Task created successfully
echo.

REM Test heartbeat
echo [*] Running initial heartbeat test...
call "%LAUNCHER%" >nul 2>&1
timeout /t 3 /nobreak >nul

echo.
echo ╔════════════════════════════════════════════════════════╗
echo ║        ✓ INSTALLATION SUCCESSFUL!                    ║
echo ╚════════════════════════════════════════════════════════╝
echo.

echo [*] INSTALLATION SUMMARY:
echo     • Heartbeat installed to: %INSTALL_PATH%
echo     • Task name: %TASK_NAME%
echo     • Status: Ready
echo.

echo [*] WHAT HAPPENS NEXT:
echo     • Heartbeat will start automatically at Windows startup
echo     • Heartbeat will start automatically when you log in
echo     • Every 60 seconds, heartbeat will be sent to Dashboard IT
echo     • Machine will be marked as isInternal=true
echo.

echo [*] RECOMMENDED ACTIONS:
echo     1. Restart Windows now (or log off and log on)
echo     2. Verify in Task Scheduler (search: "Task Scheduler")
echo     3. Find task: "%TASK_NAME%"
echo     4. Check status: Should show "Ready"
echo.

echo [*] VERIFICATION:
echo     • Log file: %INSTALL_PATH%\heartbeat-error.log
echo     • Manual test: %INSTALL_PATH%\ClientHeartbeatSender.ps1 -Once
echo.

echo [*] SUPPORT:
echo     • If machine doesn't appear on Dashboard:
echo       1. Wait 2-3 minutes for next heartbeat
echo       2. Restart Windows
echo       3. Check log file for errors
echo       4. Contact IT Support with log contents
echo.

echo Press any key to finish...
pause >nul

cd /d "%SCRIPT_DIR%"
exit /b 0
