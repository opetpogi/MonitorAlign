# MonitorAlign

A small Windows tray app for monitors of different physical sizes. It makes the mouse cross between them at the same physical height, and can span a wallpaper so the picture lines up across them too.

Windows lines monitors up by pixels. With a 27" and a 23" monitor that are both 1920×1080, a pixel row on the 27" sits at a different height than the same row on the 23". So the pointer jumps up or down when it crosses, and a spanned wallpaper doesn't line up. MonitorAlign converts positions to millimetres instead.

## Features

- **Mouse alignment.** The pointer crosses to the other monitor at the same physical height.
- **Optional edge stop.** Where the bigger monitor sticks out above or below the smaller one, the pointer stops at the edge instead of jumping to the nearest corner.
- **Guide lines.** Click-through lines every 20 mm. Drag a monitor in Settings until the lines run straight across the gap between the screens.
- **Aligned wallpaper.** Pick a picture and each monitor shows the part that is physically behind it. It's set with Windows "Span" and rebuilt when the alignment or the screen layout changes.
- **Remembers each monitor** by its hardware ID, even while it's switched off.
- One small `.exe` with no dependencies. It runs on Windows 10/11 with the .NET Framework that comes with Windows, and needs no admin rights.

## Install

1. Download `MonitorAlign.zip` from [Releases](../../releases) and unzip it.
2. Double-click **Install.bat**. It copies the app to `%LOCALAPPDATA%\Programs\MonitorAlign`, adds a Start menu shortcut, sets it to start with Windows, and starts it.
3. Double-click the tray icon, enter each monitor's diagonal size, then line them up with the guide lines.

To remove it, run **Uninstall.bat**. Settings are stored in `%APPDATA%\MonitorAlign\settings.txt`.

The app isn't code-signed, so SmartScreen may warn about it. Choose **More info → Run anyway**.

## Tips

- In Windows **Display settings**, place the monitors level with each other so the mouse can cross anywhere along the edge.
- For the wallpaper, drag the monitors apart in the preview by about the width of the two bezels. The picture then steps over the bezels instead of losing a strip behind them.

## Build from source

```bat
build.bat
```

This uses the C# compiler that ships with Windows (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`).
The installer scripts are in `package/`. `demo\make-demo.bat` renders the demo video and needs `ffmpeg` on PATH.

## How it works

- A low-level mouse hook (`WH_MOUSE_LL`) watches for the pointer leaving one monitor for another. It converts the crossing point to millimetres using each monitor's size and position, then moves the pointer with `SetCursorPos`.
- The process is per-monitor DPI aware, so every coordinate is in real pixels.
- The wallpaper is rendered at the size of the whole virtual desktop. Each monitor's area is cropped from the picture at its physical position.
