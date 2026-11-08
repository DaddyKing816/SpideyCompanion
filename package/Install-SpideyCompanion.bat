@echo off
title Install Spidey Companion
set "DIR=%LOCALAPPDATA%\SpideyCompanion"
set "EXE=SpideyCompanion-v1.3.1.exe"
echo Installing Spidey Companion v1.3.1 to %DIR% ...
taskkill /IM "SpideyCompanion*.exe" /F >nul 2>nul
taskkill /IM SpiderCompanion.exe /F >nul 2>nul
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v SpiderCompanion /f >nul 2>nul
rd /s /q "%LOCALAPPDATA%\SpiderCompanion" 2>nul
mkdir "%DIR%" 2>nul
del /q "%DIR%\SpideyCompanion*.exe" >nul 2>nul
del /q "%DIR%\SpideyCompanion*.exe.config" >nul 2>nul
copy /Y "%~dp0%EXE%" "%DIR%\" >nul
copy /Y "%~dp0%EXE%.config" "%DIR%\" >nul 2>nul
if not exist "%DIR%\update-feed.txt" copy /Y "%~dp0update-feed.txt" "%DIR%\" >nul
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v SpideyCompanion /t REG_SZ /d "\"%DIR%\%EXE%\"" /f >nul
start "" "%DIR%\%EXE%"
echo.
echo Done. Spidey Companion v1.3.1 is running now - look for its
echo icon in the system tray, and say hi to your new desktop buddy.
echo It will start automatically with Windows.
echo To remove it, run Uninstall-SpideyCompanion.bat.
pause
