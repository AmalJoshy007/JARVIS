# Setup-TaskScheduler.ps1
# Configures a Windows Scheduled Task for JarvisBot to run at user log on with highest privileges

$publishExe = Join-Path $PSScriptRoot "..\publish\JarvisBot.exe"
$resolvedExe = [System.IO.Path]::GetFullPath($publishExe)
$workDir = Split-Path $resolvedExe

if (-not (Test-Path $resolvedExe)) {
    Write-Warning "Could not find $resolvedExe. Building and publishing first..."
    & dotnet publish (Join-Path $PSScriptRoot "..\JarvisBot.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o (Join-Path $PSScriptRoot "..\publish")
}

$taskName = "JarvisBotAutoStart"
$action = New-ScheduledTaskAction -Execute $resolvedExe -WorkingDirectory $workDir
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force

Write-Host "Successfully registered scheduled task '$taskName' to run at logon." -ForegroundColor Green
