@echo off
title TERRAGUARD Mobile (Flutter)
echo =======================================================
echo   TERRAGUARD / SmartReptile - Khoi dong Mobile Flutter
echo =======================================================
echo.
echo [1/3] Dang khoi dong Android Emulator (Pixel 8)...
start "Android Emulator" "D:\Android\Sdk\emulator\emulator.exe" -avd SmartReptile_Pixel8

echo.
echo [2/3] Dang cho may ao khoi dong xong (adb wait-for-device)...
adb wait-for-device
echo May ao da san sang!
echo.

echo [3/3] Dang khoi chay ung dung Flutter len may ao...
cd app
flutter run -d emulator-5554

pause
