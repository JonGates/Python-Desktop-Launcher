@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Build.ps1" %*
set "result=%errorlevel%"
echo.
if not "%result%"=="0" echo Build failed. Read the first error above and README.md.
pause
exit /b %result%
