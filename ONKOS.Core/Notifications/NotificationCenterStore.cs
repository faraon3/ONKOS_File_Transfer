using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ONKOS.Core.Logging;
using ONKOS.Core.Storage;

namespace ONKOS.Core.Notifications
{
    public enum NotificationCategory
    {
        Sent,
        Received,
        Error,
        AuthorizationRequest,
        SecurityChange,
        ServiceStatus
    }

    public sealed class NotificationPreferences
    {
        public int ToastDurationSeconds { get; set; } = 6;
        public bool EnableSent { get; set; } = true;
        public bool EnableReceived { get; set; } = true;
        public bool EnableErrors { get; set; } = true;
        public bool EnableAuthorization { get; set; } = true;
        public bool EnableSecurity { get; set; } = true;
        public bool EnableServiceStatus { get; set; } = true;
    }

    public sealed class NotificationRecord
    {
        public string Id { get; set; }
        public DateTime TimestampLocal { get; set; }
        public NotificationCategory Category { get; set; }
        public string GroupKey { get; set; }
        public int ItemCount { get; set; }
        public string Title { get; set; }
        public string SenderOrTarget { get; set; }
        public string Detail { get; set; }
        public bool IsRead { get; set; }
    }

    public static class NotificationCenterStore
    {
        private const string NotifMutex = @"Global\ONKOS_Notifications_Lock";
        private const string PrefsMutex = @"Global\ONKOS_NotificationPrefs_Lock";
        private static readonly TimeSpan GroupingWindow = TimeSpan.FromSeconds(12);

        private static readonly string NotificationsFilePath = Path.Combine(
            PathAndAclHelper.BaseDataFolder,
            "notificaciones.tsv");

        private static readonly string PrefsFilePath = Path.Combine(
            PathAndAclHelper.BaseDataFolder,
            "preferencias_notificaciones.txt");

        public static NotificationPreferences LoadPreferences()
        {
            var prefs = new NotificationPreferences();
            try
            {
                if (!File.Exists(PrefsFilePath))
                    return prefs;

                string raw = AtomicFileStore.ReadAllTextShared(PrefsFilePath, Encoding.UTF8, PrefsMutex);
                string[] lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string key = line.Substring(0, eq).Trim().ToUpperInvariant();
                    string val = line.Substring(eq + 1).Trim();

                    if (key == "DURATION_SEC" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sec))
                        prefs.ToastDurationSeconds = Math.Max(2, Math.Min(30, sec));
                    else if (key == "ENABLE_SENT" && bool.TryParse(val, out bool b1))
                        prefs.EnableSent = b1;
                    else if (key == "ENABLE_RECEIVED" && bool.TryParse(val, out bool b2))
                        prefs.EnableReceived = b2;
                    else if (key == "ENABLE_ERRORS" && bool.TryParse(val, out bool b3))
                        prefs.EnableErrors = b3;
                    else if (key == "ENABLE_AUTH" && bool.TryParse(val, out bool b4))
                        prefs.EnableAuthorization = b4;
                    else if (key == "ENABLE_SECURITY" && bool.TryParse(val, out bool b5))
                        prefs.EnableSecurity = b5;
                    else if (key == "ENABLE_SERVICE" && bool.TryParse(val, out bool b6))
                        prefs.EnableServiceStatus = b6;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("NotificationPrefs", $"No se pudieron leer preferencias ({ex.Message}).");
            }

            return prefs;
        }

        public static void SavePreferences(NotificationPreferences prefs)
        {
            if (prefs == null)
                throw new ArgumentNullException(nameof(prefs));

            PathAndAclHelper.EnsureBaseAndLogsFolders();
            int sec = Math.Max(2, Math.Min(30, prefs.ToastDurationSeconds));
            var lines = new[]
            {
                "DURATION_SEC=" + sec.ToString(CultureInfo.InvariantCulture),
                "ENABLE_SENT=" + (prefs.EnableSent ? "true" : "false"),
                "ENABLE_RECEIVED=" + (prefs.EnableReceived ? "true" : "false"),
                "ENABLE_ERRORS=" + (prefs.EnableErrors ? "true" : "false"),
                "ENABLE_AUTH=" + (prefs.EnableAuthorization ? "true" : "false"),
                "ENABLE_SECURITY=" + (prefs.EnableSecurity ? "true" : "false"),
                "ENABLE_SERVICE=" + (prefs.EnableServiceStatus ? "true" : "false")
            };

            AtomicFileStore.WriteAllTextAtomic(
                PrefsFilePath,
                string.Join(Environment.NewLine, lines) + Environment.NewLine,
                Encoding.UTF8,
                PrefsMutex);
        }

        public static bool IsCategoryEnabled(NotificationPreferences prefs, NotificationCategory category)
        {
            if (prefs == null)
                return true;

            switch (category)
            {
                case NotificationCategory.Sent:
                    return prefs.EnableSent;
                case NotificationCategory.Received:
                    return prefs.EnableReceived;
                case NotificationCategory.Error:
                    return prefs.EnableErrors;
                case NotificationCategory.AuthorizationRequest:
                    return prefs.EnableAuthorization;
                case NotificationCategory.SecurityChange:
                    return prefs.EnableSecurity;
                case NotificationCategory.ServiceStatus:
                    return prefs.EnableServiceStatus;
                default:
                    return true;
            }
        }

        public static NotificationRecord PublishGrouped(
            NotificationCategory category,
            string groupKey,
            string title,
            string senderOrTarget,
            string detail)
        {
            NotificationPreferences prefs = LoadPreferences();
            if (!IsCategoryEnabled(prefs, category))
                return null;

            List<NotificationRecord> all = LoadAllInternal();
            DateTime now = DateTime.Now;
            string cleanGroup = Sanitize(groupKey);

            NotificationRecord existingGroup = null;
            if (!string.IsNullOrWhiteSpace(cleanGroup))
            {
                existingGroup = all.LastOrDefault(n =>
                    !n.IsRead &&
                    n.Category == category &&
                    string.Equals(n.GroupKey, cleanGroup, StringComparison.OrdinalIgnoreCase) &&
                    (now - n.TimestampLocal) <= GroupingWindow);
            }

            if (existingGroup != null)
            {
                existingGroup.ItemCount = Math.Max(1, existingGroup.ItemCount) + 1;
                existingGroup.TimestampLocal = now;
                existingGroup.Title = $"{Sanitize(title)} ({existingGroup.ItemCount} elementos)";
                if (!string.IsNullOrWhiteSpace(detail))
                    existingGroup.Detail = Sanitize(detail);

                SaveAllInternal(all);
                return existingGroup;
            }

            var created = new NotificationRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                TimestampLocal = now,
                Category = category,
                GroupKey = cleanGroup,
                ItemCount = 1,
                Title = Sanitize(title),
                SenderOrTarget = Sanitize(senderOrTarget),
                Detail = Sanitize(detail),
                IsRead = false
            };

            all.Add(created);
            if (all.Count > 2000)
                all = all.Skip(all.Count - 2000).ToList();

            SaveAllInternal(all);
            return created;
        }

        public static List<NotificationRecord> GetAll(bool onlyUnread = false)
        {
            List<NotificationRecord> all = LoadAllInternal();
            IEnumerable<NotificationRecord> query = all.AsEnumerable().Reverse();
            if (onlyUnread)
                query = query.Where(n => !n.IsRead);

            return query.ToList();
        }

        public static int GetUnreadCount()
        {
            return LoadAllInternal().Count(n => !n.IsRead);
        }

        public static void MarkAllAsRead()
        {
            List<NotificationRecord> all = LoadAllInternal();
            bool changed = false;
            foreach (NotificationRecord n in all)
            {
                if (!n.IsRead)
                {
                    n.IsRead = true;
                    changed = true;
                }
            }

            if (changed)
                SaveAllInternal(all);
        }

        private static List<NotificationRecord> LoadAllInternal()
        {
            var list = new List<NotificationRecord>();
            try
            {
                if (!File.Exists(NotificationsFilePath))
                    return list;

                string raw = AtomicFileStore.ReadAllTextShared(NotificationsFilePath, Encoding.UTF8, NotifMutex);
                string[] lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    string[] cols = line.Split('\t');
                    if (cols.Length < 8)
                        continue;

                    DateTime dt;
                    if (!DateTime.TryParseExact(cols[1], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                        DateTime.TryParse(cols[1], out dt);

                    NotificationCategory cat;
                    if (!Enum.TryParse(cols[2].Trim(), ignoreCase: true, out cat))
                        cat = NotificationCategory.Received;

                    int count = 1;
                    int.TryParse(cols[4].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count);

                    bool isRead = false;
                    if (cols.Length > 8)
                        bool.TryParse(cols[8].Trim(), out isRead);

                    list.Add(new NotificationRecord
                    {
                        Id = cols[0].Trim(),
                        TimestampLocal = dt == DateTime.MinValue ? DateTime.Now : dt,
                        Category = cat,
                        GroupKey = cols[3].Trim(),
                        ItemCount = Math.Max(1, count),
                        Title = cols[5].Trim(),
                        SenderOrTarget = cols[6].Trim(),
                        Detail = cols[7].Trim(),
                        IsRead = isRead
                    });
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("NotificationCenter", $"Error cargando notificaciones ({ex.Message}).");
            }

            return list;
        }

        private static void SaveAllInternal(List<NotificationRecord> items)
        {
            PathAndAclHelper.EnsureBaseAndLogsFolders();
            var sb = new StringBuilder();
            foreach (NotificationRecord n in items)
            {
                sb.AppendLine(string.Join("\t",
                    Sanitize(n.Id),
                    n.TimestampLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    n.Category.ToString(),
                    Sanitize(n.GroupKey),
                    Math.Max(1, n.ItemCount).ToString(CultureInfo.InvariantCulture),
                    Sanitize(n.Title),
                    Sanitize(n.SenderOrTarget),
                    Sanitize(n.Detail),
                    n.IsRead ? "true" : "false"));
            }

            AtomicFileStore.WriteAllTextAtomic(NotificationsFilePath, sb.ToString(), Encoding.UTF8, NotifMutex);
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
