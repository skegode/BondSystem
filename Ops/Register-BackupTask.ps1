# Register-BackupTask.ps1
# Run this ONCE, as Administrator, directly on the SQL Server box (servicesuiteai\SQLEXPRESS01),
# to register the daily backup as a Windows Scheduled Task. Requires Backup-OnwardsSwiftDB.ps1
# to be present in the same folder as this script (copy the whole Ops\ folder onto that server).

$scriptPath = Join-Path $PSScriptRoot "Backup-OnwardsSwiftDB.ps1"

if (-not (Test-Path $scriptPath)) {
    throw "Backup-OnwardsSwiftDB.ps1 not found next to this script at: $scriptPath"
}

$action = New-ScheduledTaskAction -Execute "powershell.exe" `
    -Argument "-ExecutionPolicy Bypass -File `"$scriptPath`""

$trigger = New-ScheduledTaskTrigger -Daily -At 2:00AM

$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopOnIdleEnd

Register-ScheduledTask -TaskName "OnwardsSwiftDB Daily Backup" `
    -Action $action -Trigger $trigger -Settings $settings `
    -RunLevel Highest -Force

Write-Host "Registered 'OnwardsSwiftDB Daily Backup' -- runs daily at 02:00, 14-day retention."
Write-Host "Test it now with: Start-ScheduledTask -TaskName 'OnwardsSwiftDB Daily Backup'"
