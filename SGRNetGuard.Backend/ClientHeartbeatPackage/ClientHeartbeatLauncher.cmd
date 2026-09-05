@echo off
setlocal
set SCRIPT_DIR=%~dp0
set API_URL=https://sgrnetguard-backend.onrender.com
if not "%SGR_NETGUARD_API_URL%"=="" set API_URL=%SGR_NETGUARD_API_URL%
powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%SCRIPT_DIR%ClientHeartbeatSender.ps1" -ApiUrl "%API_URL%"
