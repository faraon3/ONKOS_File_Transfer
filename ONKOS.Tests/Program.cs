using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using ONKOS.Core.Discovery;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;
using ONKOS.Core.Notifications;
using ONKOS.Core.Protocol;
using ONKOS.Core.Security;
using ONKOS.Core.Storage;
using ONKOS.Core.Transfers;

namespace ONKOS.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ONKOS_Phase8A_Tests_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);

                // 1. Validación de ruta segura y bloqueo de traversal
                var limits = new TransferLimits();
                string safeRel = SecureTransferProtocol.NormalizeAndValidateRelativePath("carpeta/informe.pdf", limits);
                if (safeRel != Path.Combine("carpeta", "informe.pdf"))
                    throw new InvalidOperationException("Falló la normalización de ruta relativa válida.");

                bool blockedTraversal = false;
                try
                {
                    SecureTransferProtocol.NormalizeAndValidateRelativePath("../secreto.txt", limits);
                }
                catch (InvalidDataException)
                {
                    blockedTraversal = true;
                }
                if (!blockedTraversal)
                    throw new InvalidOperationException("No se bloqueó una ruta relativa con '..'.");

                // 2. Hash SHA-256 de archivo
                string sampleFile = Path.Combine(tempDir, "muestra.txt");
                File.WriteAllText(sampleFile, "ONKOS-V2-INTEGRITY", Encoding.UTF8);
                string sha = SecureTransferProtocol.ComputeFileSha256Hex(sampleFile);
                if (string.IsNullOrWhiteSpace(sha) || sha.Length != 64)
                    throw new InvalidOperationException("Falló el cálculo de SHA-256.");

                // 3. Cálculo de broadcast de subred (/23) y normalización de huella
                IPAddress bcast = LanDiscoveryService.ComputeBroadcast(
                    IPAddress.Parse("192.168.10.25"),
                    IPAddress.Parse("255.255.254.0"));
                if (bcast.ToString() != "192.168.11.255")
                    throw new InvalidOperationException("Falló el cálculo de broadcast /23.");

                string fpUpper = PeerAuthorizationStore.NormalizeFingerprint("AA:BB:CC:DD");
                string fpLower = PeerAuthorizationStore.NormalizeFingerprint("aa:bb:cc:dd");
                if (string.IsNullOrWhiteSpace(fpUpper) ||
                    !string.Equals(fpUpper, fpLower, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Falló la normalización de huella SHA-256.");
                }

                // 4. Caché de tamaño y cola
                PathSizeSummary sizeSum = PathSizeCache.GetOrComputeAsync(sampleFile, CancellationToken.None).GetAwaiter().GetResult();
                if (sizeSum.FileCount != 1 || sizeSum.TotalBytes <= 0)
                    throw new InvalidOperationException("Falló PathSizeCache.");

                using (var q = new TransferQueueManager())
                {
                    var enq = q.EnqueueBatch(
                        new[] { sampleFile },
                        new[] { new PeerInfo { FullName = "Prueba Local", IPv4 = "127.0.0.1" } });
                    if (enq.Count != 1)
                        throw new InvalidOperationException("Falló EnqueueBatch.");
                    q.CancelAllActive();
                }

                // 5. Notificaciones agrupadas
                NotificationRecord n1 = NotificationCenterStore.PublishGrouped(
                    NotificationCategory.Received,
                    "phase8a-group",
                    "Archivo recibido",
                    "Soporte — Sistemas",
                    "doc1.pdf");
                NotificationRecord n2 = NotificationCenterStore.PublishGrouped(
                    NotificationCategory.Received,
                    "phase8a-group",
                    "Archivo recibido",
                    "Soporte — Sistemas",
                    "doc2.pdf");
                if (n1 == null || n2 == null || n2.ItemCount < 2)
                    throw new InvalidOperationException("Falló la agrupación de notificaciones.");

                // 6. Presencia de instalador Inno Setup
                if (!File.Exists("ONKOS_Setup.iss") && !File.Exists(Path.Combine("..", "ONKOS_Setup.iss")))
                    throw new FileNotFoundException("No se encontró ONKOS_Setup.iss.");

                FileLogger.Instance.Info("Phase8A.Tests", "Batería completa de pruebas de Fase 8A aprobada.");
                Console.WriteLine("PHASE8A_TESTS_OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PHASE8A_TESTS_FAILED: " + ex.Message);
                return 1;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
