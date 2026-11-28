# Operación y Pruebas — ONKOS File Transfer 2.0

## Instalación y actualización en Windows

1. Ejecuta el instalador `Instalador_ONKOS_File_Transfer_2.0.exe` como administrador.
2. En actualizaciones sobre versiones previas, el instalador detiene la interfaz y el servicio si están activos, actualiza los binarios de `ONKOS.UI` y `ONKOS.Service`, mantiene el servicio en `NT AUTHORITY\LocalService` y reconfigura las reglas de firewall para `LocalSubnet`.
3. En la primera ejecución de `ONKOSFileTransfer.exe`, el asistente (`FirstRunWizardForm`) solicita nombre completo, área y carpeta de recibidos, y muestra una comprobación rápida del servicio y la red LAN.

## Diagnóstico y resolución de fallos frecuentes

* **Informe de diagnóstico:** Abre **Configuración** -> **Actualizar diagnóstico** -> **Copiar informe** para obtener estado de `ONKOSTransferService`, puerto TCP `5050`, adaptadores LAN activos/máscaras/broadcast, huella SHA-256 local, espacio libre en la carpeta de recibidos y ruta de logs (`ProgramData\ONKOS\logs`).
* **Equipo remitente rechazado por autorización:** Verifica en el equipo receptor que la huella SHA-256 del remitente esté aprobada como autorizada y que la política de aceptación automática esté habilitada.
* **No aparecen equipos en otra subred o adaptador virtual:** `LanDiscoveryService` ignora adaptadores virtuales/VPN/túnel y direcciones APIPA (`169.254.x.x`), y calcula el broadcast por máscara de subred. Si un equipo no responde al broadcast, usa **Agregar equipo manualmente** con su IPv4.
* **Error en envío o recepción:** Revisa el mensaje de acción sugerida en la barra de estado / botón **Ver detalles**, consulta el historial (`TransferHistoryForm`) y revisa los archivos de registro en `ProgramData\ONKOS\logs`.

## Checklist de pruebas en laboratorio Windows (no ejecutadas aquí)

* [ ] Compilación limpia en `Release|AnyCPU` de `ONKOS.Core`, `ONKOS.Service`, `ONKOS.UI` y `ONKOS.Tests` y ejecución de `ONKOS.Tests.exe` (`PHASE8A_TESTS_OK`).
* [ ] Instalación limpia, actualización sobre versión anterior y desinstalación con `ONKOS_Setup.iss` (verificando servicio `ONKOSTransferService` y reglas de firewall).
* [ ] Envío de archivos y carpetas entre dos equipos en red LAN (`/24` y `/23`), validando hash SHA-256, pausa/reanudación, cancelación, reintento de fallidos y limpieza de temporales.
* [ ] Recepción con servicio en segundo plano, actualización del contador de **Recibidos**, notificaciones visuales sin sonido en el monitor activo y exportación CSV del historial.
* [ ] Pruebas de interfaz con escalas DPI de 100%, 125% y 150%, temas (`Dark`, `Light`, `HighContrast`) y atajos `Ctrl+O`, `Supr`, `Ctrl+Enter` y `F1`.
