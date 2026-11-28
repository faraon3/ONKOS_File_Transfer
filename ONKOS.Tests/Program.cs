using System;
using System.IO;
using System.Text;
using ONKOS.Core.Logging;
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
                string tempDir = Path.Combine(Path.GetTempPath(), "ONKOS_Phase2A_Tests_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                string targetFile = Path.Combine(tempDir, "test.txt");
                string mutexName = @"Global\ONKOS_Test_" + Guid.NewGuid().ToString("N");

                AtomicFileStore.WriteAllTextAtomic(targetFile, "linea-1", Encoding.UTF8, mutexName);
                AtomicFileStore.AppendLine(targetFile, "linea-2", mutexName);

                string readBack = AtomicFileStore.ReadAllTextShared(targetFile, Encoding.UTF8, mutexName);
                if (!readBack.Contains("linea-1") || !readBack.Contains("linea-2"))
                    throw new InvalidOperationException("Falló la lectura atómica de prueba.");

                string normalized = PeerAuthorizationStore.NormalizeFingerprint("aa:bb:cc:dd");
                if (normalized != "AABBCCDD")
                    throw new InvalidOperationException("Falló la normalización de huella.");

                FileLogger.Instance.Info("Phase2A.Tests", "Prueba de almacenes y seguridad base completada.");

                try { Directory.Delete(tempDir, true); }
                catch (IOException) { }

                Console.WriteLine("PHASE2A_TESTS_OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PHASE2A_TESTS_FAILED: " + ex.Message);
                return 1;
            }
        }
    }
}
