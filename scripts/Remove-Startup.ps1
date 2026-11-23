# Remove-Startup.ps1
# Removes both Startup folder shortcut and Scheduled Task if present

$startupFolder = [System.Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startupFolder "JarvisBot.lnk"
if (Test-Path $shortcutPath) {
    Remove-Item $shortcutPath -Force
    Write-Host "Removed startup shortcut: $shortcutPath" -ForegroundColor Yellow
}

$taskName = "JarvisBotAutoStart"
$existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($existingTask) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Write-Host "Unregistered scheduled task '$taskName'." -ForegroundColor Yellow
}

Write-Host "Cleanup completed." -ForegroundColor Green
