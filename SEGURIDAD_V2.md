# Seguridad — ONKOS File Transfer 2.0

## Controles de seguridad implementados

1. **Canal cifrado con TLS 1.2+ y autenticación mutua:**
   * Las transferencias por TCP (`5050`) negocian TLS 1.2+ mediante `SslStream`, exigiendo identidad del equipo y verificación de huella SHA-256 tanto en cliente como en servidor.
2. **Autorización por huella SHA-256 y políticas del receptor:**
   * `PeerAuthorizationStore` administra el estado de confianza de los equipos (`Authorized`, pendiente, bloqueado/revocado) por huella SHA-256.
   * `ReceiverPolicyStore` permite exigir aceptación automática de equipos autorizados o bloquear remitentes específicos por huella o IP antes de recibir datos.
3. **Validación estricta de rutas e integridad:**
   * `SecureTransferProtocol` normaliza y valida rutas relativas bloqueando secuencias `..`, rutas absolutas, caracteres inválidos y nombres reservados de Windows, evitando *path traversal* al guardar archivos o extraer paquetes `.onkosfolder`.
   * Cada archivo se escribe primero en un temporal `.part`, se calcula su hash **SHA-256** durante la transmisión y solo se mueve al destino final si el hash coincide y no existe conflicto de sobrescritura por defecto.
4. **Control de concurrencia y escritura atómica:**
   * El servicio aplica límites de concurrencia global y por IP de origen para mitigar saturación de conexiones.
   * Los archivos de configuración, contactos, historial y notificaciones usan escrituras atómicas coordinadas con `Mutex` y permisos de carpeta gestionados en `ProgramData\ONKOS`.
5. **Servicio de Windows y reglas de firewall:**
   * `ONKOS_Setup.iss` instala `ONKOSTransferService` bajo `NT AUTHORITY\LocalService` (privilegios mínimos) con reinicio automático ante fallos y configura reglas de firewall de Windows acotadas a `remoteip=LocalSubnet` en perfiles `private,domain` para TCP `5050` y UDP `5051`.
