using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ONKOSFileTransfer.ONKOSTray;

namespace ONKOSFileTransfer
{
    public static class ModernToastNotifier
    {
        private static readonly object _syncLock = new object();
        private static bool _inicializado;

        public static void Inicializar()
        {
            lock (_syncLock)
            {
                if (_inicializado) return;
                _inicializado = true;
            }
        }

        public static void MostrarLoteRecibido(IReadOnlyList<string> rutasArchivos)
        {
            if (rutasArchivos == null || rutasArchivos.Count == 0)
                return;

            try
            {
                var archivosValidos = rutasArchivos
                    .Where(File.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (archivosValidos.Count == 0)
                    return;

                string ultimoArchivo = archivosValidos.Last();
                string carpetaDestino = Path.GetDirectoryName(ultimoArchivo) ?? @"C:\ProgramData\ONKOS\Inbox";
                string nombreSubcarpeta = new DirectoryInfo(carpetaDestino).Name;
                string origen = nombreSubcarpeta.Contains("_")
                    ? nombreSubcarpeta.Split('_')[0]
                    : nombreSubcarpeta;

                string titulo = archivosValidos.Count == 1
                    ? "Archivo recibido"
                    : $"{archivosValidos.Count} archivos recibidos";

                string mensaje = archivosValidos.Count == 1
                    ? $"{Path.GetFileName(ultimoArchivo)} · Desde {origen}"
                    : $"Último: {Path.GetFileName(ultimoArchivo)} · Desde {origen}";

                var thread = new Thread(() =>
                {
                    try
                    {
                        Application.EnableVisualStyles();
                        ToastNotificationForm toast = new ToastNotificationForm(
                            titulo,
                            mensaje,
                            carpetaDestino,
                            5500,
                            true
                        );
                        Application.Run(toast);
                    }
                    catch
                    {
                    }
                });

                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
            }
            catch
            {
            }
        }
    }
}
