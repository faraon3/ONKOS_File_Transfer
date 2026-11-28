namespace ONKOS.UI
{
    public static class UiResources
    {
        public const string AppTitle = "ONKOS File Transfer 2.0";
        public const string Step1Title = "1. Elegir archivos o carpetas";
        public const string Step2Title = "2. Elegir destinatarios en la red";
        public const string Step3Title = "3. Cola y envío seguro (TLS 1.2+)";

        public const string BtnAddFiles = "Agregar archivos (Ctrl+O)";
        public const string BtnAddFolder = "Agregar carpeta";
        public const string BtnRemoveSelected = "Quitar (Supr)";
        public const string BtnAddManualPeer = "Agregar equipo manualmente";
        public const string BtnSelectAllPeers = "Seleccionar todos";
        public const string BtnSelectByGroup = "Marcar área / grupo";

        public const string SendDisabledNoFiles = "Enviar (adjunta al menos 1 archivo o carpeta)";
        public const string SendDisabledNoTargets = "Enviar (marca al menos 1 destinatario)";
        public const string SendReadyPrefix = "Enviar ahora";

        public const string HelpMessage =
            "ONKOS File Transfer 2.0 — Guía rápida y atajos:\r\n\r\n" +
            "• Paso 1: Agrega archivos o carpetas (o arrástralos a la ventana). Atajo: Ctrl+O.\r\n" +
            "• Paso 2: Marca uno o varios destinatarios en la red, selecciona un área/grupo o usa 'Agregar equipo manualmente'.\r\n" +
            "• Paso 3: Presiona Enviar (Ctrl+Enter). Puedes pausar, cancelar o reintentar elementos fallidos sin perder la cola.\r\n" +
            "• Seguridad: Todas las transferencias viajan cifradas con TLS 1.2+ y requieren autorización por huella SHA-256 en el receptor.\r\n" +
            "• Avisos: Todas las notificaciones son estrictamente visuales (sin sonido).";
    }
}
