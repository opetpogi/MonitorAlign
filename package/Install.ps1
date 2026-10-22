# Installs MonitorAlign for the current user (no admin needed).
$ErrorActionPreference = 'Stop'
$src  = Join-Path $PSScriptRoot 'MonitorAlign.exe'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\MonitorAlign'
$exe  = Join-Path $dest 'MonitorAlign.exe'

Get-Process MonitorAlign -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item $src $exe -Force
Copy-Item (Join-Path $PSScriptRoot 'Uninstall.ps1') $dest -Force
Unblock-File $exe

$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'MonitorAlign.lnk'))
$lnk.TargetPath = $exe
$lnk.Description = 'Align the mouse between monitors of different sizes'
$lnk.Save()

# Start with Windows (can be turned off in the app's settings).
Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name MonitorAlign -Value "`"$exe`""

Start-Process $exe
Write-Host "MonitorAlign installed to $dest and started. Double-click its tray icon to set your monitor sizes."
