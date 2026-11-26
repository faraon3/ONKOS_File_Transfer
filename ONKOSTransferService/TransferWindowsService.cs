using System;
using System.IO;
using System.ServiceProcess;
using ONKOSFileTransfer;
using ONKOSFileTransfer.Network;

namespace ONKOSTransferService
{
    public partial class TransferWindowsService : ServiceBase
    {
        private const int TransferPort = 5050;

        private FileReceiver _receiver;
        private string _activeInboxFolder;

        public TransferWindowsService()
        {
            ServiceName = "ONKOSTransferService";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                AppSettingsStore.EnsureInboxFolderReady();
                _activeInboxFolder = AppSettingsStore.GetInboxFolder();
                Directory.CreateDirectory(_activeInboxFolder);

                _receiver = new FileReceiver();
                _receiver.FileReceived += OnFileReceived;
                _receiver.BatchReceived += OnBatchReceived;
                _receiver.Error += OnReceiverError;
                _receiver.Start(TransferPort, _activeInboxFolder);

                OnkosLogger.Info(
                    "TransferWindowsService",
                    $"Servicio iniciado en puerto {TransferPort}, carpeta: {_activeInboxFolder}");
            }
            catch (Exception ex)
            {
                OnkosLogger.Error("TransferWindowsService", "Error al iniciar el servicio de transferencia.", ex);
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
                    _receiver.BatchReceived -= OnBatchReceived;
                    _receiver.Error -= OnReceiverError;
                    _receiver.Stop();
                    _receiver = null;
                }

                OnkosLogger.Info("TransferWindowsService", "Servicio detenido correctamente.");
            }
            catch (Exception ex)
            {
                OnkosLogger.Error("TransferWindowsService", "Error al detener el servicio de transferencia.", ex);
            }
        }

        private void OnFileReceived(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return;

                bool isDirectory = Directory.Exists(filePath);
                string itemName = Path.GetFileName(
                    filePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                long sizeBytes = 0;
                if (isDirectory)
                {
                    foreach (string f in Directory.EnumerateFiles(filePath, "*", SearchOption.AllDirectories))
                    {
                        try { sizeBytes += new FileInfo(f).Length; }
                        catch { }
                    }
                    OnkosLogger.Info(
                        "TransferWindowsService",
                        $"Carpeta recibida: '{itemName}' ({sizeBytes} bytes) en '{filePath}'");
                }
                else if (File.Exists(filePath))
                {
                    sizeBytes = new FileInfo(filePath).Length;
                    OnkosLogger.Info(
                        "TransferWindowsService",
                        $"Archivo recibido: '{itemName}' ({sizeBytes} bytes) en '{filePath}'");
                }

                string parentDirName = Path.GetFileName(Path.GetDirectoryName(filePath) ?? string.Empty);
                string senderIp = parentDirName.Contains("_")
                    ? parentDirName.Split('_')[0]
                    : parentDirName;

                TransferHistoryStore.AppendEntry(
                    "RECIBIDO",
                    itemName,
                    sizeBytes,
                    senderIp,
                    "Completado",
                    senderIp);

                TransferHistoryStore.IncrementUnseenReceived();
            }
            catch (Exception ex)
            {
                OnkosLogger.Error("TransferWindowsService", $"Error procesando elemento recibido '{filePath}'.", ex);
            }
        }

        private void OnBatchReceived(ReceivedBatchSummary batch)
        {
            try
            {
                if (batch == null || batch.FilePaths == null || batch.FilePaths.Count == 0)
                    return;

                int total = batch.FilePaths.Count;
                string quien = !string.IsNullOrWhiteSpace(batch.SenderName)
                    ? batch.SenderName
                    : batch.SenderIp;

                string titulo = $"Recibido de {quien}";
                string mensaje = total == 1
                    ? "1 archivo enviado"
                    : $"{total} archivos enviados";

                string rutaAbrir = total == 1
                    ? batch.FilePaths[0]
                    : (!string.IsNullOrWhiteSpace(batch.DestinationFolder)
                        ? batch.DestinationFolder
                        : AppSettingsStore.GetInboxFolder());

                ServiceNotifier.Notify("SUCCESS", titulo, mensaje, rutaAbrir);
            }
            catch (Exception ex)
            {
                OnkosLogger.Error("TransferWindowsService", "Error enviando notificación consolidada del lote.", ex);
            }
        }

        private void OnReceiverError(string errorMessage)
        {
            OnkosLogger.Warn("TransferWindowsService", $"Error en receptor TCP: {errorMessage}");
            ServiceNotifier.Notify("ERROR", "Error en recepción", errorMessage);
        }
    }
}
