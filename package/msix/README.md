# Microsoft Store package (MSIX)

`build-msix.ps1` builds `dist\MonitorAlign.msix` for the Microsoft Store. You don't sign it yourself: the Store signs it, which also gets rid of the SmartScreen warning.

## Build

1. In Partner Center, open the app and go to **Product management → Product identity**. Copy these three values:
   - `Package/Identity/Name`
   - `Package/Identity/Publisher` (looks like `CN=XXXXXXXX-...`)
   - `Package/Properties/PublisherDisplayName`
2. Run:

   ```powershell
   .\package\msix\build-msix.ps1 -Name '<Name>' -Publisher '<Publisher>' -PublisherDisplayName '<PublisherDisplayName>' -Version 1.0.0.0
   ```

   This needs `makeappx.exe`. Get it from the Windows SDK, or unzip the `Microsoft.Windows.SDK.BuildTools` NuGet package into `tools\`.
3. Upload `dist\MonitorAlign.msix` under **Packages** in your Store submission. Raise `-Version` for every update; the last number must stay `0`.

## Test locally without signing

With Developer Mode on (Settings → System → For developers):

```powershell
.\package\msix\build-msix.ps1 -Register
```

This installs the app from `dist\msix` the way the Store would. Remove it again with:

```powershell
Get-AppxPackage opetpogi.MonitorAlign | Remove-AppxPackage
```

## How the Store version differs

- **Start with Windows** uses the startup task in the manifest instead of the registry Run key. Registry writes are virtualized inside a package, so the Run key wouldn't work. It's on by default after the first launch, and users turn it off in **Settings → Apps → Startup**. The settings window links there.
- **Settings**: the Store version starts from the zip version's `%APPDATA%\MonitorAlign\settings.txt` if there is one. After that it saves to its own copy inside the package, so later changes aren't shared between the two.
- **Wallpaper** is set through Explorer's `IDesktopWallpaper` and written to the real path of the package's AppData folder, so Explorer can read it.
