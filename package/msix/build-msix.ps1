# Builds the Microsoft Store package (MSIX) for MonitorAlign.
#   .\build-msix.ps1            -> dist\MonitorAlign.msix (upload this in Partner Center; the Store signs it)
#   .\build-msix.ps1 -Register  -> installs the unpacked layout for testing (needs Developer Mode, no signing)
# Copy the three identity values from Partner Center > your app > Product identity.
param(
    [string]$Name = 'opetpogi.MonitorAlign',
    [string]$Publisher = 'CN=opetpogi',
    [string]$PublisherDisplayName = 'opetpogi',
    [string]$Version = '1.0.0.0',   # the Store needs the last number to be 0
    [switch]$Register
)
$ErrorActionPreference = 'Stop'
$root   = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$layout = Join-Path $root 'dist\msix'
$assets = Join-Path $layout 'Assets'

if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force $assets | Out-Null

# The app itself, built straight into the layout.
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize `
    /out:"$layout\MonitorAlign.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
    "$root\MonitorAlign.cs"
if ($LASTEXITCODE) { throw 'Build failed' }

# Manifest with the identity filled in.
$manifest = Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
foreach ($k in 'Name', 'Publisher', 'PublisherDisplayName', 'Version') {
    $manifest = $manifest.Replace("`$$k`$", [Security.SecurityElement]::Escape((Get-Variable $k -ValueOnly)))
}
Set-Content (Join-Path $layout 'AppxManifest.xml') $manifest -Encoding UTF8

# Logos: the tray icon (two monitors and the orange line), drawn on a 32-unit grid and scaled up.
Add-Type -AssemblyName System.Drawing
function Save-Logo([int]$size, [string]$file) {
    $bmp = New-Object Drawing.Bitmap $size, $size
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.Clear([Drawing.Color]::Transparent)
    $s = $size / 32.0
    $rect = { param($c, $x, $y, $w, $h) $g.FillRectangle((New-Object Drawing.SolidBrush $c), [float]($x * $s), [float]($y * $s), [float]($w * $s), [float]($h * $s)) }
    & $rect ([Drawing.Color]::FromArgb(52, 120, 200)) 1 6 17 13
    & $rect ([Drawing.Color]::FromArgb(120, 130, 145)) 20 9 11 9
    & $rect ([Drawing.Color]::Orange) 0 12 32 2
    & $rect ([Drawing.Color]::Gray) 7 21 5 4
    & $rect ([Drawing.Color]::Gray) 4 25 11 2
    $g.Dispose()
    $bmp.Save((Join-Path $assets $file), [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
Save-Logo 44  'Square44x44Logo.png'
Save-Logo 150 'Square150x150Logo.png'
Save-Logo 50  'StoreLogo.png'

if ($Register) {
    Get-Process MonitorAlign -ErrorAction SilentlyContinue | Stop-Process -Force
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml') -ForceApplicationShutdown
    Write-Host "Installed for testing. Start MonitorAlign from the Start menu. Remove it with: Get-AppxPackage $Name | Remove-AppxPackage"
    return
}

# makeappx.exe comes with the Windows SDK (or the Microsoft.Windows.SDK.BuildTools NuGet package).
$makeappx = @(Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue) +
            @(Get-ChildItem "$root\tools" -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue | Where-Object { $_.Directory.Name -eq 'x64' }) |
            Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { throw 'makeappx.exe not found. Install the Windows SDK, or unzip Microsoft.Windows.SDK.BuildTools into tools\.' }
$out = Join-Path $root 'dist\MonitorAlign.msix'
& $makeappx.FullName pack /o /d $layout /p $out
if ($LASTEXITCODE) { throw 'makeappx failed' }
Write-Host "Built $out. Upload it in Partner Center > Packages."
