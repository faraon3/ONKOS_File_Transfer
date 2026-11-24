using System;
using System.IO;
using System.ServiceProcess;
using ONKOSFileTransfer;
using ONKOSFileTransfer.Network;

namespace ONKOSTransferService
{
    public partial class TransferWindowsService : ServiceBase
    {
        private const int DefaultTransferPort = 5050;
        private FileReceiver _receiver;

        public TransferWindowsService()
        {
            ServiceName = "ONKOSTransferService";
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                string inbox = AppSettingsStore.GetInboxFolder();
                AppSettingsStore.EnsureInboxFolderReady(inbox);

                _receiver = new FileReceiver();
                _receiver.FileReceived += OnFileReceived;
                _receiver.Error += OnReceiverError;
                _receiver.Start(DefaultTransferPort, inbox);

                OnkosLogger.Info("TransferWindowsService", $"Servicio iniciado en puerto {DefaultTransferPort}, carpeta: {inbox}");
                ServiceNotifier.Notify("INFO", "ONKOS File Transfer", $"Servicio de recepción activo en puerto {DefaultTransferPort}.");
            }
            catch (Exception ex)
            {
                OnkosLogger.Error("TransferWindowsService", "Error al iniciar el servicio.", ex);
                throw;
            }
        }

        protected override void OnStop()
        {
            try
            {
                if (_receiver != null)
                {
                    _receiver.FileReceived -= OnFileReceived;
                    _receiver.Error -= OnReceiverError;
                    _receiver.Stop();
                    _receiver = null;
                }

                OnkosLogger.Info("TransferWindowsService", "Servicio detenido limpiamente.");
                ServiceNotifier.Notify("INFO", "ONKOS File Transfer", "Servicio de recepción detenido.");
            }
            catch (Exception ex)
            {
                OnkosLogger.Error("TransferWindowsService", "Error al detener el servicio.", ex);
            }
        }

        private void OnFileReceived(string savedPath)
        {
            try
            {
                string name = Path.GetFileName(savedPath);
                long size = 0L;
                if (File.Exists(savedPath))
                    size = new FileInfo(savedPath).Length;
                else if (Directory.Exists(savedPath))
                {
                    foreach (string f in Directory.EnumerateFiles(savedPath, "*", SearchOption.AllDirectories))
                    {
                        try { size += new FileInfo(f).Length; }
                        catch (Exception fEx) { OnkosLogger.Warn("TransferWindowsService", $"Error leyendo '{f}'.", fEx); }
                    }
                }

                OnkosLogger.Info("TransferWindowsService", $"Archivo/carpeta recibido: '{name}' ({size} bytes) en '{savedPath}'");
                ServiceNotifier.Notify("SUCCESS", "Archivo recibido", $"Se recibió '{name}'. Haz clic para abrir.", savedPath);
            }
            catch (Exception ex)
            {
                OnkosLogger.Error("TransferWindowsService", $"Error procesando recepción de '{savedPath}'.", ex);
            }
        }

        private void OnReceiverError(string errorMessage)
        {
            OnkosLogger.Error("TransferWindowsService", $"Error en receptor: {errorMessage}");
            ServiceNotifier.Notify("ERROR", "Error en recepción", errorMessage);
        }
    }
}
