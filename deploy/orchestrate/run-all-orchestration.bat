@echo off
setlocal

set SCRIPT=C:\git\internal\pi-stomp\deployment\orchestrate\deploy-and-run.ps1

pwsh -File "%SCRIPT%" -UploadScripts
if errorlevel 1 exit /b %errorlevel%

pwsh -File "%SCRIPT%" -InspectHost
if errorlevel 1 exit /b %errorlevel%

pwsh -File "%SCRIPT%" -UninstallPowerShell
if errorlevel 1 exit /b %errorlevel%

pwsh -File "%SCRIPT%" -UninstallNetCore
if errorlevel 1 exit /b %errorlevel%

pwsh -File "%SCRIPT%" -InstallPowerShell
if errorlevel 1 exit /b %errorlevel%

pwsh -File "%SCRIPT%" -InstallNetCore
if errorlevel 1 exit /b %errorlevel%

echo All steps completed successfully.
endlocal