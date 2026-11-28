using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using ONKOS.Core.Logging;

namespace ONKOS.Core.Storage
{
    public static class PathAndAclHelper
    {
        public static string BaseDataFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ONKOS");

        public static string LogsFolder => Path.Combine(BaseDataFolder, "logs");

        public static string SecurityFolder => Path.Combine(BaseDataFolder, "security");

        public static string DefaultInboxFolder => Path.Combine(BaseDataFolder, "Inbox");

        public static string SettingsFilePath => Path.Combine(BaseDataFolder, "ruta_recibidos.txt");

        public static string ProfileFilePath => Path.Combine(BaseDataFolder, "perfil.txt");

        public static string ContactsFilePath => Path.Combine(BaseDataFolder, "contactos.txt");

        public static string HistoryFilePath => Path.Combine(BaseDataFolder, "historial_transferencias.tsv");

        public static string UnseenReceivedFilePath => Path.Combine(BaseDataFolder, "recibidos_no_vistos.txt");

        public static string PeerAuthFilePath => Path.Combine(SecurityFolder, "equipos_autorizados.tsv");

        public static string IdentityPfxFilePath => Path.Combine(SecurityFolder, "identidad_equipo.pfx");

        public static string IdentitySecretFilePath => Path.Combine(SecurityFolder, "identidad_llave.bin");

        public static void EnsureBaseAndLogsFolders()
        {
            Directory.CreateDirectory(BaseDataFolder);
            Directory.CreateDirectory(LogsFolder);
            Directory.CreateDirectory(SecurityFolder);
            TryGrantAuthenticatedUsersModify(BaseDataFolder);
            TryGrantAuthenticatedUsersModify(LogsFolder);
            TryRestrictSecurityFolder(SecurityFolder);
        }

        public static void EnsureInboxReady(string inboxPath)
        {
            if (string.IsNullOrWhiteSpace(inboxPath))
                throw new ArgumentException("La carpeta de recibidos no puede estar vacía.", nameof(inboxPath));

            Directory.CreateDirectory(inboxPath);
            TryGrantAuthenticatedUsersModify(inboxPath);
        }

        public static bool HasEnoughFreeSpace(string folderPath, long requiredBytes, out string reason)
        {
            reason = string.Empty;
            try
            {
                string fullPath = Path.GetFullPath(folderPath);
                string root = Path.GetPathRoot(fullPath);
                if (string.IsNullOrWhiteSpace(root))
                {
                    reason = "No se pudo determinar la unidad de destino.";
                    return false;
                }

                var drive = new DriveInfo(root);
                if (!drive.IsReady)
                {
                    reason = $"La unidad {root} no está lista o no está disponible.";
                    return false;
                }

                long bufferBytes = 10L * 1024L * 1024L;
                long needed = Math.Max(0L, requiredBytes) + bufferBytes;
                if (drive.AvailableFreeSpace < needed)
                {
                    reason = $"Espacio insuficiente en {root} (disponible: {drive.AvailableFreeSpace} bytes).";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = "Error al comprobar espacio en disco: " + ex.Message;
                FileLogger.Instance.Warn("Storage", reason);
                return false;
            }
        }

        public static bool IsCurrentProcessElevated()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                if (identity == null)
                    return false;

                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("Security.Acl", $"No se pudo verificar elevación ({ex.Message}).");
                return false;
            }
        }

        public static void TryGrantAuthenticatedUsersModify(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                    return;

                var authUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
                var rule = new FileSystemAccessRule(
                    authUsers,
                    FileSystemRights.Modify,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow);

                DirectorySecurity security = Directory.GetAccessControl(folderPath);
                security.AddAccessRule(rule);
                Directory.SetAccessControl(folderPath, security);
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        public static void TryRestrictSecurityFolder(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                    return;

                var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                var localServiceSid = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);

                DirectorySecurity security = Directory.GetAccessControl(folderPath);
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(systemSid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(localServiceSid, FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                Directory.SetAccessControl(folderPath, security);
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        public static void TryRestrictFileToAdminsAndService(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    return;

                var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                var localServiceSid = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);

                FileSecurity security = File.GetAccessControl(filePath);
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(systemSid, FileSystemRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(localServiceSid, FileSystemRights.Read, AccessControlType.Allow));
                File.SetAccessControl(filePath, security);
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }
    }
}
