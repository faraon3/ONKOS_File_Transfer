using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;
using ONKOS.Core.Security;
using ONKOS.Core.Storage;

namespace ONKOS.Core.Discovery
{
    public static class PeerContactsStore
    {
        private const string ContactsMutex = @"Global\ONKOS_Contacts_Lock";

        public static string BuildContactKey(PeerInfo peer)
        {
            if (peer == null)
                return string.Empty;

            string fp = PeerAuthorizationStore.NormalizeFingerprint(peer.FingerprintSha256);
            if (!string.IsNullOrWhiteSpace(fp))
                return "FP:" + fp;

            string machine = (peer.MachineName ?? string.Empty).Trim().ToUpperInvariant();
            string name = (peer.FullName ?? string.Empty).Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(machine) || !string.IsNullOrWhiteSpace(name))
                return "ID:" + machine + "|" + name;

            return "IP:" + (peer.IPv4 ?? string.Empty).Trim();
        }

        public static List<PeerInfo> LoadContacts()
        {
            var result = new List<PeerInfo>();
            try
            {
                if (!File.Exists(PathAndAclHelper.ContactsFilePath))
                    return result;

                string raw = AtomicFileStore.ReadAllTextShared(
                    PathAndAclHelper.ContactsFilePath,
                    Encoding.UTF8,
                    ContactsMutex);

                string[] lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    string[] cols = line.Split('\t');
                    if (cols.Length < 4)
                        continue;

                    string fp = PeerAuthorizationStore.NormalizeFingerprint(cols[0]);
                    string name = UserProfileStore.SanitizeField(cols[1]);
                    string area = UserProfileStore.SanitizeField(cols[2]);
                    string ip = (cols[3] ?? string.Empty).Trim();
                    string machine = cols.Length > 4 ? UserProfileStore.SanitizeField(cols[4]) : string.Empty;

                    if (UserProfileStore.IsIgnoredSystemName(name) || string.IsNullOrWhiteSpace(name))
                        continue;

                    DateTime lastSeen = DateTime.MinValue;
                    if (cols.Length > 5 && !string.IsNullOrWhiteSpace(cols[5]))
                        DateTime.TryParse(cols[5], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out lastSeen);

                    result.Add(new PeerInfo
                    {
                        FingerprintSha256 = fp,
                        FullName = name,
                        Area = area,
                        IPv4 = ip,
                        MachineName = machine,
                        LastSeenUtc = lastSeen
                    });
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("PeerContactsStore", $"No se pudieron cargar contactos ({ex.Message}).");
            }

            return result;
        }

        public static void SaveContacts(IEnumerable<PeerInfo> peers, int stalePurgeDays = 30)
        {
            if (peers == null)
                return;

            DateTime cutoffUtc = DateTime.UtcNow.AddDays(-Math.Max(1, stalePurgeDays));
            var deduped = new Dictionary<string, PeerInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (PeerInfo p in peers)
            {
                if (p == null || UserProfileStore.IsIgnoredSystemName(p.FullName) || string.IsNullOrWhiteSpace(p.FullName))
                    continue;

                if (p.LastSeenUtc != DateTime.MinValue && p.LastSeenUtc < cutoffUtc)
                    continue;

                string key = BuildContactKey(p);
                if (string.IsNullOrEmpty(key))
                    continue;

                if (!deduped.TryGetValue(key, out PeerInfo current) || p.LastSeenUtc >= current.LastSeenUtc)
                {
                    deduped[key] = new PeerInfo
                    {
                        FingerprintSha256 = PeerAuthorizationStore.NormalizeFingerprint(p.FingerprintSha256),
                        FullName = UserProfileStore.SanitizeField(p.FullName),
                        Area = UserProfileStore.SanitizeField(p.Area),
                        IPv4 = (p.IPv4 ?? string.Empty).Trim(),
                        MachineName = UserProfileStore.SanitizeField(p.MachineName),
                        LastSeenUtc = p.LastSeenUtc
                    };
                }
            }

            var sb = new StringBuilder();
            foreach (PeerInfo p in deduped.Values.OrderBy(x => x.FullName))
            {
                sb.AppendLine(string.Join("\t",
                    p.FingerprintSha256 ?? string.Empty,
                    p.FullName ?? string.Empty,
                    p.Area ?? string.Empty,
                    p.IPv4 ?? string.Empty,
                    p.MachineName ?? string.Empty,
                    p.LastSeenUtc == DateTime.MinValue ? string.Empty : p.LastSeenUtc.ToString("o", CultureInfo.InvariantCulture)));
            }

            AtomicFileStore.WriteAllTextAtomic(
                PathAndAclHelper.ContactsFilePath,
                sb.ToString(),
                Encoding.UTF8,
                ContactsMutex);
        }

        public static bool TryParseManualContact(string input, out string name, out string area, out string ipv4, out string error)
        {
            name = string.Empty;
            area = "Manual";
            ipv4 = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "Ingresa una IPv4 válida o el formato Nombre = IP.";
                return false;
            }

            string trimmed = input.Trim();
            IPAddress parsedIp;

            if (trimmed.Contains("="))
            {
                string[] parts = trimmed.Split(new[] { '=' }, 2);
                name = UserProfileStore.SanitizeField(parts[0]);
                string ipPart = parts[1].Trim();

                if (!IPAddress.TryParse(ipPart, out parsedIp) || parsedIp.AddressFamily != AddressFamily.InterNetwork)
                {
                    error = "La dirección IPv4 indicada no es válida.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(name) || UserProfileStore.IsIgnoredSystemName(name))
                    name = "Equipo " + parsedIp;

                ipv4 = parsedIp.ToString();
                return true;
            }

            if (IPAddress.TryParse(trimmed, out parsedIp) && parsedIp.AddressFamily == AddressFamily.InterNetwork)
            {
                ipv4 = parsedIp.ToString();
                name = "Equipo " + ipv4;
                area = "Red local";
                return true;
            }

            error = "Formato inválido. Usa una IPv4 válida (ej. 192.168.1.25) o 'Nombre = IP'.";
            return false;
        }
    }
}
