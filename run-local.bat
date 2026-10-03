@echo off
echo =======================================================
echo   TERRAGUARD / SmartReptile - Khoi dong he thong Local
echo =======================================================

start "SmartReptile Backend API (Port 8080)" cmd /k "cd backend && dotnet run --project src/SmartReptile.Api"
start "TERRAGUARD Web React (Port 8081)" cmd /k "cd web && npm run dev"

echo.
echo Dang khoi dong Backend tai: http://localhost:8080/swagger
echo Dang khoi dong Web React tai: http://localhost:8081
echo.
timeout /t 5
start http://localhost:8081

