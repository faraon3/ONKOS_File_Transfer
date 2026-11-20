@echo off
setlocal EnableExtensions
title Instalador Permanente - ONKOS File Transfer
color 0A

:: 1. Auto-elevar permisos de Administrador al hacer doble clic
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Solicitando permisos de Administrador...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -WorkingDirectory '%~dp0' -Verb RunAs"
    exit /b
)

set "ORIGEN=%~dp0"
set "DESTINO=C:\ONKOS"
set "INBOX=C:\Users\Public\Desktop\Archivos Recibidos ONKOS"
set "INBOX_ICON=%DESTINO%\recibidos.ico"
set "START_MENU=C:\ProgramData\Microsoft\Windows\Start Menu\Programs"
set "STARTUP_ALL=C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup"
set "PUBLIC_DESKTOP=C:\Users\Public\Desktop"

echo ==================================================
echo   INSTALANDO ONKOS FILE TRANSFER EN EL EQUIPO
echo ==================================================
echo.

:: 2. Detener servicio y bandeja previos si ya existian
echo [1/8] Deteniendo instancias anteriores...
sc stop ONKOSTransferService >nul 2>&1
timeout /t 2 /nobreak >nul
sc delete ONKOSTransferService >nul 2>&1
taskkill /F /IM ONKOSFileTransfer.exe >nul 2>&1

:: 3. Copiar binarios permanentes a C:\ONKOS y desbloquear marca de descarga de Windows
echo [2/8] Instalando y desbloqueando archivos en %DESTINO%...
if not exist "%DESTINO%\Servicio" mkdir "%DESTINO%\Servicio"
if not exist "%DESTINO%\Interfaz" mkdir "%DESTINO%\Interfaz"
xcopy "%ORIGEN%Servicio\*" "%DESTINO%\Servicio\" /E /I /Y >nul
xcopy "%ORIGEN%Interfaz\*" "%DESTINO%\Interfaz\" /E /I /Y >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%DESTINO%' -Recurse | Unblock-File -ErrorAction SilentlyContinue" >nul 2>&1

if not exist "%DESTINO%\Servicio\ONKOS.TransferService.exe" (
    echo [ERROR] No se encontro Servicio\ONKOS.TransferService.exe junto al instalador.
    pause
    exit /b 1
)

:: 4. Crear carpeta directa en el Escritorio con el icono recibidos.ico
echo [3/8] Configurando carpeta de recibidos en el Escritorio...
if not exist "%INBOX%" mkdir "%INBOX%"
if exist "%ORIGEN%recibidos.ico" (
    copy /Y "%ORIGEN%recibidos.ico" "%INBOX_ICON%" >nul
)

attrib -h -s "%INBOX%\desktop.ini" >nul 2>&1
if exist "%INBOX_ICON%" (
    (
        echo [.ShellClassInfo]
        echo IconResource=%INBOX_ICON%,0
        echo InfoTip=Carpeta de llegada de archivos recibidos por ONKOS Transfer
    ) > "%INBOX%\desktop.ini"
    attrib +h +s "%INBOX%\desktop.ini" >nul 2>&1
    attrib +r "%INBOX%" >nul 2>&1
)

:: 5. Registrar e iniciar el servicio de Windows (arranca solo al prender el PC)
echo [4/8] Registrando servicio de Windows con inicio automatico...
sc create ONKOSTransferService binPath= "\"%DESTINO%\Servicio\ONKOS.TransferService.exe\"" start= auto DisplayName= "ONKOS Transfer Service" >nul
sc description ONKOSTransferService "Servicio receptor de archivos ONKOS en puerto TCP 5050." >nul
sc failure ONKOSTransferService reset= 86400 actions= restart/5000/restart/5000/restart/5000 >nul
sc start ONKOSTransferService >nul
timeout /t 2 /nobreak >nul

:: 6. Abrir puerto TCP 5050 en el Firewall de Windows
echo [5/8] Habilitando puerto TCP 5050 en Firewall...
netsh advfirewall firewall delete rule name="ONKOS Transfer Service (TCP 5050)" >nul 2>&1
netsh advfirewall firewall add rule name="ONKOS Transfer Service (TCP 5050)" dir=in action=allow protocol=TCP localport=5050 >nul

:: 7. Borrar accesos directos viejos de ONKOS/Recibidos y dejar solo el acceso del programa
echo [6/8] Limpiando duplicados y configurando accesos directos...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ws = New-Object -ComObject WScript.Shell; " ^
  "$appExe = '%DESTINO%\Interfaz\ONKOSFileTransfer.exe'; " ^
  "$appDir = '%DESTINO%\Interfaz'; " ^
  "$carpetasEsc = Get-ChildItem 'C:\Users' -Directory -ErrorAction SilentlyContinue | ForEach-Object { Join-Path $_.FullName 'Desktop' }; " ^
  "$carpetasEsc += '%PUBLIC_DESKTOP%'; " ^
  "foreach ($esc in ($carpetasEsc | Select-Object -Unique)) { " ^
  "  if (Test-Path $esc) { " ^
  "    Get-ChildItem -Path $esc -Filter '*.lnk' -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'ONKOS|Recibid' } | Remove-Item -Force -ErrorAction SilentlyContinue; " ^
  "  } " ^
  "}; " ^
  "$s1 = $ws.CreateShortcut('%START_MENU%\ONKOS File Transfer.lnk'); $s1.TargetPath = $appExe; $s1.WorkingDirectory = $appDir; $s1.Description = 'Enviar archivos por red local con ONKOS'; $s1.Save(); " ^
  "$s2 = $ws.CreateShortcut('%STARTUP_ALL%\ONKOS Tray.lnk'); $s2.TargetPath = $appExe; $s2.Arguments = '--tray'; $s2.WorkingDirectory = $appDir; $s2.Save(); " ^
  "$a = $ws.CreateShortcut('%PUBLIC_DESKTOP%\ONKOS File Transfer.lnk'); $a.TargetPath = $appExe; $a.WorkingDirectory = $appDir; $a.Description = 'Enviar archivos por red local con ONKOS'; $a.Save()" >nul

:: 8. Reforzar arranque automatico en el Registro de Windows
echo [7/8] Registrando inicio automatico al encender Windows...
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "ONKOSFileTransferTray" /t REG_SZ /d "\"%DESTINO%\Interfaz\ONKOSFileTransfer.exe\" --tray" /f >nul

:: 9. Refrescar iconos de Windows e iniciar la bandeja ahora mismo
echo [8/8] Refrescando iconos e iniciando bandeja de notificaciones...
ie4uinit.exe -show >nul 2>&1
taskkill /F /IM explorer.exe >nul 2>&1
start explorer.exe
timeout /t 2 /nobreak >nul
start "" "%DESTINO%\Interfaz\ONKOSFileTransfer.exe" --tray

echo.
echo ==================================================
echo   ONKOS INSTALADO DE FORMA PERMANENTE EN EL EQUIPO
echo ==================================================
sc query ONKOSTransferService | findstr "ESTADO STATE"
echo.
echo IPv4 de este equipo:
ipconfig | findstr /i "IPv4"
echo.
pause
