@echo off
setlocal EnableExtensions
title Desinstalador - ONKOS File Transfer
color 0C

:: 1. Auto-elevar permisos de Administrador al hacer doble clic
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Solicitando permisos de Administrador para desinstalar...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -WorkingDirectory '%~dp0' -Verb RunAs"
    exit /b
)

echo ==================================================
echo   DESINSTALANDO ONKOS FILE TRANSFER DEL EQUIPO
echo ==================================================
echo.

:: 2. Detener y eliminar el servicio de Windows
echo [1/6] Deteniendo y eliminando servicio de Windows...
sc stop ONKOSTransferService >nul 2>&1
timeout /t 2 /nobreak >nul
sc delete ONKOSTransferService >nul 2>&1

:: 3. Cerrar la aplicacion de bandeja e interfaz si estan abiertas
echo [2/6] Cerrando aplicacion en ejecucion...
taskkill /F /IM ONKOSFileTransfer.exe >nul 2>&1

:: 4. Eliminar entradas de inicio automatico
echo [3/6] Quitando inicio automatico de Windows...
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "ONKOSFileTransferTray" /f >nul 2>&1
del /F /Q "C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup\ONKOS Tray.lnk" >nul 2>&1

:: 5. Eliminar regla del Firewall
echo [4/6] Cerrando puerto TCP 5050 en Firewall...
netsh advfirewall firewall delete rule name="ONKOS Transfer Service (TCP 5050)" >nul 2>&1

:: 6. Borrar accesos directos del Escritorio y Menu Inicio
echo [5/6] Eliminando accesos directos...
del /F /Q "C:\ProgramData\Microsoft\Windows\Start Menu\Programs\ONKOS File Transfer.lnk" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$carpetasEsc = Get-ChildItem 'C:\Users' -Directory -ErrorAction SilentlyContinue | ForEach-Object { Join-Path $_.FullName 'Desktop' }; " ^
  "$carpetasEsc += 'C:\Users\Public\Desktop'; " ^
  "foreach ($esc in ($carpetasEsc | Select-Object -Unique)) { " ^
  "  if (Test-Path $esc) { " ^
  "    Get-ChildItem -Path $esc -Filter '*.lnk' -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'ONKOS|Recibid' } | Remove-Item -Force -ErrorAction SilentlyContinue; " ^
  "  } " ^
  "}" >nul

:: 7. Eliminar carpeta de instalacion C:\ONKOS
echo [6/6] Eliminando archivos del programa en C:\ONKOS...
timeout /t 1 /nobreak >nul
rmdir /S /Q "C:\ONKOS" >nul 2>&1

echo.
echo ==================================================
echo   ONKOS FILE TRANSFER HA SIDO DESINSTALADO
echo ==================================================
echo Nota: La carpeta C:\ProgramData\ONKOS\Inbox se conservo
echo por seguridad para no borrar los archivos recibidos.
echo.
pause