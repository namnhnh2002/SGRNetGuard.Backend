SGR NetGuard Heartbeat Agent
============================

This package sends the user computer heartbeat to:
https://sgrnetguard-backend.onrender.com

Install
-------
1. Extract this folder to a permanent location, for example:
   C:\SGRNetGuard\Heartbeat
2. Open PowerShell as Administrator.
3. Change to this folder and run:
   Set-ExecutionPolicy -Scope Process Bypass
   .\Install-ClientHeartbeatTask.ps1
4. Start the task immediately:
   Start-ScheduledTask -TaskName "SGRNetGuard-Heartbeat"

Verify
------
The task runs at Windows startup and user logon. It sends a heartbeat every 60 seconds.
The script does not require api.ipify.org; public IP lookup cannot block heartbeat delivery.

To test one heartbeat manually:
   powershell -NoProfile -ExecutionPolicy Bypass -File .\ClientHeartbeatSender.ps1 -Once

If delivery fails, inspect:
   .\heartbeat-error.log

Important
---------
This is the heartbeat agent package, not the full SGR NetGuard desktop application installer.
The existing desktop app remains unchanged. The dashboard shows a computer Online only after
receiving a recent heartbeat from this agent or the integrated TelemetryClient.
