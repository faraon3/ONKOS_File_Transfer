using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Toolkit.Uwp.Notifications;

namespace ONKOSFileTransfer
{
    public static class ModernToastNotifier
    {
        public static void Inicializar()
        {
            ToastNotificationManagerCompat.OnActivated += toastArgs =>
            {
                try
                {
                    ToastArguments args = ToastArguments.Parse(toastArgs.Argument);

                    if (args.TryGetValue("action", out string action) && action == "openFolder")
                    {
                        if (args.TryGetValue("folderPath", out string folderPath) && Directory.Exists(folderPath))
                        {
                            Process.Start("explorer.exe", $"\"{folderPath}\"");
                        }
                    }
                }
                catch
                {
                    // Evita cerrar la bandeja si ocurre un error al abrir el explorador
                }
            };
        }

        public static void MostrarArchivoRecibido(string rutaCompletaArchivo)
        {
            try
            {
                string nombreArchivo = Path.GetFileName(rutaCompletaArchivo);
                string carpetaContenedora = Path.GetDirectoryName(rutaCompletaArchivo);
                string nombreCarpetaOrigen = new DirectoryInfo(carpetaContenedora).Name;

                // Extrae la IP o equipo origen si la carpeta tiene formato "IP_fecha" o similar
                string origen = nombreCarpetaOrigen.Contains("_")
                    ? nombreCarpetaOrigen.Split('_')[0]
                    : nombreCarpetaOrigen;

                var builder = new ToastContentBuilder()
                    .AddArgument("action", "openFolder")
                    .AddArgument("folderPath", carpetaContenedora)
                    .AddText("Archivo recibido • ONKOS Transfer")
                    .AddText($"Origen: {origen}")
                    .AddText(nombreArchivo)
                    .AddButton(new ToastButton()
                        .SetContent("Abrir carpeta")
                        .AddArgument("action", "openFolder")
                        .AddArgument("folderPath", carpetaContenedora))
                    .AddButton(new ToastButtonDismiss("Descartar"));

                // Si colocas un logo PNG llamado onkos_logo.png junto al .exe, lo muestra en círculo
                string rutaLogo = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "onkos_logo.png");
                if (File.Exists(rutaLogo))
                {
                    builder.AddAppLogoOverride(new Uri(rutaLogo), ToastGenericAppLogoCrop.Circle);
                }

                builder.Show();
            }
            catch
            {
                // Si el sistema bloquea notificaciones (ej. No molestar), no interrumpe la recepción
            }
        }
    }
}
