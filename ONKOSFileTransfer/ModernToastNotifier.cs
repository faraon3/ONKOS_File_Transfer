using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ONKOSFileTransfer
{
    public static class ModernToastNotifier
    {
        private static readonly object _syncLock = new object();
        private static List<string> _ultimosArchivos = new List<string>();
        private static NotifyIcon _notifyIcon;
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

                lock (_syncLock)
                {
                    _ultimosArchivos = archivosValidos;
                }

                string titulo = archivosValidos.Count == 1
                    ? "ONKOS • Archivo recibido"
                    : $"ONKOS • {archivosValidos.Count} archivos recibidos";

                string cuerpo = archivosValidos.Count == 1
                    ? $"{Path.GetFileName(ultimoArchivo)} ({origen})"
                    : $"Desde {origen} • Clic para abrir y seleccionar";

                if (cuerpo.Length > 120)
                {
                    cuerpo = cuerpo.Substring(0, 117) + "...";
                }

                // Ejecutar en hilo UI/STA para que el globo y su clic funcionen sin fallos
                var thread = new Thread(() =>
                {
                    try
                    {
                        DestruirIconoAnterior();

                        Icon iconoApp = ObtenerIconoAplicacion();
                        var icon = new NotifyIcon
                        {
                            Icon = iconoApp,
                            Visible = true,
                            BalloonTipTitle = titulo,
                            BalloonTipText = cuerpo,
                            BalloonTipIcon = ToolTipIcon.Info
                        };

                        lock (_syncLock)
                        {
                            _notifyIcon = icon;
                        }

                        icon.BalloonTipClicked += (s, e) =>
                        {
                            AbrirSeleccionEnExplorador();
                            LimpiarIcono(icon);
                        };

                        icon.BalloonTipClosed += (s, e) =>
                        {
                            LimpiarIcono(icon);
                        };

                        icon.ShowBalloonTip(5000);

                        // Mantener vivo el hilo un momento para recibir el clic del usuario
                        DateTime limite = DateTime.UtcNow.AddSeconds(7);
                        while (DateTime.UtcNow < limite)
                        {
                            Application.DoEvents();
                            Thread.Sleep(50);
                        }

                        LimpiarIcono(icon);
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

        private static Icon ObtenerIconoAplicacion()
        {
            try
            {
                string exePath = Application.ExecutablePath;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    Icon extraido = Icon.ExtractAssociatedIcon(exePath);
                    if (extraido != null)
                        return extraido;
                }
            }
            catch
            {
            }

            return SystemIcons.Information;
        }

        private static void AbrirSeleccionEnExplorador()
        {
            try
            {
                List<string> archivos;
                lock (_syncLock)
                {
                    archivos = _ultimosArchivos.Where(File.Exists).ToList();
                }

                if (archivos.Count > 0)
                {
                    string archivoObjetivo = archivos.Last();
                    Process.Start("explorer.exe", $"/select,\"{archivoObjetivo}\"");
                    return;
                }

                string inbox = @"C:\ProgramData\ONKOS\Inbox";
                if (Directory.Exists(inbox))
                {
                    Process.Start("explorer.exe", $"\"{inbox}\"");
                }
            }
            catch
            {
            }
        }

        private static void LimpiarIcono(NotifyIcon icon)
        {
            try
            {
                if (icon != null)
                {
                    icon.Visible = false;
                    icon.Dispose();
                }
            }
            catch
            {
            }
        }

        private static void DestruirIconoAnterior()
        {
            NotifyIcon anterior;
            lock (_syncLock)
            {
                anterior = _notifyIcon;
                _notifyIcon = null;
            }
            LimpiarIcono(anterior);
        }
    }
}
