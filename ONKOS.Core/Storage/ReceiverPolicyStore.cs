using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ONKOS.Core.Logging;
using ONKOS.Core.Security;

namespace ONKOS.Core.Storage
{
    public sealed class ReceiverPolicy
    {
        public bool AutoAcceptAuthorized { get; set; } = true;
        public List<string> BlockedFingerprints { get; } = new List<string>();
        public List<string> BlockedIps { get; } = new List<string>();
    }

    public static class ReceiverPolicyStore
    {
        private const string PolicyMutex = @"Global\ONKOS_ReceiverPolicy_Lock";
        private static readonly string PolicyFilePath = Path.Combine(
            PathAndAclHelper.BaseDataFolder,
            "politica_receptor.txt");

        public static ReceiverPolicy Load()
        {
            var policy = new ReceiverPolicy();
            try
            {
                if (!File.Exists(PolicyFilePath))
                    return policy;

                string raw = AtomicFileStore.ReadAllTextShared(PolicyFilePath, Encoding.UTF8, PolicyMutex);
                string[] lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("AUTO_ACCEPT=", StringComparison.OrdinalIgnoreCase))
                    {
                        bool val;
                        if (bool.TryParse(trimmed.Substring("AUTO_ACCEPT=".Length).Trim(), out val))
                            policy.AutoAcceptAuthorized = val;
                    }
                    else if (trimmed.StartsWith("BLOCK_FP=", StringComparison.OrdinalIgnoreCase))
                    {
                        string fp = PeerAuthorizationStore.NormalizeFingerprint(trimmed.Substring("BLOCK_FP=".Length));
                        if (!string.IsNullOrEmpty(fp) && !policy.BlockedFingerprints.Contains(fp))
                            policy.BlockedFingerprints.Add(fp);
                    }
                    else if (trimmed.StartsWith("BLOCK_IP=", StringComparison.OrdinalIgnoreCase))
                    {
                        string ip = trimmed.Substring("BLOCK_IP=".Length).Trim();
                        if (!string.IsNullOrEmpty(ip) && !policy.BlockedIps.Contains(ip))
                            policy.BlockedIps.Add(ip);
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("ReceiverPolicyStore", $"Error cargando política del receptor ({ex.Message}).");
            }

            return policy;
        }

        public static void Save(ReceiverPolicy policy)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));

            PathAndAclHelper.EnsureBaseAndLogsFolders();
            var lines = new List<string>
            {
                "AUTO_ACCEPT=" + (policy.AutoAcceptAuthorized ? "true" : "false")
            };

            foreach (string fp in policy.BlockedFingerprints.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string cleanFp = PeerAuthorizationStore.NormalizeFingerprint(fp);
                if (!string.IsNullOrEmpty(cleanFp))
                    lines.Add("BLOCK_FP=" + cleanFp);
            }

            foreach (string ip in policy.BlockedIps.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string cleanIp = (ip ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(cleanIp))
                    lines.Add("BLOCK_IP=" + cleanIp);
            }

            AtomicFileStore.WriteAllTextAtomic(
                PolicyFilePath,
                string.Join(Environment.NewLine, lines) + Environment.NewLine,
                Encoding.UTF8,
                PolicyMutex);
        }

        public static bool IsBlocked(string fingerprint, string remoteIp, out string reason)
        {
            reason = string.Empty;
            ReceiverPolicy p = Load();
            string cleanFp = PeerAuthorizationStore.NormalizeFingerprint(fingerprint);

            if (!string.IsNullOrEmpty(cleanFp) &&
                p.BlockedFingerprints.Any(x => string.Equals(x, cleanFp, StringComparison.OrdinalIgnoreCase)))
            {
                reason = "El remitente está bloqueado por huella en este equipo.";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(remoteIp) &&
                p.BlockedIps.Any(x => string.Equals(x.Trim(), remoteIp.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                reason = "La IP del remitente está bloqueada en este equipo.";
                return true;
            }

            return false;
        }
    }
}
