using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ONKOS.Core.Logging;
using ONKOS.Core.Storage;

namespace ONKOS.Core.Security
{
    public enum PeerTrustState
    {
        Pending,
        Authorized,
        Blocked,
        Revoked
    }

    public sealed class AuthorizedPeerRecord
    {
        public string FingerprintSha256 { get; set; }
        public string DisplayName { get; set; }
        public string Area { get; set; }
        public string LastIp { get; set; }
        public string MachineName { get; set; }
        public PeerTrustState State { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public string Notes { get; set; }
    }

    public static class PeerAuthorizationStore
    {
        private const string AuthMutex = @"Global\ONKOS_PeerAuth_Lock";

        public static event Action<AuthorizedPeerRecord> PendingRequestReceived;
        public static event Action AuthorizationListChanged;

        public static string NormalizeFingerprint(string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint))
                return string.Empty;

            return fingerprint.Trim().Replace(":", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
        }

        public static PeerTrustState GetTrustState(string fingerprint)
        {
            string cleanFp = NormalizeFingerprint(fingerprint);
            if (string.IsNullOrEmpty(cleanFp))
                return PeerTrustState.Pending;

            List<AuthorizedPeerRecord> all = LoadAll();
            AuthorizedPeerRecord found = all.FirstOrDefault(x =>
                string.Equals(x.FingerprintSha256, cleanFp, StringComparison.OrdinalIgnoreCase));

            return found != null ? found.State : PeerTrustState.Pending;
        }

        public static bool IsAuthorized(string fingerprint)
        {
            return GetTrustState(fingerprint) == PeerTrustState.Authorized;
        }

        public static AuthorizedPeerRecord RegisterOrTouchPending(
            string fingerprint,
            string displayName,
            string area,
            string ip,
            string machineName)
        {
            string cleanFp = NormalizeFingerprint(fingerprint);
            if (string.IsNullOrEmpty(cleanFp))
                throw new ArgumentException("La huella SHA-256 del equipo es obligatoria.", nameof(fingerprint));

            List<AuthorizedPeerRecord> all = LoadAll();
            AuthorizedPeerRecord existing = all.FirstOrDefault(x =>
                string.Equals(x.FingerprintSha256, cleanFp, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(displayName))
                    existing.DisplayName = Sanitize(displayName);
                if (!string.IsNullOrWhiteSpace(area))
                    existing.Area = Sanitize(area);
                if (!string.IsNullOrWhiteSpace(ip))
                    existing.LastIp = Sanitize(ip);
                if (!string.IsNullOrWhiteSpace(machineName))
                    existing.MachineName = Sanitize(machineName);

                existing.UpdatedUtc = DateTime.UtcNow;
                SaveAll(all);
                return existing;
            }

            var record = new AuthorizedPeerRecord
            {
                FingerprintSha256 = cleanFp,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Equipo desconocido" : Sanitize(displayName),
                Area = Sanitize(area),
                LastIp = Sanitize(ip),
                MachineName = Sanitize(machineName),
                State = PeerTrustState.Pending,
                UpdatedUtc = DateTime.UtcNow,
                Notes = "Solicitud pendiente de aprobación por Sistemas"
            };

            all.Add(record);
            SaveAll(all);

            FileLogger.Instance.Info(
                "Security.Auth",
                $"Nueva solicitud de equipo PENDIENTE. Huella: {cleanFp}, Equipo: {record.MachineName}, IP: {record.LastIp}");

            PendingRequestReceived?.Invoke(record);
            AuthorizationListChanged?.Invoke();
            return record;
        }

        public static void SetTrustState(string fingerprint, PeerTrustState newState, string notes = "")
        {
            if (!PathAndAclHelper.IsCurrentProcessElevated())
                throw new UnauthorizedAccessException("Cambiar el estado de autorización de equipos requiere privilegios de administrador.");

            string cleanFp = NormalizeFingerprint(fingerprint);
            if (string.IsNullOrEmpty(cleanFp))
                throw new ArgumentException("Huella SHA-256 inválida.", nameof(fingerprint));

            List<AuthorizedPeerRecord> all = LoadAll();
            AuthorizedPeerRecord existing = all.FirstOrDefault(x =>
                string.Equals(x.FingerprintSha256, cleanFp, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                existing = new AuthorizedPeerRecord
                {
                    FingerprintSha256 = cleanFp,
                    DisplayName = cleanFp.Substring(0, Math.Min(12, cleanFp.Length)),
                    Area = string.Empty,
                    LastIp = string.Empty,
                    MachineName = string.Empty
                };
                all.Add(existing);
            }

            existing.State = newState;
            existing.UpdatedUtc = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(notes))
                existing.Notes = Sanitize(notes);

            SaveAll(all);
            FileLogger.Instance.Info(
                "Security.Auth",
                $"Estado actualizado para huella {cleanFp} -> {newState} ({existing.Notes}).");

            AuthorizationListChanged?.Invoke();
        }

        public static List<AuthorizedPeerRecord> LoadAll()
        {
            var result = new List<AuthorizedPeerRecord>();
            try
            {
                if (!File.Exists(PathAndAclHelper.PeerAuthFilePath))
                    return result;

                string raw = AtomicFileStore.ReadAllTextShared(
                    PathAndAclHelper.PeerAuthFilePath,
                    Encoding.UTF8,
                    AuthMutex);

                string[] lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    string[] cols = line.Split('\t');
                    if (cols.Length < 2)
                        continue;

                    string fp = NormalizeFingerprint(cols[0]);
                    if (string.IsNullOrEmpty(fp))
                        continue;

                    PeerTrustState state;
                    if (!Enum.TryParse(cols[1].Trim(), ignoreCase: true, out state))
                        state = PeerTrustState.Pending;

                    DateTime updated = DateTime.UtcNow;
                    if (cols.Length > 6 && !string.IsNullOrWhiteSpace(cols[6]))
                    {
                        DateTime.TryParse(cols[6], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out updated);
                    }

                    result.Add(new AuthorizedPeerRecord
                    {
                        FingerprintSha256 = fp,
                        State = state,
                        DisplayName = cols.Length > 2 ? cols[2].Trim() : string.Empty,
                        Area = cols.Length > 3 ? cols[3].Trim() : string.Empty,
                        LastIp = cols.Length > 4 ? cols[4].Trim() : string.Empty,
                        MachineName = cols.Length > 5 ? cols[5].Trim() : string.Empty,
                        UpdatedUtc = updated,
                        Notes = cols.Length > 7 ? cols[7].Trim() : string.Empty
                    });
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("Security.Auth", "Error cargando lista de equipos autorizados.", ex);
            }

            return result;
        }

        private static void SaveAll(List<AuthorizedPeerRecord> records)
        {
            PathAndAclHelper.EnsureBaseAndLogsFolders();
            var sb = new StringBuilder();

            foreach (AuthorizedPeerRecord r in records.OrderBy(x => x.DisplayName))
            {
                sb.AppendLine(string.Join("\t",
                    NormalizeFingerprint(r.FingerprintSha256),
                    r.State.ToString(),
                    Sanitize(r.DisplayName),
                    Sanitize(r.Area),
                    Sanitize(r.LastIp),
                    Sanitize(r.MachineName),
                    r.UpdatedUtc.ToString("o", CultureInfo.InvariantCulture),
                    Sanitize(r.Notes)));
            }

            AtomicFileStore.WriteAllTextAtomic(
                PathAndAclHelper.PeerAuthFilePath,
                sb.ToString(),
                Encoding.UTF8,
                AuthMutex);

            PathAndAclHelper.TryRestrictFileToAdminsAndService(PathAndAclHelper.PeerAuthFilePath);
        }

        private static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return input.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
