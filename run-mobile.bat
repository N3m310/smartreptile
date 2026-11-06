@echo off
setlocal
cd /d "%~dp0"
echo ============================================================
echo   SmartReptile - Mobile setup (dien thoai USB + emulator)
echo ============================================================
echo.
echo   Chay khong tham so  : xem thiet bi + adb reverse
echo   -StartEmulator      : bat emulator (mac dinh focus_study)
echo   -Run phone          : chay app tren dien thoai (hot reload)
echo   -Run emulator       : chay app tren emulator (hot reload)
echo   -Run both           : build 1 lan, cai len ca hai may
echo   -Backend            : tu bat docker db + dotnet run
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-mobile.ps1" %*
echo.
pause
exit /b 0
