# Arquitectura — ONKOS File Transfer 2.0

## Visión general

ONKOS File Transfer 2.0 separa la recepción continua y segura en un **Servicio de Windows** (`ONKOS.Service`) de la experiencia interactiva de envío, cola, notificaciones y diagnóstico en **WinForms** (`ONKOS.UI`), compartiendo toda la lógica de dominio y persistencia en `ONKOS.Core`.

## Capas y proyectos

1. **`ONKOS.Core`**
   * `Protocol/SecureTransferProtocol.cs`: Definición de límites (`TransferLimits`), validación de rutas relativas (bloqueando `..`, rutas absolutas, caracteres inválidos y nombres reservados), cálculo de hash SHA-256 y negociación/autenticación TLS 1.2+.
   * `Security/`: Gestión de identidad del equipo (`DeviceIdentityStore`) y estado de autorización por huella SHA-256 (`PeerAuthorizationStore`).
   * `Discovery/`: Anuncios UDP en puerto `5051` calculando broadcast por máscara de subred (`SubnetInfo.BroadcastAddress`), filtrando adaptadores virtuales/VPN/túnel/APIPA (`169.254.x.x`), deduplicando contactos por huella y verificando presencia TCP en puerto `5050`.
   * `Transfers/`: `TransferQueueManager` (cola asíncrona con pausa/reanudación, cancelación, reintento con *backoff* exponencial y limpieza de temporales en `finally`) y `PathSizeCache` (caché de tamaño con invalidación y cancelación).
   * `Notifications/`: `NotificationCenterStore` (agrupación de eventos en ventana temporal, preferencias por categoría y duración sin sonido).
   * `Storage/` y `Logging/`: Escritura atómica bajo `Mutex` (`AtomicFileStore`), validación de rutas/espacio/ACL (`PathAndAclHelper`), perfil/áreas (`UserProfileStore`), libreta/grupos (`AddressBookStore`), políticas del receptor (`ReceiverPolicyStore`), historial (`TransferHistoryStore`) y registro con rotación (`FileLogger`).

2. **`ONKOS.Service`**
   * Ejecuta `TransferWindowsService` (`ONKOSTransferService`) en TCP `5050` con control de concurrencia global y por IP de origen.
   * Rechaza conexiones no autorizadas o bloqueadas antes de escribir archivos finales y registra auditoría en historial, centro de notificaciones y visor de eventos de Windows.

3. **`ONKOS.UI`**
   * `Program.cs`: Control de instancia única por sesión de Windows, captura global de excepciones y lanzamiento del asistente inicial (`FirstRunWizardForm`) si falta perfil.
   * `MainForm.cs`: Flujo de 3 pasos en `TableLayoutPanel`/`FlowLayoutPanel`, atajos de teclado (`Ctrl+O`, `Supr`, `Ctrl+Enter`, `F1`), temas (`Dark`, `Light`, `HighContrast`), bandeja del sistema (`NotifyIcon`) y sincronización periódica del contador de recibidos no vistos y notificaciones.
   * `SettingsForm.cs`, `ManualPeerDialog.cs`, `TransferHistoryForm.cs`, `NotificationCenterForm.cs` y `VisualToastForm.cs`.

4. **`ONKOS.Tests`**
   * Ejecutable de pruebas automatizadas que valida reglas críticas de rutas, SHA-256, cálculo de subred, caché, cola, agrupación de notificaciones y presencia del script de instalador.
