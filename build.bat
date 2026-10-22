@echo off
rem Builds MonitorAlign.exe with the C# compiler that ships with Windows (.NET Framework 4).
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize /out:MonitorAlign.exe ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll MonitorAlign.cs
