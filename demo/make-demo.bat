@echo off
rem Builds the demo renderer and writes dist\MonitorAlign-demo.mp4 (needs ffmpeg on PATH).
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize /out:MakeDemo.exe /r:System.Drawing.dll MakeDemo.cs || exit /b 1
if not exist ..\dist mkdir ..\dist
.\MakeDemo.exe "..\dist\MonitorAlign-demo.mp4" %1
