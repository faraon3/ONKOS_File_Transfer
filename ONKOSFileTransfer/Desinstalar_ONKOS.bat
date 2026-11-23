@echo off
chcp 65001 >nul
title Desinstalador Automatico - ONKOS File Transfer

net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo Desinstalando ONKOS File Transfer por completo...
echo.

taskkill /F /IM ONKOSFileTransfer.exe >nul 2>&1
taskkill /F /IM ONKOS.TransferService.exe >nul 2>&1

sc query ONKOSTransferService >nul 2>&1
if %errorlevel% equ 0 (
    sc stop ONKOSTransferService >nul 2>&1
    timeout /t 2 /nobreak >nul
    sc delete ONKOSTransferService >nul 2>&1
)

netsh advfirewall firewall delete rule name="ONKOS File Transfer" >nul 2>&1
netsh advfirewall firewall delete rule name="ONKOS File Transfer TCP 5050" >nul 2>&1
netsh advfirewall firewall delete rule name="ONKOS Transfer Service (TCP 5050)" >nul 2>&1
netsh advfirewall firewall delete rule name="ONKOS Discovery UDP 5051" >nul 2>&1

schtasks /Delete /TN "ONKOSFileTransfer" /F >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "ONKOSFileTransfer" /f >nul 2>&1
reg delete "HKLM\Software\Microsoft\Windows\CurrentVersion\Run" /v "ONKOSFileTransfer" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "ONKOSFileTransferTray" /f >nul 2>&1

if exist "C:\ONKOS" rmdir /s /q "C:\ONKOS"
if exist "%ProgramData%\ONKOS" rmdir /s /q "%ProgramData%\ONKOS"
if exist "%ProgramFiles%\ONKOS\FileTransfer" rmdir /s /q "%ProgramFiles%\ONKOS\FileTransfer"
if exist "%ProgramFiles%\ONKOS" rmdir /s /q "%ProgramFiles%\ONKOS"
if exist "%ProgramFiles(x86)%\ONKOS\FileTransfer" rmdir /s /q "%ProgramFiles(x86)%\ONKOS\FileTransfer"
if exist "%ProgramFiles(x86)%\ONKOS" rmdir /s /q "%ProgramFiles(x86)%\ONKOS"
if exist "%LocalAppData%\ONKOS" rmdir /s /q "%LocalAppData%\ONKOS"
if exist "%AppData%\ONKOS" rmdir /s /q "%AppData%\ONKOS"

if exist "%Public%\Desktop\ONKOS File Transfer 2.0.lnk" del /f /q "%Public%\Desktop\ONKOS File Transfer 2.0.lnk"
if exist "%Public%\Desktop\ONKOS File Transfer.lnk" del /f /q "%Public%\Desktop\ONKOS File Transfer.lnk"
if exist "%UserProfile%\Desktop\ONKOS File Transfer 2.0.lnk" del /f /q "%UserProfile%\Desktop\ONKOS File Transfer 2.0.lnk"
if exist "%UserProfile%\Desktop\ONKOS File Transfer.lnk" del /f /q "%UserProfile%\Desktop\ONKOS File Transfer.lnk"
if exist "%ProgramData%\Microsoft\Windows\Start Menu\Programs\ONKOS File Transfer 2.0" rmdir /s /q "%ProgramData%\Microsoft\Windows\Start Menu\Programs\ONKOS File Transfer 2.0"
if exist "%ProgramData%\Microsoft\Windows\Start Menu\Programs\ONKOS File Transfer.lnk" del /f /q "%ProgramData%\Microsoft\Windows\Start Menu\Programs\ONKOS File Transfer.lnk"
if exist "%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup\ONKOS Tray.lnk" del /f /q "%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup\ONKOS Tray.lnk"

if exist "%UserProfile%\Desktop\Archivos Recibidos ONKOS" (
    attrib -r -s -h "%UserProfile%\Desktop\Archivos Recibidos ONKOS\desktop.ini" >nul 2>&1
    attrib -r "%UserProfile%\Desktop\Archivos Recibidos ONKOS" >nul 2>&1
    rmdir /s /q "%UserProfile%\Desktop\Archivos Recibidos ONKOS"
)
if exist "%Public%\Desktop\Archivos Recibidos ONKOS" (
    attrib -r -s -h "%Public%\Desktop\Archivos Recibidos ONKOS\desktop.ini" >nul 2>&1
    attrib -r "%Public%\Desktop\Archivos Recibidos ONKOS" >nul 2>&1
    rmdir /s /q "%Public%\Desktop\Archivos Recibidos ONKOS"
)

echo Desinstalacion completa finalizada.
timeout /t 2 /nobreak >nul
exit /b 0
