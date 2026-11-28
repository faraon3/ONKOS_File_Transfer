using System;
using System.IO;
using System.Net;
using ONKOS.Core.Discovery;
using ONKOS.Core.Logging;
using ONKOS.Core.Notifications;
using ONKOS.Core.Protocol;
using ONKOS.Core.Storage;

namespace ONKOS.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                if (!File.Exists("ONKOS_Setup.iss") && !File.Exists(Path.Combine("..", "ONKOS_Setup.iss")))
                    throw new FileNotFoundException("No se encontró ONKOS_Setup.iss en el entorno de pruebas.");

                NotificationRecord n1 = NotificationCenterStore.PublishGrouped(
                    NotificationCategory.Received,
                    "phase7-group",
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

                FileLogger.Instance.Info("Phase7B.Tests", "Pruebas de empaquetado, notificaciones, protocolo y red completadas.");
                Console.WriteLine("PHASE7B_TESTS_OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PHASE7B_TESTS_FAILED: " + ex.Message);
                return 1;
            }
        }
    }
}
