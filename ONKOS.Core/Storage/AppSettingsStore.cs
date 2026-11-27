using System;
using System.IO;
using System.Text;
using ONKOS.Core.Logging;

namespace ONKOS.Core.Storage
{
    public static class AppSettingsStore
    {
        private const string SettingsMutex = @"Global\ONKOS_Settings_Lock";

        public static string GetDefaultInboxFolder()
        {
            return PathAndAclHelper.DefaultInboxFolder;
        }

        public static string GetInboxFolder()
        {
            try
            {
                PathAndAclHelper.EnsureBaseAndLogsFolders();

                if (File.Exists(PathAndAclHelper.SettingsFilePath))
                {
                    string saved = AtomicFileStore.ReadAllTextShared(
                        PathAndAclHelper.SettingsFilePath,
                        Encoding.UTF8,
                        SettingsMutex).Trim();

                    if (!string.IsNullOrWhiteSpace(saved))
                        return saved;
                }

                string legacyDesktopInbox = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "Archivos Recibidos ONKOS");

                string chosen = Directory.Exists(legacyDesktopInbox)
                    ? legacyDesktopInbox
                    : PathAndAclHelper.DefaultInboxFolder;

                AtomicFileStore.WriteAllTextAtomic(
                    PathAndAclHelper.SettingsFilePath,
                    chosen,
                    Encoding.UTF8,
                    SettingsMutex);

                return chosen;
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("AppSettingsStore", "No se pudo leer ruta_recibidos.txt; usando ruta por defecto.", ex);
                return PathAndAclHelper.DefaultInboxFolder;
            }
        }

        public static void SetInboxFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("La carpeta de recibidos no puede estar vacía.", nameof(folderPath));

            string cleanPath = Path.GetFullPath(folderPath.Trim());
            PathAndAclHelper.EnsureInboxReady(cleanPath);

            string spaceReason;
            if (!PathAndAclHelper.HasEnoughFreeSpace(cleanPath, 0L, out spaceReason))
                throw new IOException(spaceReason);

            AtomicFileStore.WriteAllTextAtomic(
                PathAndAclHelper.SettingsFilePath,
                cleanPath,
                Encoding.UTF8,
                SettingsMutex);

            FileLogger.Instance.Info("AppSettingsStore", "Carpeta de recibidos actualizada: " + cleanPath);
        }

        public static bool TryEnsureInboxReady(out string activeFolder, out string errorMessage)
        {
            activeFolder = PathAndAclHelper.DefaultInboxFolder;
            errorMessage = string.Empty;

            try
            {
                activeFolder = GetInboxFolder();
                PathAndAclHelper.EnsureInboxReady(activeFolder);

                string spaceReason;
                if (!PathAndAclHelper.HasEnoughFreeSpace(activeFolder, 0L, out spaceReason))
                {
                    errorMessage = spaceReason;
                    FileLogger.Instance.Error("AppSettingsStore", errorMessage);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = "Error preparando carpeta de recibidos: " + ex.Message;
                FileLogger.Instance.Error("AppSettingsStore", errorMessage, ex);
                return false;
            }
        }
    }
}
