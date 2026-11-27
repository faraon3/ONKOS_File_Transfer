using System;
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
                string safeTitle = SanitizeTitleOrLevel(title);
                string safeMessage = SanitizeMessage(message);
                string payload = $"{level}|{safeTitle}|{safeMessage}";
                byte[] bytes = Encoding.UTF8.GetBytes(payload);

                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.None))
                {
                    client.Connect(1200);
                    client.Write(bytes, 0, bytes.Length);
                    client.Flush();
                    client.WaitForPipeDrain();
                }
            }
            catch
            {
            }
        }

        private static string SanitizeTitleOrLevel(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string SanitizeMessage(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
