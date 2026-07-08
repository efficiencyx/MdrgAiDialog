@echo off
setlocal

call "%~dp0install.bat" -LaunchGame %*
exit /b %ERRORLEVEL%
