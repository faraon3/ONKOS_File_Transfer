using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace ONKOSFileTransfer
{
    public class InboxMonitor : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly ConcurrentDictionary<string, DateTime> _recientes = new ConcurrentDictionary<string, DateTime>();

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

            _watcher.Created += OnArchivoDetectado;
        }

        private void OnArchivoDetectado(object sender, FileSystemEventArgs e)
        {
            // Ignorar carpetas y archivos temporales mientras se escriben
            if (Directory.Exists(e.FullPath) || e.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                return;

            // Evitar notificaciones duplicadas por el mismo archivo en menos de 3 segundos
            DateTime ahora = DateTime.UtcNow;
            if (_recientes.TryGetValue(e.FullPath, out DateTime ultimoAviso) && (ahora - ultimoAviso).TotalSeconds < 3)
                return;

            _recientes[e.FullPath] = ahora;

            Task.Run(async () =>
            {
                // Espera breve para asegurar que el servicio terminó de escribir el archivo
                await Task.Delay(700);
                if (File.Exists(e.FullPath))
                {
                    ModernToastNotifier.MostrarArchivoRecibido(e.FullPath);
                }
            });
        }

        public void Dispose()
        {
            _watcher?.Dispose();
        }
    }
}
