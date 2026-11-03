@echo off
setlocal
title TERRAGUARD - Mobile E2E
cd /d "%~dp0"

rem Double-click = smoke + hermetic on the default AVD, emulator window open so the run can be watched.
rem   run-e2e-mobile.bat                          all (smoke + hermetic), visible emulator
rem   run-e2e-mobile.bat smoke
rem   run-e2e-mobile.bat hermetic
rem   run-e2e-mobile.bat fullstack                needs Docker Desktop running
rem   run-e2e-mobile.bat smoke fcm_pixel_36
rem   run-e2e-mobile.bat smoke focus_study headless   unattended, no window
set SUITE=%~1
if "%SUITE%"=="" set SUITE=all
set AVD=%~2
if "%AVD%"=="" set AVD=focus_study
set VIEW=%~3
if "%VIEW%"=="" set VIEW=visible
set VIEWFLAG=
if /i "%VIEW%"=="headless" set VIEWFLAG=-Headless

echo =======================================================
echo   TERRAGUARD / SmartReptile - Mobile E2E
echo =======================================================
echo.
echo   Suite : %SUITE%      (smoke ^| hermetic ^| fullstack ^| all)
echo   AVD   : %AVD%
echo   View  : %VIEW%        (visible ^| headless)
echo   Logs  : build\e2e\
echo.
if /i not "%VIEW%"=="headless" (
  echo   Watch the emulator window: the app runs the suite there, and the
  echo   banner at the top names the suite, the case and the step in flight.
  echo.
)

where flutter >nul 2>&1
if errorlevel 1 (
  echo [X] flutter is not on PATH. Add it, then run again.
  goto end
)

if /i "%SUITE%"=="fullstack" (
  where docker >nul 2>&1
  if errorlevel 1 (
    echo [!] Docker is not on PATH; the fullstack suite will refuse to start.
  )
)

echo == Starting (the emulator is booted by the script, watch this window for progress)...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\e2e.ps1 -Suite %SUITE% -Avd %AVD% %VIEWFLAG%
set CODE=%ERRORLEVEL%

echo.
echo == Exit code: %CODE%   (0 = passed, 1 = a test failed, 2 = missing prerequisite)
if exist build\e2e (
  echo == Artifacts in build\e2e\ :
  dir /b build\e2e
  echo.
  echo    .log  -^> full output of that suite
  echo    .json -^> per-test results
  echo    -fail.png / -logcat.txt -^> only when a suite failed
)

:end
echo.
pause
endlocal
