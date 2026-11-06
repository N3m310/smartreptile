@echo off
rem Windows blocks .ps1 files under the default ExecutionPolicy, so the runner is reached through this .cmd.
rem Same thing as run-mobile-e2e.bat at the repository root, for when you are already in app\.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run_e2e.ps1" %*
exit /b %ERRORLEVEL%
