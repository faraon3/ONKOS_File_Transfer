using System;
using System.IO;
using System.Net;
using ONKOS.Core.Discovery;
using ONKOS.Core.Logging;
using ONKOS.Core.Notifications;
using ONKOS.Core.Protocol;
using ONKOS.Core.Storage;
using ONKOS.UI;

namespace ONKOS.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                ThemePalette dark = ThemeManager.GetPalette(AppThemeMode.Dark);
                ThemePalette hc = ThemeManager.GetPalette(AppThemeMode.HighContrast);
                if (dark.WindowBg == hc.WindowBg)
                    throw new InvalidOperationException("Falló la distinción de paletas de tema.");

                NotificationRecord n1 = NotificationCenterStore.PublishGrouped(
                    NotificationCategory.Received,
                    "phase6-group",
                    "Archivo recibido",
                    "Soporte — Sistemas",
                    "reporte.pdf");
                if (n1 == null)
                    throw new InvalidOperationException("Falló la publicación de notificación.");

                var limits = new TransferLimits();
                string safeRel = SecureTransferProtocol.NormalizeAndValidateRelativePath("carpeta/informe.pdf", limits);
                if (safeRel != Path.Combine("carpeta", "informe.pdf"))
                    throw new InvalidOperationException("Falló la validación de ruta relativa.");

                IPAddress bcast = LanDiscoveryService.ComputeBroadcast(
                    IPAddress.Parse("192.168.10.25"),
                    IPAddress.Parse("255.255.254.0"));
                if (bcast.ToString() != "192.168.11.255")
                    throw new InvalidOperationException("Falló el cálculo de broadcast /23.");

                FileLogger.Instance.Info("Phase6B.Tests", "Pruebas de temas, notificaciones, protocolo y red completadas.");
                Console.WriteLine("PHASE6B_TESTS_OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PHASE6B_TESTS_FAILED: " + ex.Message);
                return 1;
            }
        }
    }
}
