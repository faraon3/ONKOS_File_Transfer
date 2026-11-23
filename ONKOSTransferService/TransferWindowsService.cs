using System;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using ONKOSFileTransfer.Network;

namespace ONKOSTransferService
{
    public partial class TransferWindowsService : ServiceBase
    {
        private const int DefaultListenPort = 5050;

        private FileReceiver _receiver;
        private ManualResetEventSlim _stopEvent;

        public TransferWindowsService()
        {
            ServiceName = "ONKOSTransferService";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool GetUserProfileDirectory(IntPtr hToken, StringBuilder lpProfileDir, ref uint lpcchSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static string GetActiveUserDesktopInbox()
        {
            IntPtr userToken = IntPtr.Zero;
            try
            {
                uint sessionId = WTSGetActiveConsoleSessionId();
                if (sessionId != 0xFFFFFFFF && WTSQueryUserToken(sessionId, out userToken) && userToken != IntPtr.Zero)
                {
                    uint capacity = 260;
                    var profileDir = new StringBuilder((int)capacity);
                    if (!GetUserProfileDirectory(userToken, profileDir, ref capacity))
                    {
                        profileDir = new StringBuilder((int)capacity);
                        if (GetUserProfileDirectory(userToken, profileDir, ref capacity))
                        {
                            return Path.Combine(profileDir.ToString(), "Desktop", "Archivos Recibidos ONKOS");
                        }
                    }
                    else
                    {
                        return Path.Combine(profileDir.ToString(), "Desktop", "Archivos Recibidos ONKOS");
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (userToken != IntPtr.Zero)
                {
                    try { CloseHandle(userToken); } catch { }
                }
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Archivos Recibidos ONKOS");
        }

        private static string GetTargetInboxFolder()
        {
            try
            {
                string settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ONKOS",
                    "ruta_recibidos.txt");

                if (File.Exists(settingsPath))
                {
                    string saved = File.ReadAllText(settingsPath, Encoding.UTF8).Trim();
                    if (!string.IsNullOrWhiteSpace(saved))
                        return saved;
                }
            }
            catch
            {
            }

            return GetActiveUserDesktopInbox();
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                string inboxFolder = GetTargetInboxFolder();
                Directory.CreateDirectory(inboxFolder);

                _stopEvent = new ManualResetEventSlim(false);

                _receiver = new FileReceiver();
                _receiver.FileReceived += OnFileReceived;
                _receiver.Error += OnReceiverError;
                _receiver.Start(DefaultListenPort, inboxFolder);

                ServiceNotifier.SendInfo(
                    "Servicio iniciado",
                    $"Escuchando en el puerto {DefaultListenPort}."
                );
            }
            catch (Exception ex)
            {
                ServiceNotifier.SendError("Error al iniciar servicio", ex.Message);
                throw;
            }
        }

        private void OnFileReceived(string savedPath)
        {
            string itemName = Path.GetFileName(savedPath);
            if (string.IsNullOrWhiteSpace(itemName))
                itemName = "Elemento recibido";

            string folderToOpen = Directory.Exists(savedPath)
                ? savedPath
                : (!string.IsNullOrWhiteSpace(Path.GetDirectoryName(savedPath))
                    ? Path.GetDirectoryName(savedPath)
                    : GetTargetInboxFolder());

            string senderHint = string.Empty;
            try
            {
                string parentName = Path.GetFileName(Path.GetDirectoryName(savedPath) ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(parentName) && parentName.Contains("_"))
                    senderHint = parentName.Split('_')[0];
            }
            catch
            {
            }

            string msg = string.IsNullOrWhiteSpace(senderHint)
                ? $"Recibiste \"{itemName}\". Clic para abrir carpeta."
                : $"Recibiste \"{itemName}\" desde {senderHint}. Clic para abrir carpeta.";

            ServiceNotifier.SendSuccess(
                "Archivo recibido",
                $"{msg}|{folderToOpen}"
            );
        }

        private void OnReceiverError(string errorMessage)
        {
            ServiceNotifier.SendError("Error en recepción", errorMessage);
        }

        protected override void OnStop()
        {
            try
            {
                _stopEvent?.Set();

                if (_receiver != null)
                {
                    _receiver.FileReceived -= OnFileReceived;
                    _receiver.Error -= OnReceiverError;
                    _receiver.Stop();
                    _receiver = null;
                }

                ServiceNotifier.SendWarning(
                    "Servicio detenido",
                    "ONKOSTransferService se ha detenido."
                );
            }
            catch
            {
            }
            finally
            {
                _stopEvent?.Dispose();
                _stopEvent = null;
            }
        }
    }
}
