using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace ONKOSFileTransfer
{
    public static class ModernToastNotifier
    {
        public static void Inicializar()
        {
            // Inicialización lista para notificaciones emergentes desde la bandeja
        }

        public static void MostrarArchivoRecibido(string rutaCompletaArchivo)
        {
            try
            {
                string nombreArchivo = Path.GetFileName(rutaCompletaArchivo);
                string carpetaContenedora = Path.GetDirectoryName(rutaCompletaArchivo);
                string nombreCarpetaOrigen = !string.IsNullOrEmpty(carpetaContenedora)
                    ? new DirectoryInfo(carpetaContenedora).Name
                    : "Red";

                string origen = nombreCarpetaOrigen.Contains("_")
                    ? nombreCarpetaOrigen.Split('_')[0]
                    : nombreCarpetaOrigen;

                // Si tienes ToastNotificationForm en ONKOSTray, puedes mostrarlo o usar BalloonTip
                var tray = Application.OpenForms.Count > 0 ? null : null;
                System.Media.SystemSounds.Asterisk.Play();

                // Abre la carpeta si se requiere o muestra aviso nativo desde la bandeja
                var tempIcon = new NotifyIcon
                {
                    Icon = System.Drawing.SystemIcons.Information,
                    Visible = true,
                    BalloonTipTitle = "Archivo recibido • ONKOS Transfer",
                    BalloonTipText = $"De: {origen}\nArchivo: {nombreArchivo}"
                };

                tempIcon.BalloonTipClicked += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(carpetaContenedora) && Directory.Exists(carpetaContenedora))
                    {
                        Process.Start("explorer.exe", $"\"{carpetaContenedora}\"");
                    }
                    tempIcon.Visible = false;
                    tempIcon.Dispose();
                };

                tempIcon.BalloonTipClosed += (s, e) =>
                {
                    tempIcon.Visible = false;
                    tempIcon.Dispose();
                };

                tempIcon.ShowBalloonTip(5000);
            }
            catch
            {
            }
        }
    }
}
