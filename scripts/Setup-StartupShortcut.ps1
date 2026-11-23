# Setup-StartupShortcut.ps1
# Creates a shortcut to published JarvisBot.exe in the current user's Startup folder

$publishExe = Join-Path $PSScriptRoot "..\publish\JarvisBot.exe"
$resolvedExe = [System.IO.Path]::GetFullPath($publishExe)

if (-not (Test-Path $resolvedExe)) {
    Write-Warning "Could not find $resolvedExe. Building and publishing first..."
    & dotnet publish (Join-Path $PSScriptRoot "..\JarvisBot.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o (Join-Path $PSScriptRoot "..\publish")
}

$startupFolder = [System.Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startupFolder "JarvisBot.lnk"

$wshShell = New-Object -ComObject WScript.Shell
$shortcut = $wshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $resolvedExe
$shortcut.WorkingDirectory = Split-Path $resolvedExe
$shortcut.Description = "J.A.R.V.I.S. Local Voice Assistant"
$shortcut.Save()

Write-Host "Successfully created startup shortcut at: $shortcutPath" -ForegroundColor Green
