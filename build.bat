@echo off
REM Requires the .NET 8 SDK / .NET 8 SDK gerekir: https://dotnet.microsoft.com/download/dotnet/8.0
cd /d "%~dp0src\TrackPadForWindows"
dotnet publish -c Release -r win-x64 --self-contained true -p:DebugType=none -o "%~dp0publish\TrackPadForWindows"
if errorlevel 1 (
  echo Build failed / Derleme basarisiz.
  pause
  exit /b 1
)
echo.
echo Done / Hazir: %~dp0publish\TrackPadForWindows\TrackPadForWindows.exe
pause
