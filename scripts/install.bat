@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%install.ps1"

if not exist "%PS_SCRIPT%" (
    echo ERROR: Could not find "%PS_SCRIPT%".
    exit /b 1
)

where powershell.exe >nul 2>nul
if errorlevel 1 (
    echo ERROR: Windows PowerShell is required to run this installer.
    exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
exit /b %ERRORLEVEL%
