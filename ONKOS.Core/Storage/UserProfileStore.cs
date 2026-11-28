using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;

namespace ONKOS.Core.Storage
{
    public static class UserProfileStore
    {
        public const int MaxFieldLength = 80;
        private const string ProfileMutex = @"Global\ONKOS_Profile_Lock";
        private const string AreasMutex = @"Global\ONKOS_Areas_Lock";

        private static readonly string AreasFilePath = Path.Combine(
            PathAndAclHelper.BaseDataFolder,
            "areas.txt");

        private static readonly string[] DefaultAreas = new[]
        {
            "Sistemas / TI",
            "Facturación",
            "Admisiones",
            "Enfermería",
            "Consulta Externa",
            "Laboratorio",
            "Farmacia",
            "Dirección / Gerencia"
        };

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

        public static List<string> GetConfiguredAreas()
        {
            try
            {
                PathAndAclHelper.EnsureBaseAndLogsFolders();
                if (!File.Exists(AreasFilePath))
                {
                    string initial = string.Join(Environment.NewLine, DefaultAreas) + Environment.NewLine;
                    AtomicFileStore.WriteAllTextAtomic(AreasFilePath, initial, Encoding.UTF8, AreasMutex);
                    return DefaultAreas.ToList();
                }

                string raw = AtomicFileStore.ReadAllTextShared(AreasFilePath, Encoding.UTF8, AreasMutex);
                var list = raw
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(SanitizeField)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return list.Count > 0 ? list : DefaultAreas.ToList();
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("UserProfileStore", $"Error cargando áreas configuradas ({ex.Message}).");
                return DefaultAreas.ToList();
            }
        }

        public static void AddConfiguredArea(string areaName)
        {
            string clean = SanitizeField(areaName);
            if (string.IsNullOrWhiteSpace(clean))
                return;

            List<string> areas = GetConfiguredAreas();
            if (!areas.Any(a => string.Equals(a, clean, StringComparison.OrdinalIgnoreCase)))
            {
                areas.Add(clean);
                string content = string.Join(Environment.NewLine, areas) + Environment.NewLine;
                AtomicFileStore.WriteAllTextAtomic(AreasFilePath, content, Encoding.UTF8, AreasMutex);
            }
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

            AddConfiguredArea(cleanArea);

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

            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (c == '|' || c == '\t' || c == '\r' || c == '\n' || char.IsControl(c))
                    continue;
                sb.Append(c);
            }

            string cleaned = sb.ToString().Trim();
            if (cleaned.Length > MaxFieldLength)
                cleaned = cleaned.Substring(0, MaxFieldLength).Trim();

            return cleaned;
        }
    }
}
