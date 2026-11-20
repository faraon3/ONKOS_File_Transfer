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
        private static readonly HashSet<string> _notificadosRecientes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static Thread _uiThread;
        private static ToastNotificationForm _activeToast;
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
                List<string> nuevosArchivos;
                lock (_syncLock)
                {
                    if (_notificadosRecientes.Count > 2000)
                        _notificadosRecientes.Clear();

                    nuevosArchivos = rutasArchivos
                        .Where(r => !string.IsNullOrWhiteSpace(r) && File.Exists(r))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Where(r => _notificadosRecientes.Add(r))
                        .ToList();
                }

                if (nuevosArchivos.Count == 0)
                    return;

                string ultimoArchivo = nuevosArchivos.Last();
                string carpetaDestino = Path.GetDirectoryName(ultimoArchivo) ?? InboxMonitor.CarpetaRecibidos;
                string nombreSubcarpeta = new DirectoryInfo(carpetaDestino).Name;
                string origen = nombreSubcarpeta.Contains("_")
                    ? nombreSubcarpeta.Split('_')[0]
                    : nombreSubcarpeta;

                string titulo = nuevosArchivos.Count == 1
                    ? "1 archivo recibido"
                    : $"{nuevosArchivos.Count} archivos recibidos";

                string mensaje = nuevosArchivos.Count == 1
                    ? $"{Path.GetFileName(ultimoArchivo)} · {origen}"
                    : $"Último: {Path.GetFileName(ultimoArchivo)} · {origen}";

                MostrarEnHiloUnico(titulo, mensaje, carpetaDestino);
            }
            catch
            {
            }
        }

        private static void MostrarEnHiloUnico(string titulo, string mensaje, string carpetaDestino)
        {
            lock (_syncLock)
            {
                if (_uiThread == null || !_uiThread.IsAlive)
                {
                    _uiThread = new Thread(() =>
                    {
                        Application.EnableVisualStyles();
                        Application.Run(new ApplicationContext());
                    });
                    _uiThread.SetApartmentState(ApartmentState.STA);
                    _uiThread.IsBackground = true;
                    _uiThread.Start();

                    Thread.Sleep(150);
                }

                try
                {
                    if (_activeToast != null && !_activeToast.IsDisposed)
                    {
                        if (_activeToast.InvokeRequired)
                        {
                            _activeToast.BeginInvoke(new Action(() =>
                            {
                                try { _activeToast.Close(); } catch { }
                            }));
                        }
                        else
                        {
                            _activeToast.Close();
                        }
                    }
                }
                catch
                {
                }
            }

            var abrirToast = new Thread(() =>
            {
                try
                {
                    ToastNotificationForm toast = new ToastNotificationForm(
                        titulo,
                        mensaje,
                        carpetaDestino,
                        4500,
                        true
                    );

                    lock (_syncLock)
                    {
                        _activeToast = toast;
                    }

                    toast.FormClosed += (s, e) =>
                    {
                        lock (_syncLock)
                        {
                            if (ReferenceEquals(_activeToast, toast))
                                _activeToast = null;
                        }
                    };

                    Application.Run(toast);
                }
                catch
                {
                }
            });

            abrirToast.SetApartmentState(ApartmentState.STA);
            abrirToast.IsBackground = true;
            abrirToast.Start();
        }
    }
}
