@echo off
pwsh.exe -NoProfile -File "%~dp0Add-BuilderSimulator.ps1"
if errorlevel 1 pause
