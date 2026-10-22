# Removes MonitorAlign for the current user. Pass -KeepSettings to keep your saved alignment.
param([switch]$KeepSettings)
Get-Process MonitorAlign -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name MonitorAlign -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'MonitorAlign.lnk') -ErrorAction SilentlyContinue
if (-not $KeepSettings) { Remove-Item (Join-Path $env:APPDATA 'MonitorAlign') -Recurse -ErrorAction SilentlyContinue }
# The script may live inside the install folder, so delete that folder from a separate process.
$dir = Join-Path $env:LOCALAPPDATA 'Programs\MonitorAlign'
Start-Process cmd -WindowStyle Hidden -ArgumentList "/c timeout /t 2 >nul & rd /s /q `"$dir`""
Write-Host 'MonitorAlign uninstalled.'
