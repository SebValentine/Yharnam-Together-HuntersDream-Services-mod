@echo off
rem Same as the switch in the launcher's Mods page. Close the launcher first.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0shim\turn.ps1" -Mode on
echo.
pause
