using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using ONKOS.Core.Discovery;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;
using ONKOS.Core.Protocol;
using ONKOS.Core.Storage;
using ONKOS.Core.Transfers;

namespace ONKOS.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "ONKOS_Phase4B_Tests_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                string fileA = Path.Combine(tempDir, "a.bin");
                File.WriteAllBytes(fileA, new byte[4096]);
                PathSizeSummary sum = PathSizeCache.GetOrComputeAsync(fileA, CancellationToken.None).GetAwaiter().GetResult();
                if (sum.TotalBytes != 4096L || sum.FileCount != 1)
                    throw new InvalidOperationException("Falló el cálculo en caché de tamaño.");

                using (var q = new TransferQueueManager())
                {
                    var enqueued = q.EnqueueBatch(
                        new[] { fileA },
                        new[] { new PeerInfo { FullName = "Destino Prueba", IPv4 = "127.0.0.1" } });
                    if (enqueued.Count != 1)
                        throw new InvalidOperationException("Falló el encolado del lote.");
                    q.CancelAllActive();
                }

                IPAddress bcast = LanDiscoveryService.ComputeBroadcast(
                    IPAddress.Parse("192.168.10.25"),
                    IPAddress.Parse("255.255.254.0"));
                if (bcast.ToString() != "192.168.11.255")
                    throw new InvalidOperationException("Falló el cálculo de broadcast /23.");

                FileLogger.Instance.Info("Phase4B.Tests", "Pruebas de caché de tamaño, cola y red completadas.");

                try { Directory.Delete(tempDir, true); }
                catch (IOException) { }

                Console.WriteLine("PHASE4B_TESTS_OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PHASE4B_TESTS_FAILED: " + ex.Message);
                return 1;
            }
        }
    }
}
