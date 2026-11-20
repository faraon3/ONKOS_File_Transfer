using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ONKOSFileTransfer
{
    public sealed class InboxMonitor : IDisposable
    {
        public static readonly string CarpetaRecibidos =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Archivos Recibidos ONKOS");

        private readonly object _lock = new object();
        private readonly List<string> _pendientes = new List<string>();
        private FileSystemWatcher _watcher;
        private Timer _debounceTimer;
        private bool _disposed;

        public InboxMonitor()
        {
            Directory.CreateDirectory(CarpetaRecibidos);

            _debounceTimer = new Timer(ProcesarLote, null, Timeout.Infinite, Timeout.Infinite);

            _watcher = new FileSystemWatcher(CarpetaRecibidos)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnArchivoDetectado;
            _watcher.Changed += OnArchivoDetectado;
        }

        private void OnArchivoDetectado(object sender, FileSystemEventArgs e)
        {
            if (_disposed || string.IsNullOrWhiteSpace(e.FullPath))
                return;

            string nombre = Path.GetFileName(e.FullPath);
            if (string.Equals(nombre, "desktop.ini", StringComparison.OrdinalIgnoreCase))
                return;

            if (Directory.Exists(e.FullPath))
                return;

            lock (_lock)
            {
                if (_disposed)
                    return;

                if (!_pendientes.Contains(e.FullPath, StringComparer.OrdinalIgnoreCase))
                {
                    _pendientes.Add(e.FullPath);
                }

                _debounceTimer.Change(600, Timeout.Infinite);
            }
        }

        private void ProcesarLote(object state)
        {
            List<string> lote;
            lock (_lock)
            {
                if (_disposed || _pendientes.Count == 0)
                    return;

                lote = _pendientes.ToList();
                _pendientes.Clear();
            }

            List<string> listos = new List<string>();
            foreach (string ruta in lote)
            {
                if (EsperarArchivoLibre(ruta))
                {
                    listos.Add(ruta);
                }
            }

            if (listos.Count > 0 && !_disposed)
            {
                ModernToastNotifier.MostrarLoteRecibido(listos);
            }
        }

        private static bool EsperarArchivoLibre(string ruta)
        {
            for (int i = 0; i < 15; i++)
            {
                try
                {
                    if (!File.Exists(ruta))
                        return false;

                    using (FileStream fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                    Thread.Sleep(200);
                }
                catch
                {
                    return false;
                }
            }

            return File.Exists(ruta);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;

                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Dispose();
                    _watcher = null;
                }

                if (_debounceTimer != null)
                {
                    _debounceTimer.Dispose();
                    _debounceTimer = null;
                }
            }
        }
    }
}
