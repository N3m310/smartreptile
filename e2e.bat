@echo off
setlocal
cd /d "%~dp0"

echo ============================================================
echo   SmartReptile - Mobile E2E
echo   chay integration_test  -^>  TEST-RESULTS.md + report.html
echo ============================================================
echo   Chay duoc tu bat ky thu muc nao. Vi du:
echo     e2e.bat                    emulator-5554, tu mo dashboard khi xong
echo     e2e.bat -NoOpen            chi sinh file, khong mo trinh duyet
echo     e2e.bat -Device ^<serial^>  chay tren dien thoai USB
echo     e2e.bat -ApiBase http://127.0.0.1:8080
echo.
echo   Can: backend dang chay (http://localhost:8080/health/ready = Healthy)
echo   Ket qua: app\integration_test\TEST-RESULTS.md
echo            app\build\e2e\report.html   (dashboard, mo bang trinh duyet)
echo ============================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0app\tool\run_e2e.ps1" %*
set "CODE=%ERRORLEVEL%"

echo.
if not "%CODE%"=="0" (
  echo [THAT BAI] Mo dashboard de xem test nao hong: app\build\e2e\report.html
) else (
  echo [OK] Dashboard: app\build\e2e\report.html
  echo      Bang Markdown: app\integration_test\TEST-RESULTS.md
)

rem Double-click (no arguments) keeps the window open so the result is readable.
if "%~1"=="" pause
exit /b %CODE%
