using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;
using ONKOS.Core.Security;

namespace ONKOS.Core.Storage
{
    public sealed class AddressBookEntry
    {
        public string FingerprintSha256 { get; set; }
        public string FullName { get; set; }
        public string Area { get; set; }
        public string GroupName { get; set; }
        public string IPv4 { get; set; }
        public string MachineName { get; set; }
        public bool IsFavorite { get; set; }
    }

    public static class AddressBookStore
    {
        private const string BookMutex = @"Global\ONKOS_AddressBook_Lock";
        private static readonly string BookFilePath = Path.Combine(
            PathAndAclHelper.BaseDataFolder,
            "libreta_contactos.tsv");

        public static List<AddressBookEntry> LoadAll()
        {
            var result = new List<AddressBookEntry>();
            try
            {
                if (!File.Exists(BookFilePath))
                    return result;

                string raw = AtomicFileStore.ReadAllTextShared(BookFilePath, Encoding.UTF8, BookMutex);
                string[] lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    string[] cols = line.Split('\t');
                    if (cols.Length < 4)
                        continue;

                    bool fav = false;
                    if (cols.Length > 6)
                        bool.TryParse(cols[6].Trim(), out fav);

                    result.Add(new AddressBookEntry
                    {
                        FingerprintSha256 = PeerAuthorizationStore.NormalizeFingerprint(cols[0]),
                        FullName = UserProfileStore.SanitizeField(cols[1]),
                        Area = UserProfileStore.SanitizeField(cols[2]),
                        GroupName = UserProfileStore.SanitizeField(cols[3]),
                        IPv4 = cols.Length > 4 ? cols[4].Trim() : string.Empty,
                        MachineName = cols.Length > 5 ? UserProfileStore.SanitizeField(cols[5]) : string.Empty,
                        IsFavorite = fav
                    });
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("AddressBookStore", $"Error cargando libreta ({ex.Message}).");
            }

            return result;
        }

        public static void UpsertContact(PeerInfo peer, string groupName = null, bool? isFavorite = null)
        {
            if (peer == null || string.IsNullOrWhiteSpace(peer.IPv4))
                return;

            List<AddressBookEntry> all = LoadAll();
            string fp = PeerAuthorizationStore.NormalizeFingerprint(peer.FingerprintSha256);

            AddressBookEntry existing = all.FirstOrDefault(x =>
                (!string.IsNullOrEmpty(fp) && string.Equals(x.FingerprintSha256, fp, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(x.IPv4, peer.IPv4.Trim(), StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                existing = new AddressBookEntry();
                all.Add(existing);
            }

            if (!string.IsNullOrEmpty(fp))
                existing.FingerprintSha256 = fp;
            if (!string.IsNullOrWhiteSpace(peer.FullName))
                existing.FullName = UserProfileStore.SanitizeField(peer.FullName);
            if (!string.IsNullOrWhiteSpace(peer.Area))
            {
                existing.Area = UserProfileStore.SanitizeField(peer.Area);
                if (string.IsNullOrWhiteSpace(existing.GroupName))
                    existing.GroupName = existing.Area;
            }
            if (groupName != null)
                existing.GroupName = UserProfileStore.SanitizeField(groupName);
            existing.IPv4 = peer.IPv4.Trim();
            if (!string.IsNullOrWhiteSpace(peer.MachineName))
                existing.MachineName = UserProfileStore.SanitizeField(peer.MachineName);
            if (isFavorite.HasValue)
                existing.IsFavorite = isFavorite.Value;

            SaveAll(all);
        }

        public static List<string> GetGroupsAndAreas(IEnumerable<PeerInfo> discoveredPeers)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string a in UserProfileStore.GetConfiguredAreas())
            {
                if (!string.IsNullOrWhiteSpace(a))
                    set.Add(a);
            }

            foreach (AddressBookEntry entry in LoadAll())
            {
                if (!string.IsNullOrWhiteSpace(entry.GroupName))
                    set.Add(entry.GroupName);
                if (!string.IsNullOrWhiteSpace(entry.Area))
                    set.Add(entry.Area);
            }

            if (discoveredPeers != null)
            {
                foreach (PeerInfo p in discoveredPeers)
                {
                    if (!string.IsNullOrWhiteSpace(p.Area))
                        set.Add(p.Area);
                }
            }

            return set.OrderBy(x => x).ToList();
        }

        private static void SaveAll(List<AddressBookEntry> items)
        {
            PathAndAclHelper.EnsureBaseAndLogsFolders();
            var sb = new StringBuilder();
            foreach (AddressBookEntry e in items)
            {
                sb.AppendLine(string.Join("\t",
                    e.FingerprintSha256 ?? string.Empty,
                    e.FullName ?? string.Empty,
                    e.Area ?? string.Empty,
                    e.GroupName ?? string.Empty,
                    e.IPv4 ?? string.Empty,
                    e.MachineName ?? string.Empty,
                    e.IsFavorite ? "true" : "false"));
            }

            AtomicFileStore.WriteAllTextAtomic(BookFilePath, sb.ToString(), Encoding.UTF8, BookMutex);
        }
    }
}
