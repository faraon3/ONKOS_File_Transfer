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
        private string _lastNotifiedKey = string.Empty;

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

                    string relative = e.FullPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                        ? e.FullPath.Substring(folder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        : name;

                    string topEntry = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                    string targetPath = Path.Combine(folder, topEntry);

                    OnFileReceived(targetPath);
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
                string key = (receivedPath ?? string.Empty).Trim().ToLowerInvariant();
                DateTime now = DateTime.UtcNow;
                if ((now - _lastNotifiedUtc).TotalSeconds < 3 &&
                    string.Equals(_lastNotifiedKey, key, StringComparison.OrdinalIgnoreCase))
                    return;

                _lastNotifiedUtc = now;
                _lastNotifiedKey = key;

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
