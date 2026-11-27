using System;
using System.IO;
using System.Text;
using ONKOS.Core.Logging;
using ONKOS.Core.Storage;

namespace ONKOS.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "ONKOS_Phase1_Tests_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                string targetFile = Path.Combine(tempDir, "test.txt");
                string mutexName = @"Global\ONKOS_Test_" + Guid.NewGuid().ToString("N");

                AtomicFileStore.WriteAllTextAtomic(targetFile, "linea-1", Encoding.UTF8, mutexName);
                AtomicFileStore.AppendLine(targetFile, "linea-2", mutexName);

                string readBack = AtomicFileStore.ReadAllTextShared(targetFile, Encoding.UTF8, mutexName);
                if (!readBack.Contains("linea-1") || !readBack.Contains("linea-2"))
                    throw new InvalidOperationException("Falló la lectura atómica de prueba.");

                FileLogger.Instance.Info("Phase1.Tests", "Prueba básica de almacenamiento y logger completada.");

                try { Directory.Delete(tempDir, true); }
                catch (IOException) { }

                Console.WriteLine("PHASE1_TESTS_OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PHASE1_TESTS_FAILED: " + ex.Message);
                return 1;
            }
        }
    }
}
