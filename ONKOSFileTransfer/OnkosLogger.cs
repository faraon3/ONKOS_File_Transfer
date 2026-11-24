using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ONKOSFileTransfer
{
    public static class OnkosLogger
    {
        private static readonly string LogsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ONKOS",
            "logs");

        private static readonly object SyncLock = new object();
        private const long MaxLogBytes = 2L * 1024 * 1024;
        private const int MaxArchiveFiles = 5;

        public static void Info(string source, string message)
        {
            Write("INFO", source, message, null);
        }

        public static void Warn(string source, string message, Exception ex = null)
        {
            Write("WARN", source, message, ex);
        }

        public static void Error(string source, string message, Exception ex = null)
        {
            Write("ERROR", source, message, ex);
        }

        private static void Write(string level, string source, string message, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(LogsFolder);
                string logFile = Path.Combine(LogsFolder, $"onkos_{DateTime.Now:yyyy-MM-dd}.log");

                var sb = new StringBuilder();
                sb.AppendFormat(
                    "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [{2}] {3}",
                    DateTime.Now,
                    level,
                    Sanitize(source),
                    Sanitize(message));

                if (ex != null)
                {
                    sb.Append(" | ");
                    sb.Append(Sanitize(ex.ToString()));
                }

                lock (SyncLock)
                {
                    RotateIfNeededLocked(logFile);
                    using (var fs = new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        sw.WriteLine(sb.ToString());
                    }
                }
            }
            catch (Exception logEx)
            {
                System.Diagnostics.Trace.TraceError($"OnkosLogger falló: {logEx.Message}");
            }
        }

        private static void RotateIfNeededLocked(string currentLogFile)
        {
            if (!File.Exists(currentLogFile))
                return;

            var fi = new FileInfo(currentLogFile);
            if (fi.Length < MaxLogBytes)
                return;

            string baseName = Path.GetFileNameWithoutExtension(currentLogFile);
            string ext = Path.GetExtension(currentLogFile);
            string archiveName = Path.Combine(LogsFolder, $"{baseName}_{DateTime.Now:HHmmss}{ext}");

            if (!File.Exists(archiveName))
                File.Move(currentLogFile, archiveName);

            var archives = Directory.GetFiles(LogsFolder, $"{baseName}_*{ext}")
                .OrderByDescending(f => f)
                .Skip(MaxArchiveFiles)
                .ToList();

            foreach (string old in archives)
            {
                try { File.Delete(old); }
                catch (Exception delEx) { System.Diagnostics.Trace.TraceWarning(delEx.Message); }
            }
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
