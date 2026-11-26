@echo off
title Compilador Local - ONKOS File Transfer 2.0

set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

echo [1/3] Compilando Interfaz Grafica (Release)...
dotnet build ONKOSFileTransfer\ONKOSFileTransfer.csproj -c Release
if errorlevel 1 (
    echo ERROR compilando ONKOSFileTransfer.
    pause
    exit /b 1
)

echo.
echo [2/3] Compilando Servicio de Windows (Release)...
dotnet build ONKOSTransferService\ONKOSTransferService.csproj -c Release
if errorlevel 1 (
    echo ERROR compilando ONKOSTransferService.
    pause
    exit /b 1
)

echo.
echo [3/3] Generando Instalador con Inno Setup...
"%ISCC%" ONKOS_Setup.iss
if errorlevel 1 (
    echo ERROR generando el instalador.
    pause
    exit /b 1
)

echo.
echo ====================================================
echo  LISTO! Instalador generado en:
echo  Output\Instalador_ONKOS_File_Transfer_2.0.exe
echo ====================================================
pause
