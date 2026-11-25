using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ONKOSFileTransfer
{
    public sealed class TransferHistoryEntry
    {
        public DateTime TimestampLocal { get; set; }
        public string Direction { get; set; }
        public string ItemName { get; set; }
        public long SizeBytes { get; set; }
        public string Counterparty { get; set; }
        public string Status { get; set; }
        public string TargetPath { get; set; }
        public string Notes { get; set; }
    }

    public static class TransferHistoryStore
    {
        private const string CrossProcessMutexName = "Global\\ONKOS_HistoryStore_Mutex";

        private static readonly string BaseFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ONKOS");

        private static readonly string HistoryFilePath = Path.Combine(
            BaseFolder,
            "historial_transferencias.tsv");

        private static readonly string UnseenReceivedFilePath = Path.Combine(
            BaseFolder,
            "recibidos_no_vistos.txt");

        private static readonly object FileLock = new object();
        private const int MaxEntries = 1000;

        public static event Action UnseenReceivedCountChanged;

        private static void WithCrossProcessLock(Action action)
        {
            lock (FileLock)
            {
                using (var mutex = new Mutex(false, CrossProcessMutexName))
                {
                    bool acquired = false;
                    try
                    {
                        try
                        {
                            acquired = mutex.WaitOne(TimeSpan.FromSeconds(5));
                        }
                        catch (AbandonedMutexException)
                        {
                            acquired = true;
                        }

                        action();
                    }
                    finally
                    {
                        if (acquired)
                        {
                            try { mutex.ReleaseMutex(); }
                            catch (Exception ex) { OnkosLogger.Warn("TransferHistoryStore", "Error liberando Mutex.", ex); }
                        }
                    }
                }
            }
        }

        public static void AppendEntry(
            string direction,
            string itemName,
            long sizeBytes,
            string counterparty,
            string status,
            string targetPath = "",
            string notes = "")
        {
            bool incrementedUnseen = false;

            try
            {
                Directory.CreateDirectory(BaseFolder);

                DateTime now = DateTime.Now;
                string cleanDirection = Sanitize(direction);
                string cleanStatus = Sanitize(status);

                string line = string.Join("\t",
                    now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    cleanDirection,
                    Sanitize(itemName),
                    Math.Max(0L, sizeBytes).ToString(CultureInfo.InvariantCulture),
                    Sanitize(counterparty),
                    cleanStatus,
                    Sanitize(targetPath),
                    Sanitize(notes));

                WithCrossProcessLock(() =>
                {
                    using (var fs = new FileStream(HistoryFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        sw.WriteLine(line);
                    }

                    TrimIfNeededLocked();

                    if (string.Equals(cleanDirection, "RECIBIDO", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(cleanStatus, "Completado", StringComparison.OrdinalIgnoreCase))
                    {
                        int current = ReadUnseenCountLocked();
                        WriteUnseenCountLocked(current + 1);
                        incrementedUnseen = true;
                    }
                });
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("TransferHistoryStore", "Error agregando entrada al historial.", ex);
            }

            if (incrementedUnseen)
                UnseenReceivedCountChanged?.Invoke();
        }

        public static int GetUnseenReceivedCount()
        {
            int count = 0;
            try
            {
                WithCrossProcessLock(() => { count = ReadUnseenCountLocked(); });
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("TransferHistoryStore", "Error leyendo contador de no vistos.", ex);
            }
            return count;
        }

        public static void MarkReceivedAsSeen()
        {
            bool changed = false;
            try
            {
                WithCrossProcessLock(() =>
                {
                    if (ReadUnseenCountLocked() > 0)
                    {
                        Directory.CreateDirectory(BaseFolder);
                        WriteUnseenCountLocked(0);
                        changed = true;
                    }
                });
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("TransferHistoryStore", "Error marcando recibidos como vistos.", ex);
            }

            if (changed)
                UnseenReceivedCountChanged?.Invoke();
        }

        private static int ReadUnseenCountLocked()
        {
            if (!File.Exists(UnseenReceivedFilePath))
                return 0;

            using (var fs = new FileStream(UnseenReceivedFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
            {
                string raw = sr.ReadToEnd().Trim();
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val) && val > 0)
                    return val;
            }

            return 0;
        }

        private static void WriteUnseenCountLocked(int count)
        {
            int safe = Math.Max(0, count);
            string tempFile = UnseenReceivedFilePath + ".tmp";
            File.WriteAllText(tempFile, safe.ToString(CultureInfo.InvariantCulture), Encoding.UTF8);

            if (File.Exists(UnseenReceivedFilePath))
                File.Replace(tempFile, UnseenReceivedFilePath, null);
            else
                File.Move(tempFile, UnseenReceivedFilePath);
        }

        public static List<TransferHistoryEntry> LoadEntries(string directionFilter = null, string searchText = null)
        {
            var list = new List<TransferHistoryEntry>();

            try
            {
                if (!File.Exists(HistoryFilePath))
                    return list;

                string[] lines = Array.Empty<string>();
                WithCrossProcessLock(() =>
                {
                    if (!File.Exists(HistoryFilePath))
                        return;

                    using (var fs = new FileStream(HistoryFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        string content = sr.ReadToEnd();
                        lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    }
                });

                foreach (string line in lines)
                {
                    string[] cols = line.Split('\t');
                    if (cols.Length < 6)
                        continue;

                    if (!DateTime.TryParseExact(
                        cols[0],
                        "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateTime dt))
                    {
                        DateTime.TryParse(cols[0], out dt);
                    }

                    long.TryParse(cols[3], NumberStyles.Any, CultureInfo.InvariantCulture, out long bytes);

                    var entry = new TransferHistoryEntry
                    {
                        TimestampLocal = dt == DateTime.MinValue ? DateTime.Now : dt,
                        Direction = cols[1].Trim(),
                        ItemName = cols[2].Trim(),
                        SizeBytes = bytes,
                        Counterparty = cols[4].Trim(),
                        Status = cols[5].Trim(),
                        TargetPath = cols.Length > 6 ? cols[6].Trim() : string.Empty,
                        Notes = cols.Length > 7 ? cols[7].Trim() : string.Empty
                    };

                    list.Add(entry);
                }
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("TransferHistoryStore", "Error cargando historial.", ex);
            }

            IEnumerable<TransferHistoryEntry> query = list.AsEnumerable().Reverse();

            if (!string.IsNullOrWhiteSpace(directionFilter) &&
                !string.Equals(directionFilter, "TODOS", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => string.Equals(x.Direction, directionFilter, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                string q = searchText.Trim().ToLowerInvariant();
                query = query.Where(x =>
                    (x.ItemName ?? string.Empty).ToLowerInvariant().Contains(q) ||
                    (x.Counterparty ?? string.Empty).ToLowerInvariant().Contains(q) ||
                    (x.Status ?? string.Empty).ToLowerInvariant().Contains(q) ||
                    (x.Notes ?? string.Empty).ToLowerInvariant().Contains(q));
            }

            return query.ToList();
        }

        public static void ClearAll()
        {
            try
            {
                WithCrossProcessLock(() =>
                {
                    if (File.Exists(HistoryFilePath))
                        File.Delete(HistoryFilePath);
                    if (File.Exists(UnseenReceivedFilePath))
                        File.Delete(UnseenReceivedFilePath);
                });
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("TransferHistoryStore", "Error limpiando historial.", ex);
            }

            UnseenReceivedCountChanged?.Invoke();
        }

        private static void TrimIfNeededLocked()
        {
            try
            {
                string[] allLines = File.ReadAllLines(HistoryFilePath, Encoding.UTF8);
                if (allLines.Length > MaxEntries)
                {
                    string[] kept = allLines.Skip(allLines.Length - MaxEntries).ToArray();
                    string tempFile = HistoryFilePath + ".tmp";
                    File.WriteAllLines(tempFile, kept, Encoding.UTF8);

                    if (File.Exists(HistoryFilePath))
                        File.Replace(tempFile, HistoryFilePath, null);
                    else
                        File.Move(tempFile, HistoryFilePath);
                }
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("TransferHistoryStore", "Error recortando historial.", ex);
            }
        }

        private static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return input
                .Replace("\t", " ")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }
    }
}
