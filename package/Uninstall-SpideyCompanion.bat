@echo off
title Uninstall Spidey Companion
taskkill /IM "SpideyCompanion*.exe" /F >nul 2>nul
taskkill /IM SpiderCompanion.exe /F >nul 2>nul
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v SpideyCompanion /f >nul 2>nul
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v SpiderCompanion /f >nul 2>nul
rd /s /q "%LOCALAPPDATA%\SpideyCompanion" 2>nul
rd /s /q "%LOCALAPPDATA%\SpiderCompanion" 2>nul
echo Spidey Companion has been stopped and removed.
pause
