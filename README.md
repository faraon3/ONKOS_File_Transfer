# ONKOS File Transfer 2.0

Aplicación de transferencia segura de archivos y carpetas en red local (LAN) para entornos clínicos, construida sobre **.NET Framework 4.8** con arquitectura modular en **C# 7.3**.

## Componentes principales

* **`ONKOS.Core`**: Lógica de red/descubrimiento UDP, protocolo cifrado TLS 1.2+ con validación estricta de rutas e integridad SHA-256, gestión de identidad y autorización por huella, cola de transferencias con caché de tamaños, notificaciones agrupadas sin sonido y almacenamiento atómico con ACL.
* **`ONKOS.Service`** (`ONKOSTransferService`): Servicio de Windows en segundo plano que escucha en el puerto **TCP 5050** bajo `NT AUTHORITY\LocalService`, autentica clientes mediante TLS 1.2+ mutuo, verifica políticas y huellas SHA-256, escribe archivos mediante `.part` y mueve de forma atómica tras validar SHA-256, y extrae paquetes `.onkosfolder` con control antidesbordamiento.
* **`ONKOS.UI`** (`ONKOSFileTransfer.exe`): Interfaz de escritorio WinForms con soporte DPI (`PerMonitorV2`), flujo guiado en 3 pasos, selección por área/grupo o equipo manual, cola con progreso/velocidad/ETA/pausa/cancelación/reintento, historial filtrable con exportación CSV, centro de notificaciones visuales sin sonido, bandeja del sistema y diagnóstico copiable.
* **`ONKOS.Tests`** (`ONKOS.Tests.exe`): Suite de pruebas de lógica y reglas críticas para ejecución automatizada en CI y laboratorio.

## Compilación rápida

* Compilar en `Release|AnyCPU` con `msbuild` (ver `.github/workflows/build.yml` para el flujo completo de compilación, pruebas y empaquetado con Inno Setup).

## Documentación adicional

* `ARQUITECTURA_V2.md`: Estructura de proyectos, flujos de datos y persistencia.
* `SEGURIDAD_V2.md` (en Bloque 8B): Modelo de seguridad TLS 1.2+, autorización por huella SHA-256, reglas de red/firewall y ACL.
* `OPERACION_Y_PRUEBAS_V2.md` (en Bloque 8B): Instalación, diagnóstico, resolución de incidencias y pruebas en Windows.
