using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ONKOSFileTransfer
{
    public static class InboxMonitor
    {
        public static readonly string CarpetaRecibidos =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Archivos Recibidos ONKOS");

        private static readonly object _lock = new object();
        private static readonly List<string> _pendientes = new List<string>();
        private static FileSystemWatcher _watcher;
        private static Timer _debounceTimer;
        private static bool _iniciado;

        public static void Iniciar()
        {
            lock (_lock)
            {
                if (_iniciado) return;

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

                _iniciado = true;
            }
        }

        private static void OnArchivoDetectado(object sender, FileSystemEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.FullPath))
                return;

            string nombre = Path.GetFileName(e.FullPath);
            if (string.Equals(nombre, "desktop.ini", StringComparison.OrdinalIgnoreCase))
                return;

            if (Directory.Exists(e.FullPath))
                return;

            lock (_lock)
            {
                if (!_pendientes.Contains(e.FullPath, StringComparer.OrdinalIgnoreCase))
                {
                    _pendientes.Add(e.FullPath);
                }

                _debounceTimer.Change(600, Timeout.Infinite);
            }
        }

        private static void ProcesarLote(object state)
        {
            List<string> lote;
            lock (_lock)
            {
                if (_pendientes.Count == 0)
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

            if (listos.Count > 0)
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

        public static void Detener()
        {
            lock (_lock)
            {
                _iniciado = false;
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
