using System;
using System.Collections.Generic;
using System.IO;
using ONKOSFileTransfer.Network;

namespace ONKOSFileTransfer
{
    public class InboxMonitor : IDisposable
    {
        private const int TransferPort = 5050;
        private readonly FileReceiver _receiver;
        private FileSystemWatcher _serviceWatcher;
        private DateTime _lastNotifiedUtc = DateTime.MinValue;
        private string _lastNotifiedName = string.Empty;

        public static string CarpetaRecibidos => AppSettingsStore.GetInboxFolder();

        public InboxMonitor()
        {
            AppSettingsStore.EnsureInboxFolderReady();

            _receiver = new FileReceiver();
            _receiver.FileReceived += OnFileReceived;

            try
            {
                _receiver.Start(TransferPort, CarpetaRecibidos);
            }
            catch
            {
                // Si el servicio de Windows ya escucha en 5050, vigilamos la carpeta de recibidos
                StartFolderWatcher();
            }
        }

        private void StartFolderWatcher()
        {
            try
            {
                string folder = CarpetaRecibidos;
                Directory.CreateDirectory(folder);
                _serviceWatcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true
                };
                _serviceWatcher.Created += (s, e) =>
                {
                    string name = Path.GetFileName(e.FullPath);
                    if (string.IsNullOrWhiteSpace(name) ||
                        name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("recibidos.ico", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".onkosfolder", StringComparison.OrdinalIgnoreCase))
                        return;

                    string parent = Path.GetDirectoryName(e.FullPath);
                    if (string.Equals(parent, folder, StringComparison.OrdinalIgnoreCase))
                        return;

                    OnFileReceived(e.FullPath);
                };
            }
            catch
            {
            }
        }

        private void OnFileReceived(string receivedPath)
        {
            try
            {
                string itemName = Path.GetFileName(receivedPath);
                DateTime now = DateTime.UtcNow;
                if ((now - _lastNotifiedUtc).TotalSeconds < 2 &&
                    string.Equals(_lastNotifiedName, itemName, StringComparison.OrdinalIgnoreCase))
                    return;

                _lastNotifiedUtc = now;
                _lastNotifiedName = itemName;

                ModernToastNotifier.MostrarLoteRecibido(
                    null,
                    null,
                    null,
                    new List<string> { receivedPath },
                    CarpetaRecibidos);
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            try { _receiver?.Stop(); } catch { }
            try { _serviceWatcher?.Dispose(); } catch { }
        }
    }
}
