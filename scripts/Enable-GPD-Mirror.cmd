@echo off
setlocal
where pwsh.exe >nul 2>nul
if not errorlevel 1 (
  set "HC_SETUP_PWSH=pwsh.exe"
  goto run
)
if exist "%ProgramFiles%\PowerShell\7\pwsh.exe" (
  set "HC_SETUP_PWSH=%ProgramFiles%\PowerShell\7\pwsh.exe"
  goto run
)
if exist "%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe" (
  set "HC_SETUP_PWSH=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe"
  goto run
)
echo PowerShell 7 was not found. Install it or ask for help on GPD-PC.
pause
exit /b 1
:run
"%HC_SETUP_PWSH%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Enable-GPD-Mirror.ps1"
set "HC_SETUP_RESULT=%ERRORLEVEL%"
echo.
pause
exit /b %HC_SETUP_RESULT%
