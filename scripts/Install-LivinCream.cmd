@echo off
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-LivinCream.ps1"
if errorlevel 1 echo Setup did not finish. Review the error above and retry.
pause
