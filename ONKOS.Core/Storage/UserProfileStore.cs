using System;
using System.IO;
using System.Text;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;

namespace ONKOS.Core.Storage
{
    public static class UserProfileStore
    {
        private const string ProfileMutex = @"Global\ONKOS_Profile_Lock";

        public static bool IsIgnoredSystemName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;

            string n = name.Trim().ToUpperInvariant();
            return n == "SYSTEM" ||
                   n == "NT AUTHORITY\\SYSTEM" ||
                   n == "LOCAL SERVICE" ||
                   n == "NETWORK SERVICE";
        }

        public static bool TryLoad(out PeerInfo profile)
        {
            profile = null;
            try
            {
                if (!File.Exists(PathAndAclHelper.ProfileFilePath))
                    return false;

                string raw = AtomicFileStore.ReadAllTextShared(
                    PathAndAclHelper.ProfileFilePath,
                    Encoding.UTF8,
                    ProfileMutex);

                string[] lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 2)
                    return false;

                string name = SanitizeField(lines[0]);
                string area = SanitizeField(lines[1]);
                if (IsIgnoredSystemName(name) || string.IsNullOrWhiteSpace(area))
                    return false;

                profile = new PeerInfo
                {
                    FullName = name,
                    Area = area,
                    MachineName = Environment.MachineName
                };

                return true;
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn(
                    "UserProfileStore",
                    $"Error al cargar perfil local ({ex.GetType().Name}: {ex.Message}).");
                return false;
            }
        }

        public static void Save(string fullName, string area)
        {
            string cleanName = SanitizeField(fullName);
            string cleanArea = SanitizeField(area);

            if (string.IsNullOrWhiteSpace(cleanName) || IsIgnoredSystemName(cleanName))
                throw new ArgumentException("Nombre de usuario no válido.", nameof(fullName));

            if (string.IsNullOrWhiteSpace(cleanArea))
                throw new ArgumentException("Área o departamento no válido.", nameof(area));

            string content = cleanName + Environment.NewLine + cleanArea + Environment.NewLine;
            AtomicFileStore.WriteAllTextAtomic(
                PathAndAclHelper.ProfileFilePath,
                content,
                Encoding.UTF8,
                ProfileMutex);

            FileLogger.Instance.Info("UserProfileStore", $"Perfil guardado para {cleanName} ({cleanArea}).");
        }

        public static string SanitizeField(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
