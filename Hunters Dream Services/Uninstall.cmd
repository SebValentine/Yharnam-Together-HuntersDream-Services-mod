@echo off
rem Close the Yharnam Together launcher and the game first.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0shim\install.ps1" -Uninstall
echo.
pause
