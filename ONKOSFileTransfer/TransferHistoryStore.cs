using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ONKOSFileTransfer
{
    public sealed class TransferHistoryEntry
    {
        public DateTime TimestampLocal { get; set; }
        public string Direction { get; set; } // "ENVIADO" o "RECIBIDO"
        public string ItemName { get; set; }
        public long SizeBytes { get; set; }
        public string Counterparty { get; set; }
        public string Status { get; set; } // "Completado" o "Error"
        public string TargetPath { get; set; }
        public string Notes { get; set; }
    }

    public static class TransferHistoryStore
    {
        private static readonly string HistoryFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ONKOS",
            "historial_transferencias.tsv");

        private static readonly object FileLock = new object();
        private const int MaxEntries = 1000;

        public static void AppendEntry(
            string direction,
            string itemName,
            long sizeBytes,
            string counterparty,
            string status,
            string targetPath = "",
            string notes = "")
        {
            try
            {
                string dir = Path.GetDirectoryName(HistoryFilePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                DateTime now = DateTime.Now;
                string line = string.Join("\t",
                    now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    Sanitize(direction),
                    Sanitize(itemName),
                    Math.Max(0L, sizeBytes).ToString(CultureInfo.InvariantCulture),
                    Sanitize(counterparty),
                    Sanitize(status),
                    Sanitize(targetPath),
                    Sanitize(notes));

                lock (FileLock)
                {
                    using (var fs = new FileStream(HistoryFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        sw.WriteLine(line);
                    }

                    TrimIfNeededLocked();
                }
            }
            catch
            {
            }
        }

        public static List<TransferHistoryEntry> LoadEntries(string directionFilter = null, string searchText = null)
        {
            var list = new List<TransferHistoryEntry>();

            try
            {
                if (!File.Exists(HistoryFilePath))
                    return list;

                string[] lines;
                lock (FileLock)
                {
                    using (var fs = new FileStream(HistoryFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        string content = sr.ReadToEnd();
                        lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    }
                }

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
            catch
            {
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
                lock (FileLock)
                {
                    if (File.Exists(HistoryFilePath))
                        File.Delete(HistoryFilePath);
                }
            }
            catch
            {
            }
        }

        private static void TrimIfNeededLocked()
        {
            try
            {
                string[] allLines = File.ReadAllLines(HistoryFilePath, Encoding.UTF8);
                if (allLines.Length > MaxEntries)
                {
                    string[] kept = allLines.Skip(allLines.Length - MaxEntries).ToArray();
                    File.WriteAllLines(HistoryFilePath, kept, Encoding.UTF8);
                }
            }
            catch
            {
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
