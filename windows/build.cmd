@echo off
rem Builds AudioRouterApps.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ ^
  /win32manifest:app.manifest /out:AudioRouterApps.exe ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll AudioRouterApps.cs || exit /b 1
.\AudioRouterApps.exe --selftest > selftest.log
set RC=%ERRORLEVEL%
type selftest.log
del selftest.log
exit /b %RC%
