$scriptPath = Join-Path $PSScriptRoot 'ClientHeartbeatSender.ps1'
$launcher = Join-Path $PSScriptRoot 'ClientHeartbeatLauncher.cmd'

if (-not (Test-Path $scriptPath)) {
    throw "ClientHeartbeatSender.ps1 was not found."
}

$taskName = 'SGRNetGuard-Heartbeat'
$action = New-ScheduledTaskAction -Execute $launcher -WorkingDirectory $PSScriptRoot
$triggers = @(
    (New-ScheduledTaskTrigger -AtStartup),
    (New-ScheduledTaskTrigger -AtLogOn)
)
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Seconds 0)

Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
try {
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $triggers -Settings $settings -Force -ErrorAction Stop | Out-Null
}
catch {
    Write-Error "Could not install the Scheduled Task. Run PowerShell as Administrator and try again: $($_.Exception.Message)"
    exit 1
}

Write-Host "Scheduled task installed successfully."
Write-Host "Task name: $taskName"
Write-Host "Script: $scriptPath"
Write-Host "Restart Windows or sign in again to run it."
