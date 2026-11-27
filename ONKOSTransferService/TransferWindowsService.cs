using System;
using System.IO;
using System.ServiceProcess;
using System.Text;
using ONKOSFileTransfer;
using ONKOSFileTransfer.Network;

namespace ONKOSTransferService
{
    public class TransferWindowsService : ServiceBase
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

        private static string GetInboxFolderSafe()
        {
            try
            {
                string settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ONKOS",
                    "ruta_recibidos.txt");

                if (File.Exists(settingsPath))
                {
                    string saved = File.ReadAllText(settingsPath, Encoding.UTF8).Trim();
                    if (!string.IsNullOrWhiteSpace(saved))
                        return saved;
                }
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("TransferWindowsService", "No se pudo leer ruta_recibidos.txt; usando ruta de respaldo.", ex);
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ONKOS",
                "Recibidos");
        }

        private static long ComputePathSizeSafe(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return 0L;

            try
            {
                if (File.Exists(path))
                    return new FileInfo(path).Length;

                if (Directory.Exists(path))
                {
                    long total = 0L;
                    foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try { total += new FileInfo(file).Length; } catch { }
                    }
                    return total;
                }
            }
            catch
            {
            }

            return 0L;
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                _activeInboxFolder = GetInboxFolderSafe();
                Directory.CreateDirectory(_activeInboxFolder);

                _receiver = new FileReceiver();
                _receiver.FileReceived += OnFileReceived;
                _receiver.BatchReceived += OnBatchReceived;
                _receiver.Error += OnReceiverError;
                _receiver.Start(TransferPort, _activeInboxFolder);

                OnkosLogger.Info("TransferWindowsService", $"Servicio iniciado correctamente en puerto {TransferPort}. Carpeta de recibidos: {_activeInboxFolder}");
            }
            catch (Exception ex)
            {
                OnkosLogger.Error("TransferWindowsService", "Error crítico al iniciar el servicio de recepción.", ex);
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
                OnkosLogger.Error("TransferWindowsService", "Error al detener el servicio de recepción.", ex);
            }
        }

        private void OnFileReceived(string destinationPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(destinationPath))
                    return;

                string itemName = Path.GetFileName(destinationPath);
                if (string.IsNullOrWhiteSpace(itemName))
                    itemName = destinationPath;

                long sizeBytes = ComputePathSizeSafe(destinationPath);
                string counterparty = "Equipo en red";
                try
                {
                    string parentFolder = Path.GetFileName(Path.GetDirectoryName(destinationPath) ?? string.Empty);
                    if (!string.IsNullOrWhiteSpace(parentFolder))
                        counterparty = parentFolder;
                }
                catch
                {
                }

                TransferHistoryStore.AppendEntry(
                    "RECIBIDO",
                    itemName,
                    sizeBytes,
                    counterparty,
                    "Completado",
                    destinationPath);
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("TransferWindowsService", "No se pudo registrar recepción en historial.", ex);
            }
        }

        private void OnBatchReceived(ReceivedBatchSummary batch)
        {
            try
            {
                if (batch == null)
                    return;

                int cantidad = batch.FilePaths != null ? batch.FilePaths.Count : 0;
                string remitente = !string.IsNullOrWhiteSpace(batch.SenderName) ? batch.SenderName : "Equipo en red";
                string titulo = cantidad == 1
                    ? "Archivo recibido"
                    : $"{cantidad} archivos recibidos";
                string mensaje = $"De: {remitente}";

                string rutaAbrir = (cantidad == 1 && batch.FilePaths != null && batch.FilePaths.Count > 0)
                    ? batch.FilePaths[0]
                    : (!string.IsNullOrWhiteSpace(batch.DestinationFolder)
                        ? batch.DestinationFolder
                        : GetInboxFolderSafe());

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
