using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;

namespace ONKOS.Core.Storage
{
    public static class TransferHistoryStore
    {
        private const string HistoryMutex = @"Global\ONKOS_History_Lock";
        private const string UnseenMutex = @"Global\ONKOS_Unseen_Lock";

        public static int RetentionDays { get; set; } = 90;

        public static event Action UnseenReceivedCountChanged;

        public static void AppendEntry(
            string direction,
            string itemName,
            long sizeBytes,
            string counterparty,
            string status,
            string targetPath = "",
            string sha256 = "",
            string notes = "")
        {
            bool incrementedUnseen = false;

            try
            {
                PathAndAclHelper.EnsureBaseAndLogsFolders();

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
                    Sanitize(sha256),
                    Sanitize(notes));

                AtomicFileStore.AppendLine(PathAndAclHelper.HistoryFilePath, line, HistoryMutex);

                if (string.Equals(cleanDirection, "RECIBIDO", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(cleanStatus, "Completado", StringComparison.OrdinalIgnoreCase))
                {
                    IncrementUnseenCount();
                    incrementedUnseen = true;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("TransferHistoryStore", "Error al registrar entrada de historial.", ex);
            }

            if (incrementedUnseen)
                RaiseUnseenChanged();
        }

        public static void ApplyRetention(int? daysOverride = null)
        {
            int days = daysOverride ?? RetentionDays;
            if (days <= 0 || !File.Exists(PathAndAclHelper.HistoryFilePath))
                return;

            try
            {
                DateTime cutoff = DateTime.Now.AddDays(-days);
                string raw = AtomicFileStore.ReadAllTextShared(PathAndAclHelper.HistoryFilePath, Encoding.UTF8, HistoryMutex);
                string[] lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                var kept = new List<string>();

                foreach (string line in lines)
                {
                    string[] cols = line.Split('\t');
                    if (cols.Length < 1)
                        continue;

                    DateTime dt;
                    if (!DateTime.TryParseExact(cols[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                        DateTime.TryParse(cols[0], out dt);

                    if (dt == DateTime.MinValue || dt >= cutoff)
                        kept.Add(line);
                }

                if (kept.Count != lines.Length)
                {
                    string content = kept.Count == 0
                        ? string.Empty
                        : string.Join(Environment.NewLine, kept) + Environment.NewLine;

                    AtomicFileStore.WriteAllTextAtomic(PathAndAclHelper.HistoryFilePath, content, Encoding.UTF8, HistoryMutex);
                    FileLogger.Instance.Info("TransferHistoryStore", $"Retención aplicada ({days} días). Filas conservadas: {kept.Count}.");
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("TransferHistoryStore", $"Error aplicando retención de historial ({ex.Message}).");
            }
        }

        private static void IncrementUnseenCount()
        {
            bool createdNew;
            using (var mutex = new Mutex(false, UnseenMutex, out createdNew))
            {
                bool acquired = false;
                try
                {
                    acquired = mutex.WaitOne(TimeSpan.FromSeconds(5));
                    int current = ReadUnseenUnsafe();
                    string nextText = (current + 1).ToString(CultureInfo.InvariantCulture);
                    string tempPath = PathAndAclHelper.UnseenReceivedFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(tempPath, nextText, Encoding.UTF8);

                    if (File.Exists(PathAndAclHelper.UnseenReceivedFilePath))
                        File.Replace(tempPath, PathAndAclHelper.UnseenReceivedFilePath, null, true);
                    else
                        File.Move(tempPath, PathAndAclHelper.UnseenReceivedFilePath);
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

        public static int GetUnseenReceivedCount()
        {
            try
            {
                bool createdNew;
                using (var mutex = new Mutex(false, UnseenMutex, out createdNew))
                {
                    bool acquired = false;
                    try
                    {
                        acquired = mutex.WaitOne(TimeSpan.FromSeconds(5));
                        return ReadUnseenUnsafe();
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
            catch (Exception ex)
            {
                FileLogger.Instance.Warn(
                    "TransferHistoryStore",
                    $"No se pudo leer contador de no vistos ({ex.GetType().Name}: {ex.Message}).");
                return 0;
            }
        }

        private static int ReadUnseenUnsafe()
        {
            if (!File.Exists(PathAndAclHelper.UnseenReceivedFilePath))
                return 0;

            using (var fs = new FileStream(PathAndAclHelper.UnseenReceivedFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
            {
                string raw = sr.ReadToEnd().Trim();
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val) && val > 0)
                    return val;
            }

            return 0;
        }

        public static void MarkReceivedAsSeen()
        {
            bool changed = false;
            try
            {
                bool createdNew;
                using (var mutex = new Mutex(false, UnseenMutex, out createdNew))
                {
                    bool acquired = false;
                    try
                    {
                        acquired = mutex.WaitOne(TimeSpan.FromSeconds(5));
                        if (ReadUnseenUnsafe() > 0)
                        {
                            PathAndAclHelper.EnsureBaseAndLogsFolders();
                            string tempPath = PathAndAclHelper.UnseenReceivedFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                            File.WriteAllText(tempPath, "0", Encoding.UTF8);

                            if (File.Exists(PathAndAclHelper.UnseenReceivedFilePath))
                                File.Replace(tempPath, PathAndAclHelper.UnseenReceivedFilePath, null, true);
                            else
                                File.Move(tempPath, PathAndAclHelper.UnseenReceivedFilePath);

                            changed = true;
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
            catch (Exception ex)
            {
                FileLogger.Instance.Warn(
                    "TransferHistoryStore",
                    $"No se pudo marcar recibidos como vistos ({ex.GetType().Name}: {ex.Message}).");
            }

            if (changed)
                RaiseUnseenChanged();
        }

        public static List<TransferHistoryEntry> LoadEntries(string directionFilter = null, string searchText = null)
        {
            var list = new List<TransferHistoryEntry>();

            try
            {
                if (!File.Exists(PathAndAclHelper.HistoryFilePath))
                    return list;

                string content = AtomicFileStore.ReadAllTextShared(
                    PathAndAclHelper.HistoryFilePath,
                    Encoding.UTF8,
                    HistoryMutex);

                string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string line in lines)
                {
                    string[] cols = line.Split('\t');
                    if (cols.Length < 6)
                        continue;

                    DateTime dt;
                    if (!DateTime.TryParseExact(
                        cols[0],
                        "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out dt))
                    {
                        DateTime.TryParse(cols[0], out dt);
                    }

                    long bytes;
                    long.TryParse(cols[3], NumberStyles.Any, CultureInfo.InvariantCulture, out bytes);

                    string col7 = cols.Length > 7 ? cols[7].Trim() : string.Empty;
                    string col8 = cols.Length > 8 ? cols[8].Trim() : string.Empty;
                    bool hasShaColumn = cols.Length >= 9 || IsHexSha256(col7);

                    var entry = new TransferHistoryEntry
                    {
                        TimestampLocal = dt == DateTime.MinValue ? DateTime.Now : dt,
                        Direction = cols[1].Trim(),
                        ItemName = cols[2].Trim(),
                        SizeBytes = bytes,
                        Counterparty = cols[4].Trim(),
                        Status = cols[5].Trim(),
                        TargetPath = cols.Length > 6 ? cols[6].Trim() : string.Empty,
                        Sha256 = hasShaColumn ? col7 : string.Empty,
                        Notes = hasShaColumn ? col8 : col7
                    };

                    list.Add(entry);
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("TransferHistoryStore", "Error al cargar historial.", ex);
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
                    (x.Sha256 ?? string.Empty).ToLowerInvariant().Contains(q) ||
                    (x.Notes ?? string.Empty).ToLowerInvariant().Contains(q));
            }

            return query.ToList();
        }

        public static void ClearAll()
        {
            if (!PathAndAclHelper.IsCurrentProcessElevated())
                throw new UnauthorizedAccessException("Borrar el historial local requiere privilegios de administrador.");

            try
            {
                AtomicFileStore.WriteAllTextAtomic(PathAndAclHelper.HistoryFilePath, string.Empty, Encoding.UTF8, HistoryMutex);
                AtomicFileStore.WriteAllTextAtomic(PathAndAclHelper.UnseenReceivedFilePath, "0", Encoding.UTF8, UnseenMutex);
                FileLogger.Instance.Info("TransferHistoryStore", "Historial local limpiado por administrador.");
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("TransferHistoryStore", "Error al limpiar historial.", ex);
            }

            RaiseUnseenChanged();
        }

        private static void RaiseUnseenChanged()
        {
            Action handler = UnseenReceivedCountChanged;
            if (handler == null)
                return;

            foreach (Action single in handler.GetInvocationList())
            {
                try { single(); }
                catch (Exception ex)
                {
                    FileLogger.Instance.Warn(
                        "TransferHistoryStore",
                        $"Error en suscriptor de UnseenReceivedCountChanged ({ex.GetType().Name}: {ex.Message}).");
                }
            }
        }

        private static bool IsHexSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isHex = (c >= '0' && c <= '9') ||
                             (c >= 'a' && c <= 'f') ||
                             (c >= 'A' && c <= 'F');
                if (!isHex)
                    return false;
            }

            return true;
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
