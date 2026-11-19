using System;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace ONKOSTransferService
{
    internal static class ServiceNotifier
    {
        private const string PipeName = "ONKOS_Transfer_Events";

        public static void SendInfo(string title, string message)
        {
            Send("INFO", title, message);
        }

        public static void SendSuccess(string title, string message)
        {
            Send("SUCCESS", title, message);
        }

        public static void SendWarning(string title, string message)
        {
            Send("WARNING", title, message);
        }

        public static void SendError(string title, string message)
        {
            Send("ERROR", title, message);
        }

        private static void Send(string level, string title, string message)
        {
            try
            {
                string safeTitle = Sanitize(title);
                string safeMessage = Sanitize(message);
                string payload = $"{level}|{safeTitle}|{safeMessage}";

                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(300);
                    using (var writer = new StreamWriter(client, Encoding.UTF8))
                    {
                        writer.Write(payload);
                        writer.Flush();
                    }
                }
            }
            catch
            {
                // Si no hay sesión de usuario activa o el ícono de bandeja está cerrado, el servicio continúa sin fallar
            }
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
