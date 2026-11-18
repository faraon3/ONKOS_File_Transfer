# ONKOS File Transfer 2.0 — Arquitectura propuesta

## Estado
Diseño inicial. No desplegar en producción ni transferir información clínica hasta completar desarrollo, pruebas y validación de seguridad.

## Objetivo
Transferencia de archivos punto a punto dentro de la LAN de la clínica. Los archivos viajan directamente entre equipos; el servidor central administra autorización, configuración y auditoría, pero no almacena ni retransmite el contenido.

## Componentes

### 1. ONKOS Transfer Service (cada computador)
- Servicio de Windows independiente de la interfaz gráfica.
- Inicia con Windows y recibe aunque ningún usuario haya abierto ONKOS.
- Ejecuta con una cuenta de privilegios mínimos (preferiblemente LocalService o una cuenta de servicio dedicada).
- Escucha únicamente en interfaces/redes internas autorizadas.
- Guarda en una carpeta configurada y con ACL restrictivas.
- No acepta conexiones anónimas ni comandos remotos generales.

### 2. ONKOS File Transfer (interfaz gráfica)
- Selección de archivos y carpetas, lista editable antes de enviar.
- Uno o varios destinatarios, selección de equipos aprobados y grupos.
- Envíos secuenciales inicialmente; concurrencia configurable después de medir capacidad.
- Muestra progreso, confirmación, errores y notificaciones.
- Historial visible según permisos.

### 3. ONKOS Transfer Admin (Sistemas)
- Registro de solicitudes de incorporación de equipos.
- Aprobación o rechazo manual por Sistemas.
- Revocación/bloqueo de equipos.
- Gestión de grupos/áreas, límites y políticas.
- Consulta de auditoría.

### 4. Servicio central de control (red interna)
- API de control y registro alojada en infraestructura interna aprobada por la clínica.
- Base de datos para dispositivos, aprobaciones, grupos, políticas y auditoría.
- No transporta el contenido de los archivos.
- Si no está disponible, las transferencias nuevas quedan bloqueadas; no se debe aceptar un equipo desconocido por modo de emergencia.

## Confianza y seguridad

- Cada instalación recibe una identidad criptográfica única de dispositivo.
- Un equipo nuevo genera una solicitud de inscripción; no puede recibir ni enviar hasta que Sistemas lo apruebe.
- Tras aprobar, se emite una credencial/certificado de dispositivo firmado por una autoridad interna.
- Comunicación de transferencia cifrada con TLS 1.2 o superior y autenticación mutua de dispositivos.
- Validación de autorización y revocación antes de aceptar transferencias.
- Claves privadas protegidas mediante ACL del sistema operativo; nunca se guardan en el repositorio ni dentro del instalador.
- Firewall limitado a perfiles/redes privadas de la clínica y al puerto definido.
- Límites de tamaño, nombres seguros, prevención de sobrescritura, archivos temporales y verificación SHA-256.
- Auditoría: dispositivo emisor/receptor, usuario disponible, fecha/hora, nombres, tamaños, hash, resultado y motivo de fallo. Evitar registrar contenido de archivos o datos clínicos.
- Políticas configurables para tipos de archivo, destinos, horarios y tamaños. La opción inicial es permitir cualquier extensión solo entre dispositivos autorizados, con posibilidad de bloquear tipos de riesgo.

## Compatibilidad y tecnología

- Mantener la interfaz WinForms existente y el objetivo .NET Framework 4.8 en la primera etapa para preservar compatibilidad con Windows 7 SP1, Windows 10 y Windows 11.
- Crear proyectos separados para interfaz, servicio Windows y bibliotecas compartidas.
- Verificar disponibilidad real de TLS 1.2, actualizaciones de Windows y certificados en cada versión de sistema operativo; Windows 7 requiere pruebas específicas y puede necesitar actualizaciones.
- No asumir que el servicio puede instalarse sin privilegios administrativos: la instalación y actualización requieren autorización de Sistemas.
- Revisar compatibilidad y soporte de la infraestructura central antes de decidir el alojamiento definitivo.

## Flujo de incorporación

1. Sistemas instala el servicio en el computador nuevo.
2. El servicio genera una solicitud de inscripción con identificador único y datos técnicos mínimos.
3. Sistemas verifica físicamente/administrativamente el equipo y aprueba o rechaza.
4. La autoridad interna emite la credencial del dispositivo aprobado.
5. El servicio instala la credencial y queda habilitado según la política asignada.
6. Sistemas puede suspender o revocar el dispositivo.

## Flujo de transferencia

1. El emisor selecciona archivos/carpetas y destinatarios aprobados.
2. La interfaz consulta autorización y políticas.
3. El receptor verifica identidad del emisor, autorización, espacio disponible y reglas.
4. Se transfiere el contenido por canal cifrado directo.
5. El receptor verifica tamaño y SHA-256, guarda el contenido y confirma.
6. Ambos equipos registran el resultado; la interfaz notifica al usuario.

## Fases de implementación

1. Separar el receptor del formulario y crear el proyecto de servicio Windows, sin cambiar aún el protocolo público.
2. Crear instalador/paquete de servicio y probar inicio, parada, recuperación y desinstalación.
3. Diseñar e implementar inscripción y aprobación de dispositivos.
4. Implementar TLS mutuo y revocación; eliminar el protocolo TCP actual sin cifrado.
5. Agregar carpetas, múltiples IP, selección editable y notificaciones.
6. Agregar auditoría central, políticas y pruebas de seguridad.
7. Piloto controlado en equipos de prueba; luego despliegue gradual.

## Riesgos y condiciones de salida

- La versión actual usa TCP sin cifrado ni autenticación y el receptor vive dentro de MainForm; cerrar la ventana detiene la recepción. No usarla para información sensible.
- No desplegar la versión 2.0 hasta probar autenticación, revocación, permisos de carpetas, firewall, integridad, recuperación ante cortes, compatibilidad y auditoría.
- La autoridad de seguridad, la ubicación del servicio central y las políticas finales deben ser aprobadas por Sistemas/seguridad de la clínica.
