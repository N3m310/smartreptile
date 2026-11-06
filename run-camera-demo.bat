@echo off
setlocal EnableDelayedExpansion
title SmartReptile - Camera demo (dien thoai PHAT + emulator XEM)
cd /d "%~dp0"

echo ============================================================
echo   SmartReptile - CAMERA DEMO
echo   dien thoai = may PHAT camera, emulator = may XEM
echo ============================================================
echo.
echo Yeu cau truoc khi chay:
echo   - Dien thoai cam USB, da bat USB debugging va bam Allow
echo   - Backend dang chay (http://localhost:8080/health/ready = Healthy)
echo   - Emulator "focus_study" (script tu bat neu chua chay)
echo.

set "BEARG="
set /p "STARTBE=Bat backend (docker db + dotnet run) trong cua so moi? (y/N): "
if /i "!STARTBE!"=="y" set "BEARG=-Backend"

set "TURN_URL="
set /p "TURN_URL=Nhap TURN URL (vd turn:192.168.1.20:3478) - bo trong neu 2 may cung Wi-Fi: "
set "TURN_USER="
set "TURN_PASS="
if defined TURN_URL (
  set /p "TURN_USER=TURN username: "
  set /p "TURN_PASS=TURN password: "
)

echo.
echo Dang chuan bi thiet bi, build 1 APK va cai len tat ca may...
echo.

if defined TURN_URL (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-mobile.ps1" -StartEmulator -Run both !BEARG! -TurnUrl "!TURN_URL!" -TurnUser "!TURN_USER!" -TurnPass "!TURN_PASS!"
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-mobile.ps1" -StartEmulator -Run both !BEARG!
)

echo.
echo ============================================================
echo   TREN DIEN THOAI (may PHAT):
echo     1. Dang nhap
echo     2. Terrarium - chon be - "Camera truc tiep"
echo     3. Icon camera goc phai - chon be - "Bat camera" - Allow quyen camera
echo     4. Giu nguyen man hinh, doi trang thai "Dang phat toi ..."
echo.
echo   TREN EMULATOR (may XEM):
echo     1. Dang nhap cung tai khoan
echo     2. "Camera truc tiep" - tab WebRTC - chon CUNG be nuoi
echo     3. Badge: DANG KET NOI - TRUC TIEP
echo ============================================================
echo.
echo Muon hot reload thi dung: run-mobile.bat -Run phone  /  -Run emulator
pause
exit /b 0
