namespace ONKOS.UI
{
    public static class UiResources
    {
        public const string AppTitle = "ONKOS Transfer";

        public const string Step1Title = "1. Archivos y carpetas";
        public const string Step2Title = "2. Destinatarios";
        public const string Step3Title = "3. Cola de envío";

        public const string BtnAddItem = "+ Agregar";
        public const string BtnRemoveItem = "Quitar";
        public const string BtnAddManualPeer = "+ Destinatario";
        public const string BtnSelectAllPeers = "Todos";
        public const string BtnSelectByGroup = "Área / grupo";

        public const string SendNeedFiles = "Enviar · agrega archivo o carpeta";
        public const string SendNeedTargets = "Enviar · elige destinatario";
        public const string SendReady = "Enviar";

        public const string HelpMessage =
            "ONKOS Transfer — Guía rápida\r\n\r\n" +
            "• Agregar: usa '+ Agregar' para elegir archivos o carpetas, o arrástralos aquí (Ctrl+O).\r\n" +
            "• Destinatarios: marca uno o varios equipos, filtra por área/grupo o usa '+ Destinatario' para una IP.\r\n" +
            "• Enviar: pulsa Enviar (Ctrl+Enter). Puedes pausar, cancelar o reintentar desde la cola.\r\n" +
            "• Seguridad y avisos: canal cifrado TLS 1.2+ con huella SHA-256 y avisos visuales sin sonido.";
    }
}
