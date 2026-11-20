using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ONKOSFileTransfer
{
    public class InboxMonitor : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly ConcurrentDictionary<string, DateTime> _vistosRecientemente = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly object _batchLock = new object();
        private readonly List<string> _pendientes = new List<string>();
        private CancellationTokenSource _debounceCts;
        private bool _disposed;

        public InboxMonitor(string rutaInbox = @"C:\ProgramData\ONKOS\Inbox")
        {
            if (!Directory.Exists(rutaInbox))
            {
                Directory.CreateDirectory(rutaInbox);
            }

            _watcher = new FileSystemWatcher(rutaInbox)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                Filter = "*.*",
                EnableRaisingEvents = true
            };

            _watcher.Created += OnArchivoCreado;
        }

        private void OnArchivoCreado(object sender, FileSystemEventArgs e)
        {
            if (_disposed || Directory.Exists(e.FullPath))
                return;

            string nombre = e.Name ?? string.Empty;
            if (nombre.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                nombre.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                return;

            DateTime ahora = DateTime.UtcNow;
            if (_vistosRecientemente.TryGetValue(e.FullPath, out DateTime ultimaVez) &&
                (ahora - ultimaVez).TotalSeconds < 2)
                return;

            _vistosRecientemente[e.FullPath] = ahora;
            EncolarArchivo(e.FullPath);
        }

        private void EncolarArchivo(string rutaArchivo)
        {
            CancellationToken token;

            lock (_batchLock)
            {
                if (_disposed) return;

                if (!_pendientes.Contains(rutaArchivo, StringComparer.OrdinalIgnoreCase))
                {
                    _pendientes.Add(rutaArchivo);
                }

                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = new CancellationTokenSource();
                token = _debounceCts.Token;
            }

            Task.Run(async () =>
            {
                try
                {
                    // Espera 1.2s para agrupar archivos que lleguen seguidos (ej. 10 o 20 archivos)
                    await Task.Delay(1200, token);

                    List<string> lote;
                    lock (_batchLock)
                    {
                        if (_disposed || token.IsCancellationRequested) return;
                        lote = _pendientes.ToList();
                        _pendientes.Clear();
                    }

                    // Espera breve adicional para confirmar que el servicio liberó el archivo
                    await Task.Delay(400, token);
                    if (token.IsCancellationRequested) return;

                    ModernToastNotifier.MostrarLoteRecibido(lote);
                }
                catch (TaskCanceledException)
                {
                    // Reiniciado porque llegó otro archivo dentro de la ventana de agrupación
                }
                catch
                {
                }
            }, token);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_batchLock)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
            }

            _watcher?.Dispose();
        }
    }
}
