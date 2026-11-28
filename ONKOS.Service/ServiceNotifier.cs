using System;
using System.Diagnostics;
using ONKOS.Core.Logging;

namespace ONKOS.Service
{
    internal static class ServiceNotifier
    {
        private const string EventSourceName = "ONKOS Transfer Service";

        public static void WriteEvent(EventLogEntryType type, string category, string message, Exception ex = null)
        {
            string safeMessage = message ?? string.Empty;
            if (ex != null)
                safeMessage = $"{safeMessage} ({ex.GetType().Name}: {ex.Message})";

            switch (type)
            {
                case EventLogEntryType.Error:
                    FileLogger.Instance.Error(category, message, ex);
                    break;
                case EventLogEntryType.Warning:
                    FileLogger.Instance.Warn(category, message);
                    break;
                default:
                    FileLogger.Instance.Info(category, message);
                    break;
            }

            try
            {
                if (EventLog.SourceExists(EventSourceName))
                {
                    EventLog.WriteEntry(EventSourceName, safeMessage, type);
                }
            }
            catch (Exception logEx)
            {
                FileLogger.Instance.Warn(
                    "ServiceNotifier",
                    $"No se pudo escribir en Event Log de Windows ({logEx.GetType().Name}: {logEx.Message}).");
            }
        }
    }
}
