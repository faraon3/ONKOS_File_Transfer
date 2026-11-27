# ONKOS Transfer Service

Servicio de Windows independiente para ONKOS File Transfer 2.0.

## Estado de esta etapa

- Proyecto independiente basado en .NET Framework 4.8.
- Cuenta de servicio: LocalService.
- Inicio automático (inicio retrasado).
- Inicio, detención y cierre de Windows controlados.
- Eventos de ciclo de vida en el registro Application de Windows.
- **La recepción de archivos por red está deshabilitada intencionalmente.** No se debe abrir el puerto TCP ni usar el protocolo antiguo hasta implementar autenticación mutua, TLS y autorización local.

## Compilación

Compilar `ONKOSTransferService.csproj` en configuración Release con MSBuild y .NET Framework 4.8 Developer Pack.

## Instalación de prueba (requiere administrador)

Desde el directorio que contiene el ejecutable y su DLL:

```bat
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe ONKOS.TransferService.exe
```

En Windows de 32 bits, usar `%WINDIR%\Microsoft.NET\Framework\v4.0.30319\InstallUtil.exe`.

Iniciar y consultar:

```bat
sc start ONKOSTransferService
sc query ONKOSTransferService
```

Desinstalar:

```bat
InstallUtil.exe /u ONKOS.TransferService.exe
```

No instalar en equipos de producción todavía. Validar primero en una máquina de pruebas con archivos no sensibles.
