using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using ONKOS.Core.Storage;

namespace ONKOS.Core.Logging
{
    public sealed class FileLogger : ILogger
    {
        private const long MaxLogFileBytes = 5L * 1024L * 1024L;
        private static readonly Lazy<FileLogger> LazyInstance = new Lazy<FileLogger>(() => new FileLogger());
        private readonly object _sync = new object();

        public static FileLogger Instance => LazyInstance.Value;

        public string LogsFolder => PathAndAclHelper.LogsFolder;

        public void Info(string category, string message)
        {
            Write(LogLevel.Info, category, message, null);
        }

        public void Warn(string category, string message)
        {
            Write(LogLevel.Warn, category, message, null);
        }

        public void Error(string category, string message, Exception ex = null)
        {
            Write(LogLevel.Error, category, message, ex);
        }

        public void Write(LogLevel level, string category, string message, Exception ex)
        {
            string mutexName = @"Global\ONKOS_Log_Lock";
            bool createdNew;
            using (var mutex = new Mutex(false, mutexName, out createdNew))
            {
                bool acquired = false;
                try
                {
                    acquired = mutex.WaitOne(TimeSpan.FromSeconds(5));
                    lock (_sync)
                    {
                        PathAndAclHelper.EnsureBaseAndLogsFolders();
                        string logPath = GetCurrentLogFilePath();
                        RotateIfNeeded(logPath);

                        string cleanCat = Sanitize(category);
                        string cleanMsg = Sanitize(message);
                        string detail = ex == null
                            ? string.Empty
                            : $" | {ex.GetType().Name}: {Sanitize(ex.Message)}";

                        string line = string.Format(
                            CultureInfo.InvariantCulture,
                            "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [{2}] {3}{4}",
                            DateTime.Now,
                            level.ToString().ToUpperInvariant(),
                            cleanCat,
                            cleanMsg,
                            detail);

                        AtomicFileStore.AppendLine(logPath, line, mutexName);
                    }
                }
                catch (Exception writeEx)
                {
                    System.Diagnostics.Trace.TraceError("FileLogger error: " + writeEx.Message);
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

        private string GetCurrentLogFilePath()
        {
            string baseName = $"onkos-{DateTime.Now:yyyy-MM-dd}";
            string primary = Path.Combine(LogsFolder, baseName + ".log");
            if (!File.Exists(primary))
                return primary;

            try
            {
                var info = new FileInfo(primary);
                if (info.Length < MaxLogFileBytes)
                    return primary;
            }
            catch (IOException)
            {
                return primary;
            }

            int index = 1;
            while (true)
            {
                string candidate = Path.Combine(LogsFolder, $"{baseName}-{index}.log");
                if (!File.Exists(candidate))
                    return candidate;

                try
                {
                    if (new FileInfo(candidate).Length < MaxLogFileBytes)
                        return candidate;
                }
                catch (IOException)
                {
                    return candidate;
                }

                index++;
            }
        }

        private static void RotateIfNeeded(string logPath)
        {
            try
            {
                if (!File.Exists(logPath))
                    return;

                var info = new FileInfo(logPath);
                if (info.Length < MaxLogFileBytes)
                    return;

                string rotated = logPath + "." + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture) + ".bak";
                if (!File.Exists(rotated))
                    File.Move(logPath, rotated);
            }
            catch (IOException)
            {
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
