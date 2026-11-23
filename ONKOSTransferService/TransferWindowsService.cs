using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using ONKOSFileTransfer;
using ONKOSFileTransfer.Network;

namespace ONKOSTransferService
{
    public partial class TransferWindowsService : ServiceBase
    {
        private const int DefaultListenPort = 5050;

        private FileReceiver _receiver;
        private ManualResetEventSlim _stopEvent;

        public TransferWindowsService()
        {
            ServiceName = "ONKOSTransferService";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        private static string GetTargetInboxFolder()
        {
            try
            {
                string configured = AppSettingsStore.GetInboxFolder();
                if (!string.IsNullOrWhiteSpace(configured))
                    return configured;
            }
            catch
            {
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Archivos Recibidos ONKOS");
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                string inboxFolder = GetTargetInboxFolder();
                AppSettingsStore.EnsureInboxFolderReady(inboxFolder);

                _stopEvent = new ManualResetEventSlim(false);

                _receiver = new FileReceiver();
                _receiver.FileReceived += OnFileReceived;
                _receiver.Error += OnReceiverError;
                _receiver.Start(DefaultListenPort, inboxFolder);

                ServiceNotifier.SendInfo(
                    "Servicio iniciado",
                    $"Escuchando en el puerto {DefaultListenPort}."
                );
            }
            catch (Exception ex)
            {
                ServiceNotifier.SendError("Error al iniciar servicio", ex.Message);
                throw;
            }
        }

        private void OnFileReceived(string savedPath)
        {
            string itemName = Path.GetFileName(savedPath);
            if (string.IsNullOrWhiteSpace(itemName))
                itemName = "Elemento recibido";

            ServiceNotifier.SendSuccess(
                "Archivo recibido",
                $"Se guardó \"{itemName}\" en bandeja."
            );
        }

        private void OnReceiverError(string errorMessage)
        {
            ServiceNotifier.SendError("Error en recepción", errorMessage);
        }

        protected override void OnStop()
        {
            try
            {
                _stopEvent?.Set();

                if (_receiver != null)
                {
                    _receiver.FileReceived -= OnFileReceived;
                    _receiver.Error -= OnReceiverError;
                    _receiver.Stop();
                    _receiver = null;
                }

                ServiceNotifier.SendWarning(
                    "Servicio detenido",
                    "ONKOSTransferService se ha detenido."
                );
            }
            catch
            {
                // Evita bloquear la detención del servicio de Windows
            }
            finally
            {
                _stopEvent?.Dispose();
                _stopEvent = null;
            }
        }
    }
}
