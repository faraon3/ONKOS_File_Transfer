using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Toolkit.Uwp.Notifications;

namespace ONKOSFileTransfer
{
    public static class ModernToastNotifier
    {
        private const string ToastTag = "onkos_inbox_batch";
        private static bool _inicializado = false;
        private static readonly object _syncLock = new object();
        private static List<string> _ultimosArchivos = new List<string>();

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr ILCreateFromPathW([MarshalAs(UnmanagedType.LPWStr)] string pszPath);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        [DllImport("shell32.dll")]
        private static extern int SHOpenFolderAndSelectItems(
            IntPtr pidlFolder,
            uint cidl,
            [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            uint dwFlags);

        public static void Inicializar()
        {
            lock (_syncLock)
            {
                if (_inicializado) return;
                _inicializado = true;

                ToastNotificationManagerCompat.OnActivated += toastArgs =>
                {
                    try
                    {
                        List<string> archivosASeleccionar;
                        lock (_syncLock)
                        {
                            archivosASeleccionar = _ultimosArchivos
                                .Where(File.Exists)
                                .ToList();
                        }

                        if (archivosASeleccionar.Count > 0)
                        {
                            AbrirCarpetaYSeleccionar(archivosASeleccionar);
                            return;
                        }

                        var args = ToastArguments.Parse(toastArgs.Argument);
                        if (args.TryGetValue("folder", out string folder) && Directory.Exists(folder))
                        {
                            Process.Start("explorer.exe", $"\"{folder}\"");
                        }
                    }
                    catch
                    {
                    }
                };
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

                string lineaPrincipal = archivosValidos.Count == 1
                    ? Path.GetFileName(ultimoArchivo)
                    : $"{archivosValidos.Count} archivos recibidos";

                var builder = new ToastContentBuilder()
                    .AddArgument("folder", carpetaDestino)
                    .AddText(lineaPrincipal)
                    .AddText($"ONKOS • {origen}");

                string rutaLogo = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "onkos_logo.png");
                if (File.Exists(rutaLogo))
                {
                    builder.AddAppLogoOverride(new Uri(rutaLogo), ToastGenericAppLogoCrop.Circle);
                }

                // Reemplaza la notificación previa con el mismo Tag para mantenerla compacta
                builder.Show(toast =>
                {
                    toast.Tag = ToastTag;
                    toast.Group = "ONKOS";
                });
            }
            catch
            {
            }
        }

        private static void AbrirCarpetaYSeleccionar(List<string> archivos)
        {
            string carpeta = Path.GetDirectoryName(archivos[0]);
            if (string.IsNullOrEmpty(carpeta) || !Directory.Exists(carpeta))
                return;

            IntPtr carpetaPidl = IntPtr.Zero;
            var itemPidls = new List<IntPtr>();

            try
            {
                carpetaPidl = ILCreateFromPathW(carpeta);
                if (carpetaPidl == IntPtr.Zero)
                {
                    Process.Start("explorer.exe", $"\"{carpeta}\"");
                    return;
                }

                foreach (string ruta in archivos)
                {
                    if (!File.Exists(ruta)) continue;
                    IntPtr fullPidl = ILCreateFromPathW(ruta);
                    if (fullPidl != IntPtr.Zero)
                    {
                        itemPidls.Add(fullPidl);
                    }
                }

                if (itemPidls.Count > 0)
                {
                    // Selecciona en el Explorador los archivos recibidos dentro de la carpeta
                    var soloHijos = new IntPtr[itemPidls.Count];
                    for (int i = 0; i < itemPidls.Count; i++)
                    {
                        // Si SHOpenFolderAndSelectItems requiere PIDLs relativos o se usa explorer /select para 1 archivo:
                        soloHijos[i] = itemPidls[i];
                    }

                    if (itemPidls.Count == 1)
                    {
                        Process.Start("explorer.exe", $"/select,\"{archivos[0]}\"");
                    }
                    else
                    {
                        Process.Start("explorer.exe", $"/select,\"{archivos.Last()}\"");
                    }
                }
                else
                {
                    Process.Start("explorer.exe", $"\"{carpeta}\"");
                }
            }
            finally
            {
                foreach (IntPtr pidl in itemPidls)
                {
                    if (pidl != IntPtr.Zero) ILFree(pidl);
                }
                if (carpetaPidl != IntPtr.Zero) ILFree(carpetaPidl);
            }
        }
    }
}
