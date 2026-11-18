# ONKOS File Transfer 2.0 — Arquitectura sin servidor central

## Estado
Diseño para desarrollo y pruebas. No desplegar en producción ni transferir información clínica sensible hasta completar las pruebas de seguridad y recibir aprobación institucional.

## Objetivo
Transferencia directa de archivos entre computadores de la LAN. No requiere servidor central, nube, API externa ni base de datos compartida. Cada computador ejecuta su propio servicio receptor y conserva localmente sus equipos autorizados, configuración e historial.

## Alcance de esta versión
- Sin servidor central ni sincronización centralizada.
- Sin almacenamiento ni retransmisión de archivos en la nube.
- La autorización de equipos se administra localmente en cada computador receptor.
- Sistemas deberá aprobar los equipos en cada receptor donde se permita recibir archivos.
- Los grupos/áreas y la libreta de destinatarios se configuran localmente en la interfaz de cada emisor.
- La auditoría e historial son locales a cada computador; no existe una vista consolidada de toda la clínica.

## Componentes

### 1. ONKOS Transfer Service (cada computador)
- Servicio de Windows independiente de la interfaz gráfica.
- Inicia con Windows y recibe aunque ningún usuario haya abierto ONKOS.
- Ejecuta con una cuenta de privilegios mínimos (preferiblemente LocalService o una cuenta dedicada).
- Escucha únicamente en la red/interfaz LAN configurada y en el puerto definido.
- Guarda en una carpeta configurada con permisos restrictivos.
- No acepta conexiones anónimas ni comandos remotos generales.
- La instalación, actualización, configuración protegida y desinstalación requieren permisos administrativos.

### 2. ONKOS File Transfer (interfaz gráfica)
- Selección de cualquier tipo de archivo y de carpetas completas, conservando su estructura.
- Lista editable antes de enviar: agregar, quitar elementos, seleccionar todo, deseleccionar y limpiar.
- Envío a una o varias IP/equipos autorizados.
- Libreta local de destinatarios y grupos por área, editable por el usuario según permisos.
- Envíos secuenciales en la primera versión para simplificar control y recuperación.
- Progreso por archivo y por destinatario, con estado claro de cada envío.
- Notificaciones visuales para todos los eventos: envíos, recepciones y errores; sin sonido. Mostrar equipo/usuario, archivos y resultado cuando corresponda, sin exponer el contenido.
- Tras completar, muestra un resumen; al cerrarlo, limpia la selección temporal, destinatarios temporales y progreso, dejando el formulario listo para otro envío.
- La limpieza de pantalla no elimina ni modifica los archivos originales ni borra el historial.
- Historial persistente de envíos y recepciones con filtros por fecha, equipo, usuario y resultado.
- Detalle de transferencia con nombres, tamaños, cantidad, fecha/hora, origen, destino, estado y errores.
- Acción para abrir la ubicación recibida, respetando permisos del usuario.

### Notificaciones visuales (preferencia definida)

- La preferencia seleccionada para ONKOS File Transfer 2.0 es **notificar todos los eventos**: envíos, recepciones y errores.
- Las notificaciones serán exclusivamente visuales; **no se reproducirán sonidos**.
- Mostrar avisos de Windows cuando la interfaz esté minimizada o no esté abierta, siempre que la sesión de usuario permita mostrar notificaciones.
- En recepción: identificar equipo y usuario de origen cuando estén disponibles, cantidad y nombres de archivos, resultado y acceso a la carpeta recibida.
- En envío: informar el resultado por destinatario, incluidos los envíos completados y fallidos.
- En errores: indicar el equipo afectado y una explicación breve con la acción sugerida, sin mostrar contenido de archivos ni datos clínicos.
- Incluir avisos de solicitudes de autorización de equipos y cambios relevantes de seguridad.
- Mantener un registro local consultable de notificaciones; las notificaciones no sustituyen el historial persistente de transferencias.
- Permitir configurar las notificaciones visuales sin introducir controles de sonido, ya que el sonido queda desactivado por decisión del usuario.

### 3. ONKOS Transfer Admin (administración local de Sistemas)
- Herramienta o sección protegida para administrar la lista de equipos permitidos en ese computador.
- Aprobar o rechazar solicitudes de incorporación recibidas localmente.
- Bloquear o retirar equipos previamente autorizados.
- Configurar carpeta de recepción, puerto, límites, políticas y grupos locales.
- Consultar y exportar el historial/auditoría local.
- Las acciones administrativas requieren elevación de permisos de Windows.
- No existe administración central: los cambios deben repetirse en cada receptor correspondiente.

## Identidad y autorización de equipos sin servidor
- Cada instalación genera una identidad criptográfica propia y una huella digital única.
- Un equipo nuevo no queda autorizado automáticamente.
- Para incorporarlo, Sistemas verifica el nombre/IP y la huella digital del equipo por un procedimiento local controlado y lo aprueba explícitamente en la lista de permitidos del receptor.
- La aprobación se realiza en cada computador receptor que deba aceptar conexiones de ese equipo.
- Los equipos bloqueados o retirados dejan de ser aceptados por ese receptor.
- No confiar únicamente en la dirección IP o el nombre de equipo: pueden cambiar o suplantarse.
- La distribución/verificación de huellas debe hacerse por un canal administrativo confiable; no aceptar aprobaciones recibidas por la misma conexión no confiable que se intenta autorizar.

## Seguridad y límites conocidos
- Usar un canal cifrado TLS 1.2 o superior y autenticación mutua basada en identidades de dispositivo, con validación explícita de la identidad autorizada por el receptor.
- No continuar con el protocolo TCP actual sin cifrado ni autenticación.
- Proteger claves privadas y configuración de confianza mediante ACL del sistema operativo; nunca incluir claves privadas en el repositorio ni en el instalador.
- Restringir el firewall a perfiles/redes privadas y al puerto requerido. No exponer el servicio a Internet.
- Validar nombres y rutas para impedir path traversal; limitar tamaño/cantidad, evitar sobrescrituras por defecto y usar archivos temporales durante la recepción.
- Verificar integridad con SHA-256 antes de marcar la recepción como completada.
- Registrar localmente equipo y usuario de origen/destino cuando estén disponibles, fecha/hora, nombres, tamaños, hash, resultado y motivo de error. No registrar contenido de archivos ni datos clínicos.
- Definir retención, permisos de consulta y exportación del historial según política institucional.
- Esta arquitectura ofrece menos control que una administración centralizada: no hay revocación global instantánea, políticas centralizadas, auditoría consolidada ni garantía de que todos los receptores tengan listas actualizadas. Un equipo debe retirarse de la lista de cada receptor.
- El cifrado y la autorización reducen riesgos, pero no convierten automáticamente esta solución en un sistema aprobado para información clínica sensible. Requiere revisión y aprobación de la clínica.

## Compatibilidad y tecnología
- Mantener la interfaz WinForms existente y evaluar .NET Framework 4.8 para la primera etapa.
- Separar en proyectos: interfaz gráfica, servicio Windows y bibliotecas compartidas/protocolo.
- Objetivo de compatibilidad: Windows 7 SP1, Windows 10 y Windows 11, sujeto a pruebas reales.
- Verificar TLS 1.2, certificados, actualizaciones y algoritmos criptográficos disponibles en cada sistema. Windows 7 requiere pruebas específicas y podría no cumplir los requisitos sin actualizaciones.
- No prometer compatibilidad segura en un sistema operativo que no pueda negociar la configuración TLS requerida; documentar excepciones o excluirlo.
- No incluir credenciales privadas ni secretos en el código fuente, configuración versionada o instalador.

## Flujo de incorporación de un equipo
1. Sistemas instala el servicio en el equipo nuevo con permisos administrativos.
2. El equipo genera su identidad y muestra su huella digital.
3. Sistemas verifica esa identidad por un canal confiable.
4. En cada computador receptor donde se autorizará, Sistemas agrega y aprueba la identidad del nuevo equipo.
5. Se prueba una conexión autenticada y una transferencia de archivos no sensibles.
6. Si el equipo debe dejar de tener acceso, Sistemas lo bloquea/elimina de la lista de cada receptor.

## Flujo de transferencia
1. El emisor selecciona archivos/carpetas y uno o varios destinatarios guardados localmente.
2. La interfaz comprueba que los destinatarios estén configurados y autorizados según la información disponible.
3. El receptor autentica la identidad del emisor, comprueba su lista local de permitidos, valida espacio y aplica sus límites.
4. El contenido se transfiere directamente por el canal cifrado.
5. El receptor valida rutas, tamaño y SHA-256, guarda el contenido y confirma el resultado.
6. Emisor y receptor registran el resultado en sus historiales locales.
7. La interfaz muestra el resumen; al cerrarlo, limpia los elementos y destinatarios temporales y reinicia el progreso, conservando originales e historial.

## Fases de implementación
1. Revisar el código actual y separar el receptor de la interfaz gráfica.
2. Crear el proyecto de servicio Windows, con inicio automático, recuperación y parada controlada.
3. Crear almacenamiento local seguro para identidad, lista de equipos autorizados, configuración e historial.
4. Implementar aprobación local de equipos y administración protegida por permisos de Windows.
5. Implementar TLS mutuo y validación de identidad; retirar el protocolo TCP sin cifrado.
6. Agregar envío de carpetas completas, lista editable, múltiples destinatarios y grupos locales.
7. Implementar notificaciones visuales de todos los eventos (envíos, recepciones, errores y solicitudes de autorización), sin sonido; además, resumen y limpieza de la pantalla tras cada envío.
8. Implementar historial local persistente, filtros, detalle y exportación.
9. Implementar límites, validación de rutas, integridad, recuperación y auditoría.
10. Probar en Windows 7 SP1, 10 y 11 con archivos no sensibles y revisar resultados antes de cualquier piloto.

## Riesgos y condiciones de salida
- La versión actual usa TCP sin cifrado ni autenticación y el receptor depende de MainForm; cerrar la ventana detiene la recepción. No usarla para información sensible.
- Sin servidor central, la autorización, configuración y auditoría quedan distribuidas por computador. La revocación y actualización de políticas deben hacerse equipo por equipo.
- No desplegar hasta probar autenticación, permisos, firewall, integridad, rutas, sobrescritura, recuperación ante cortes, historial, notificaciones y compatibilidad.
- El piloto debe comenzar con pocos equipos de prueba y archivos no sensibles. La clínica debe aprobar formalmente cualquier uso con información institucional o clínica.

## Avance de implementación — etapa 1

- Se creó el proyecto independiente `ONKOSTransferService`, basado en .NET Framework 4.8.
- El servicio usa la cuenta integrada `LocalService`, está configurado para inicio automático retrasado y admite detención controlada.
- Los eventos de inicio y detención se registran en el registro Application de Windows.
- La recepción de red permanece deshabilitada deliberadamente: este primer esqueleto no abre puertos ni acepta archivos.
- Se actualizó el flujo de compilación para compilar tanto la interfaz actual como el nuevo servicio en la rama `v2-architecture` y publicar un artefacto de prueba.
- Pendiente de validar mediante GitHub Actions y prueba real de instalación/inicio/detención en un equipo Windows de laboratorio.


## Preferencias de notificación confirmadas
- Alcance: todos los eventos (envíos, recepciones y errores).
- Presentación: solo visual, sin sonido.
