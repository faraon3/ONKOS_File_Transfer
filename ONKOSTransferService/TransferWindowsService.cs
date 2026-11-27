using System;
using System.IO;
using System.ServiceProcess;
using System.Text;
using ONKOSFileTransfer;
using ONKOSFileTransfer.Network;

@@ -21,12 +22,38 @@
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

        protected override void OnStart(string[] args)
        {
            try
            {
                AppSettingsStore.EnsureInboxFolderReady();
                _activeInboxFolder = AppSettingsStore.GetInboxFolder();
                _activeInboxFolder = GetInboxFolderSafe();
                Directory.CreateDirectory(_activeInboxFolder);

                _receiver = new FileReceiver();
@@ -138,20 +165,20 @@
                    ? batch.FilePaths[0]
                    : (!string.IsNullOrWhiteSpace(batch.DestinationFolder)
                        ? batch.DestinationFolder
                        : AppSettingsStore.GetInboxFolder());
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
