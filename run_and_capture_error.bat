@echo off
echo Starting ObsMCLauncher...
echo.
.\bin\Debug\net8.0-windows\ObsMCLauncher.exe 2>&1
echo.
echo Exit code: %ERRORLEVEL%
echo.
pause

