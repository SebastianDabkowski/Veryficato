@echo off
setlocal
cd /d "%~dp0"
if exist "%~dp0browsers" set "PLAYWRIGHT_BROWSERS_PATH=%~dp0browsers"
"%~dp0Verificato.Host.exe" %*
