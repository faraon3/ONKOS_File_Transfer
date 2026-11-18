using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

namespace ONKOS.TransferService
{
    public sealed class TransferWindowsService : ServiceBase
    {
        private const string EventSourceName = "ONKOS Transfer Service";
        private const string EventLogName = "Application";
        private EventLog serviceLog;

        public TransferWindowsService()
        {
            ServiceName = "ONKOSTransferService";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = false;
        }

        protected override void OnStart(string[] args)
        {
            serviceLog = new EventLog
            {
                Source = EventSourceName,
                Log = EventLogName
            };

            WriteEvent("ONKOS Transfer Service iniciado. La recepción de archivos permanece deshabilitada hasta implementar autenticación y cifrado.", EventLogEntryType.Information);
        }

        protected override void OnStop()
        {
            WriteEvent("ONKOS Transfer Service detenido.", EventLogEntryType.Information);

            if (serviceLog != null)
            {
                serviceLog.Dispose();
                serviceLog = null;
            }
        }

        protected override void OnShutdown()
        {
            WriteEvent("Windows está cerrando; ONKOS Transfer Service se detendrá.", EventLogEntryType.Information);
            base.OnShutdown();
        }

        private void WriteEvent(string message, EventLogEntryType type)
        {
            try
            {
                if (serviceLog != null)
                    serviceLog.WriteEntry(message, type);
            }
            catch
            {
                // A logging failure must not crash the Windows service.
            }
        }
    }
}