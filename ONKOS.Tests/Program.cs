using System;
using System.IO;
using System.Net;
using System.Text;
using ONKOS.Core.Discovery;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;
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
                NotificationRecord n1 = NotificationCenterStore.PublishGrouped(
                    NotificationCategory.Received,
                    "group-test-1",
                    "Archivo recibido",
                    "Ana Pérez — Facturación",
                    "acta_1.pdf");
                NotificationRecord n2 = NotificationCenterStore.PublishGrouped(
                    NotificationCategory.Received,
                    "group-test-1",
                    "Archivo recibido",
                    "Ana Pérez — Facturación",
                    "acta_2.pdf");

                if (n1 == null || n2 == null || n2.ItemCount < 2)
                    throw new InvalidOperationException("Falló la agrupación de notificaciones.");

                var limits = new TransferLimits();
                string safeRel = SecureTransferProtocol.NormalizeAndValidateRelativePath("carpeta/informe.pdf", limits);
                if (safeRel != Path.Combine("carpeta", "informe.pdf"))
                    throw new InvalidOperationException("Falló la validación de ruta relativa.");

                IPAddress bcast = LanDiscoveryService.ComputeBroadcast(
                    IPAddress.Parse("192.168.10.25"),
                    IPAddress.Parse("255.255.254.0"));
                if (bcast.ToString() != "192.168.11.255")
                    throw new InvalidOperationException("Falló el cálculo de broadcast /23.");

                FileLogger.Instance.Info("Phase5B.Tests", "Pruebas de agrupación de notificaciones, historial y red completadas.");
                Console.WriteLine("PHASE5B_TESTS_OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PHASE5B_TESTS_FAILED: " + ex.Message);
                return 1;
            }
        }
    }
}
