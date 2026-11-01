@echo off
setlocal
title TERRAGUARD - E2E test runner
cd /d "%~dp0"

rem Double-click = menu. With an argument it skips the menu:
rem   run-e2e.bat smoke              (also: hermetic, fullstack, all, web)
rem   run-e2e.bat smoke focus_study headless    unattended, no emulator window
set SUITE=%~1
set AVD=%~2
set VIEW=%~3
if not "%SUITE%"=="" goto run

echo =======================================================
echo   TERRAGUARD / SmartReptile - E2E test runner
echo =======================================================
echo.
echo   Mobile (Flutter integration_test, needs an emulator)
echo     [1] smoke      no backend, every destination opens
echo     [2] hermetic   stub API inside the test process
echo     [3] fullstack  emulator + docker compose (runs alerts too)
echo     [4] all        smoke + hermetic
echo     [6] smoke, headless   same suite, no emulator window (CI-style)
echo.
echo   Web (Playwright, needs the API on http://127.0.0.1:8080)
echo     [5] web
echo.
echo     [0] exit
echo.
echo   The emulator window stays open for a watchable run: the app drives
echo   itself there, with a banner naming the suite, case and current step.
echo.
set /p CHOICE=Choose [0-6]:
if "%CHOICE%"=="1" set SUITE=smoke
if "%CHOICE%"=="2" set SUITE=hermetic
if "%CHOICE%"=="3" set SUITE=fullstack
if "%CHOICE%"=="4" set SUITE=all
if "%CHOICE%"=="5" goto web
if "%CHOICE%"=="6" (
  set SUITE=smoke
  set VIEW=headless
)
if "%CHOICE%"=="0" goto end

:run
if /i "%SUITE%"=="web" goto web
if "%SUITE%"=="" goto end
set EXTRA=
if not "%AVD%"=="" set EXTRA=-Avd %AVD%
if /i "%VIEW%"=="headless" set EXTRA=%EXTRA% -Headless

echo.
echo == Mobile E2E suite: %SUITE%
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\e2e.ps1 -Suite %SUITE% %EXTRA%
set CODE=%ERRORLEVEL%
echo.
echo == Exit code: %CODE%   (0 = passed, 1 = a test failed, 2 = missing prerequisite)
echo == Artifacts: build\e2e\   (see the .log of each suite)
goto end

:web
echo.
echo == Web E2E (Playwright). The API must answer /health/ready on port 8080.
pushd web
call npm run test:e2e
set CODE=%ERRORLEVEL%
popd
echo.
echo == Exit code: %CODE%
echo == Report: web\playwright-report\index.html   Summary: web\e2e\TEST-RESULTS.md
goto end

:end
echo.
pause
endlocal
