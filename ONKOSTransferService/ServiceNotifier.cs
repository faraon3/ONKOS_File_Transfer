using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using ONKOSFileTransfer;

namespace ONKOSTransferService
{
    public static class ServiceNotifier
    {
        private const string PipeName = "ONKOS_Transfer_Events";

        public static void Notify(string level, string title, string message, string extraPath = "")
        {
            try
            {
                string payload = string.Join("|",
                    Sanitize(level),
                    Sanitize(title),
                    Sanitize(message),
                    Sanitize(extraPath));

                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(350);
                    using (var writer = new StreamWriter(client, Encoding.UTF8))
                    {
                        writer.Write(payload);
                        writer.Flush();
                    }
                }
            }
            catch (TimeoutException)
            {
                // No hay instancia de bandeja escuchando en este momento
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("ServiceNotifier", "No se pudo enviar evento al proceso de bandeja.", ex);
            }
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text
                .Replace("|", "/")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }
    }
}
