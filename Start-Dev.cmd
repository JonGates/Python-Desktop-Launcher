@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Install the .NET 10 SDK before running the source.
  pause
  exit /b 1
)
dotnet run --project "%~dp0src\ProjectLauncher.Desktop" -- --project "%~dp0."
set "result=%errorlevel%"
if not "%result%"=="0" pause
exit /b %result%
