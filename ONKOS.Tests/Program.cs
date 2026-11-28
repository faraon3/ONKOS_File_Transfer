using System;
using System.IO;
using System.Net;
using System.Text;
using ONKOS.Core.Discovery;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;
using ONKOS.Core.Protocol;
using ONKOS.Core.Security;
using ONKOS.Core.Storage;

namespace ONKOS.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "ONKOS_Phase3B_Tests_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                var limits = new TransferLimits();
                string safeRel = SecureTransferProtocol.NormalizeAndValidateRelativePath("carpeta/informe.pdf", limits);
                if (safeRel != Path.Combine("carpeta", "informe.pdf"))
                    throw new InvalidOperationException("Falló la validación de ruta relativa.");

                IPAddress bcast = LanDiscoveryService.ComputeBroadcast(
                    IPAddress.Parse("192.168.10.25"),
                    IPAddress.Parse("255.255.254.0"));
                if (bcast.ToString() != "192.168.11.255")
                    throw new InvalidOperationException("Falló el cálculo de broadcast /23.");

                string k1 = PeerContactsStore.BuildContactKey(new PeerInfo { FingerprintSha256 = "AB:CD:12:34", IPv4 = "192.168.1.10" });
                string k2 = PeerContactsStore.BuildContactKey(new PeerInfo { FingerprintSha256 = "abcd1234", IPv4 = "192.168.1.99" });
                if (!string.Equals(k1, k2, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Falló la deduplicación por huella ante cambio de IP.");

                FileLogger.Instance.Info("Phase3B.Tests", "Pruebas de subred, deduplicación por huella y protocolo completadas.");

                try { Directory.Delete(tempDir, true); }
                catch (IOException) { }

                Console.WriteLine("PHASE3B_TESTS_OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PHASE3B_TESTS_FAILED: " + ex.Message);
                return 1;
            }
        }
    }
}
