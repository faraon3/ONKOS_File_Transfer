using System;
using System.IO;
using System.Text;
using ONKOS.Core.Logging;
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
                string tempDir = Path.Combine(Path.GetTempPath(), "ONKOS_Phase2B_Tests_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                var limits = new TransferLimits();
                string safeRel = SecureTransferProtocol.NormalizeAndValidateRelativePath("carpeta/informe.pdf", limits);
                if (safeRel != Path.Combine("carpeta", "informe.pdf"))
                    throw new InvalidOperationException("Falló la validación de ruta relativa.");

                bool threwTraversal = false;
                try
                {
                    SecureTransferProtocol.NormalizeAndValidateRelativePath("../../Windows/win.ini", limits);
                }
                catch (InvalidDataException)
                {
                    threwTraversal = true;
                }
                if (!threwTraversal)
                    throw new InvalidOperationException("No se bloqueó path traversal.");

                string sampleFile = Path.Combine(tempDir, "sample.txt");
                File.WriteAllText(sampleFile, "datos de prueba ONKOS", Encoding.UTF8);
                string hash = SecureTransferProtocol.ComputeFileSha256Hex(sampleFile);
                if (string.IsNullOrWhiteSpace(hash) || hash.Length != 64)
                    throw new InvalidOperationException("Falló el cálculo SHA-256.");

                FileLogger.Instance.Info("Phase2B.Tests", "Pruebas de protocolo, rutas y SHA-256 completadas.");

                try { Directory.Delete(tempDir, true); }
                catch (IOException) { }

                Console.WriteLine("PHASE2B_TESTS_OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PHASE2B_TESTS_FAILED: " + ex.Message);
                return 1;
            }
        }
    }
}
