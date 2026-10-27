@echo off
rem Shows whether the mod is on and whether The Hunter's Dream answers right now. Changes nothing.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0shim\turn.ps1" -Mode status
echo.
pause
