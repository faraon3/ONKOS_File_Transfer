using System;
using System.IO;
using System.Text;
using System.Threading;
using ONKOS.Core.Logging;

namespace ONKOS.Core.Storage
{
    public static class AtomicFileStore
    {
        public static void WriteAllTextAtomic(string targetPath, string content, Encoding encoding, string mutexName)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentException("La ruta de destino no es válida.", nameof(targetPath));

            string dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir))
                PathAndAclHelper.EnsureBaseAndLogsFolders();

            bool createdNew;
            using (var mutex = new Mutex(false, mutexName, out createdNew))
            {
                bool acquired = false;
                try
                {
                    acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
                    if (!acquired)
                        throw new TimeoutException($"Tiempo de espera agotado al adquirir el bloqueo {mutexName}.");

                    string tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var sw = new StreamWriter(fs, encoding ?? Encoding.UTF8))
                    {
                        sw.Write(content ?? string.Empty);
                        sw.Flush();
                        fs.Flush(true);
                    }

                    if (File.Exists(targetPath))
                    {
                        string backupPath = targetPath + ".bak";
                        File.Replace(tempPath, targetPath, backupPath, ignoreMetadataErrors: true);
                        try
                        {
                            if (File.Exists(backupPath))
                                File.Delete(backupPath);
                        }
                        catch (IOException ex)
                        {
                            FileLogger.Instance.Warn("AtomicFileStore", "No se pudo eliminar archivo temporal de respaldo: " + ex.Message);
                        }
                    }
                    else
                    {
                        File.Move(tempPath, targetPath);
                    }
                }
                finally
                {
                    if (acquired)
                    {
                        try { mutex.ReleaseMutex(); }
                        catch (ApplicationException) { }
                    }
                }
            }
        }

        public static void AppendLine(string targetPath, string line, string mutexName)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentException("La ruta de destino no es válida.", nameof(targetPath));

            PathAndAclHelper.EnsureBaseAndLogsFolders();

            bool createdNew;
            using (var mutex = new Mutex(false, mutexName, out createdNew))
            {
                bool acquired = false;
                try
                {
                    acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
                    if (!acquired)
                        throw new TimeoutException($"Tiempo de espera agotado al adquirir el bloqueo {mutexName}.");

                    using (var fs = new FileStream(targetPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        sw.WriteLine(line ?? string.Empty);
                        sw.Flush();
                        fs.Flush(true);
                    }
                }
                finally
                {
                    if (acquired)
                    {
                        try { mutex.ReleaseMutex(); }
                        catch (ApplicationException) { }
                    }
                }
            }
        }

        public static string ReadAllTextShared(string targetPath, Encoding encoding, string mutexName)
        {
            if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
                return string.Empty;

            bool createdNew;
            using (var mutex = new Mutex(false, mutexName, out createdNew))
            {
                bool acquired = false;
                try
                {
                    acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
                    using (var fs = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs, encoding ?? Encoding.UTF8))
                    {
                        return sr.ReadToEnd();
                    }
                }
                finally
                {
                    if (acquired)
                    {
                        try { mutex.ReleaseMutex(); }
                        catch (ApplicationException) { }
                    }
                }
            }
        }
    }
}
