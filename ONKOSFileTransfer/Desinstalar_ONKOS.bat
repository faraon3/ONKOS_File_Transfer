@echo off
chcp 65001 >nul
title Desinstalador Automatico - ONKOS File Transfer

:: --- AUTO-ELEVACION A ADMINISTRADOR ---
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo Desinstalando ONKOS File Transfer por completo...
echo.

:: 1. Cerrar procesos en ejecucion
taskkill /F /IM ONKOSFileTransfer.exe >nul 2>&1
taskkill /F /IM ONKOS.TransferService.exe >nul 2>&1

:: 2. Detener y eliminar servicio de Windows si existe
sc query ONKOSTransferService >nul 2>&1
if %errorlevel% equ 0 (
    sc stop ONKOSTransferService >nul 2>&1
    timeout /t 2 /nobreak >nul
    sc delete ONKOSTransferService >nul 2>&1
)

:: 3. Eliminar reglas de Firewall creadas por ONKOS
netsh advfirewall firewall delete rule name="ONKOS File Transfer" >nul 2>&1
netsh advfirewall firewall delete rule name="ONKOS File Transfer TCP 5050" >nul 2>&1
netsh advfirewall firewall delete rule name="ONKOS Discovery UDP 5051" >nul 2>&1

:: 4. Eliminar tarea programada o inicio automatico si existe
schtasks /Delete /TN "ONKOSFileTransfer" /F >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "ONKOSFileTransfer" /f >nul 2>&1
reg delete "HKLM\Software\Microsoft\Windows\CurrentVersion\Run" /v "ONKOSFileTransfer" /f >nul 2>&1

:: 5. Eliminar configuracion, contactos y carpetas del programa
if exist "%ProgramData%\ONKOS" rmdir /s /q "%ProgramData%\ONKOS"
if exist "%ProgramFiles%\ONKOS\FileTransfer" rmdir /s /q "%ProgramFiles%\ONKOS\FileTransfer"
if exist "%ProgramFiles%\ONKOS" rmdir /s /q "%ProgramFiles%\ONKOS"
if exist "%ProgramFiles(x86)%\ONKOS\FileTransfer" rmdir /s /q "%ProgramFiles(x86)%\ONKOS\FileTransfer"
if exist "%ProgramFiles(x86)%\ONKOS" rmdir /s /q "%ProgramFiles(x86)%\ONKOS"
if exist "%LocalAppData%\ONKOS" rmdir /s /q "%LocalAppData%\ONKOS"
if exist "%AppData%\ONKOS" rmdir /s /q "%AppData%\ONKOS"

:: 6. Eliminar accesos directos y carpeta de Archivos Recibidos ONKOS
if exist "%Public%\Desktop\ONKOS File Transfer 2.0.lnk" del /f /q "%Public%\Desktop\ONKOS File Transfer 2.0.lnk"
if exist "%Public%\Desktop\ONKOS File Transfer.lnk" del /f /q "%Public%\Desktop\ONKOS File Transfer.lnk"
if exist "%UserProfile%\Desktop\ONKOS File Transfer 2.0.lnk" del /f /q "%UserProfile%\Desktop\ONKOS File Transfer 2.0.lnk"
if exist "%UserProfile%\Desktop\ONKOS File Transfer.lnk" del /f /q "%UserProfile%\Desktop\ONKOS File Transfer.lnk"
if exist "%ProgramData%\Microsoft\Windows\Start Menu\Programs\ONKOS File Transfer 2.0" rmdir /s /q "%ProgramData%\Microsoft\Windows\Start Menu\Programs\ONKOS File Transfer 2.0"

if exist "%UserProfile%\Desktop\Archivos Recibidos ONKOS" rmdir /s /q "%UserProfile%\Desktop\Archivos Recibidos ONKOS"
if exist "%Public%\Desktop\Archivos Recibidos ONKOS" rmdir /s /q "%Public%\Desktop\Archivos Recibidos ONKOS"

echo Desinstalacion completa finalizada.
timeout /t 2 /nobreak >nul
exit /b 0
