using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using ONKOSFileTransfer.Network;

namespace ONKOSFileTransfer
{
    internal static class Program
    {
        private static long _lastUnobservedLogTicks;

        private const int SW_RESTORE = 9;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            bool esModoBandeja = args != null && args.Any(a =>
                string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/tray", StringComparison.OrdinalIgnoreCase));

            RegisterGlobalExceptionHandlers(esModoBandeja);
            OnkosLogger.Info("Program", esModoBandeja ? "Iniciando en modo bandeja (--tray)" : "Iniciando interfaz de usuario");

            string nombreMutex = esModoBandeja
                ? "Global\\ONKOSFileTransfer_Tray_Mutex"
                : "Global\\ONKOSFileTransfer_UI_Mutex";

            using (var mutex = new Mutex(true, nombreMutex, out bool esPrimeraInstancia))
            {
                if (!esPrimeraInstancia)
                {
                    if (!esModoBandeja)
                        BringExistingWindowToFront();

                    OnkosLogger.Info("Program", $"Instancia duplicada detectada ({nombreMutex}), cerrando instancia secundaria.");
                    return;
                }

                AppSettingsStore.EnsureInboxFolderReady();
                EnsureTrayAutoStart();

                PeerInfo profile = null;
                if (!esModoBandeja && !UserProfileStore.TryLoad(out profile))
                {
                    using (var welcome = new WelcomeRegistrationForm())
                    {
                        if (welcome.ShowDialog() != DialogResult.OK || welcome.RegisteredProfile == null)
                            return;
                        profile = welcome.RegisteredProfile;
                    }
                }

                if (esModoBandeja)
                {
                    PeerDiscoveryService trayDiscovery = null;
                    if (UserProfileStore.TryLoad(out PeerInfo trayProfile))
                    {
                        trayDiscovery = new PeerDiscoveryService();
                        trayDiscovery.Start(trayProfile);
                    }

                    using (trayDiscovery)
                    using (var trayCtx = new TrayHostContext())
                    {
                        Application.Run(trayCtx);
                    }
                }
                else
                {
                    Application.Run(new MainForm(profile));
                }
            }
        }

        private static void BringExistingWindowToFront()
        {
            try
            {
                IntPtr hWnd = FindWindow(null, "ONKOS File Transfer 2.0");
                if (hWnd == IntPtr.Zero)
                    return;

                if (IsIconic(hWnd))
                    ShowWindow(hWnd, SW_RESTORE);
                else
                    ShowWindow(hWnd, 5); // SW_SHOW

                SetForegroundWindow(hWnd);
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("Program", "No se pudo traer la ventana existente al frente.", ex);
            }
        }

        private static void EnsureTrayAutoStart()
        {
            try
            {
                const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
                const string valueName = "ONKOSFileTransferTray";
                string exePath = Application.ExecutablePath;
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                    return;

                string expectedCommand = $"\"{exePath}\" --tray";

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKeyPath, true))
                {
                    if (key == null)
                        return;

                    string current = key.GetValue(valueName) as string;
                    if (!string.Equals(current, expectedCommand, StringComparison.OrdinalIgnoreCase))
                        key.SetValue(valueName, expectedCommand, RegistryValueKind.String);
                }
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("Program", "No se pudo registrar el inicio automático de la bandeja (--tray).", ex);
            }
        }

        private static void RegisterGlobalExceptionHandlers(bool isTrayMode)
        {
            Application.ThreadException += (sender, e) =>
            {
                OnkosLogger.Error("UI.ThreadException", "Excepción no controlada en hilo de UI", e.Exception);
                if (!isTrayMode)
                {
                    MessageBox.Show(
                        "Ocurrió un inconveniente inesperado. El detalle quedó guardado en el registro de ONKOS.",
                        "ONKOS File Transfer",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                OnkosLogger.Error("AppDomain.UnhandledException", $"Excepción no controlada (IsTerminating={e.IsTerminating})", ex);
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                e.SetObserved();
                long nowTicks = DateTime.UtcNow.Ticks;
                long lastTicks = Interlocked.Read(ref _lastUnobservedLogTicks);
                if (nowTicks - lastTicks > TimeSpan.FromSeconds(10).Ticks)
                {
                    Interlocked.Exchange(ref _lastUnobservedLogTicks, nowTicks);
                    OnkosLogger.Warn("TaskScheduler.Unobserved", "Excepción de tarea en segundo plano observada (resumen cada 10s).", e.Exception);
                }
            };
        }
    }

    internal sealed class TrayHostContext : ApplicationContext
    {
        private const string PipeName = "ONKOS_Transfer_Events";
        private readonly NotifyIcon _notifyIcon;
        private readonly CancellationTokenSource _pipeCts;

        public TrayHostContext()
        {
            ModernToastNotifier.Inicializar();

            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir ONKOS File Transfer", null, (s, e) => AbrirVentanaPrincipal());
            menu.Items.Add("Abrir carpeta de recibidos", null, (s, e) =>
            {
                TransferHistoryStore.MarkReceivedAsSeen();
                string rutaInbox = AppSettingsStore.GetInboxFolder();
                AppSettingsStore.EnsureInboxFolderReady(rutaInbox);
                Process.Start("explorer.exe", rutaInbox);
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, (s, e) =>
            {
                OnkosLogger.Info("TrayHostContext", "Salida solicitada desde el menú de bandeja.");
                _notifyIcon.Visible = false;
                Application.ExitThread();
            });

            Icon trayIcon = SystemIcons.Application;
            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "onkos.ico");
                if (File.Exists(icoPath))
                    trayIcon = new Icon(icoPath);
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("TrayHostContext", "No se pudo cargar onkos.ico, usando ícono por defecto.", ex);
            }

            _notifyIcon = new NotifyIcon
            {
                Text = "ONKOS File Transfer 2.0",
                Icon = trayIcon,
                ContextMenuStrip = menu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => AbrirVentanaPrincipal();

            _pipeCts = new CancellationTokenSource();
            _ = Task.Run(() => ListenServicePipeLoop(_pipeCts.Token));
        }

        private static PipeSecurity CreatePipeSecurityForService()
        {
            var ps = new PipeSecurity();
            ps.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
            ps.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
            return ps;
        }

        private async Task ListenServicePipeLoop(CancellationToken token)
        {
            PipeSecurity pipeSecurity = null;
            try
            {
                pipeSecurity = CreatePipeSecurityForService();
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("TrayHostContext.Pipe", "No se pudo aplicar PipeSecurity explícita.", ex);
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        4096,
                        4096,
                        pipeSecurity))
                    {
                        await server.WaitForConnectionAsync(token);
                        using (var reader = new StreamReader(server, Encoding.UTF8))
                        {
                            string raw = await reader.ReadToEndAsync();
                            if (!string.IsNullOrWhiteSpace(raw))
                                DispatchServiceMessage(raw);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnkosLogger.Warn("TrayHostContext.Pipe", "Error en escucha de NamedPipe; reintentando.", ex);
                    try { await Task.Delay(400, token); } catch (OperationCanceledException) { break; }
                }
            }
        }

        private void DispatchServiceMessage(string raw)
        {
            string[] parts = raw.Split(new[] { '|' }, 4);
            if (parts.Length < 3)
                return;

            string level = parts[0].Trim().ToUpperInvariant();
            string title = parts[1].Trim();
            string message = parts[2].Trim();
            string extraPath = parts.Length >= 4 ? parts[3].Trim() : string.Empty;

            if (level == "INFO" && title.IndexOf("iniciado", StringComparison.OrdinalIgnoreCase) >= 0)
                return;

            bool isError = level == "ERROR" || level == "WARNING";
            string badge = level == "SUCCESS" ? "RECIBIDO" : (isError ? "AVISO" : "ONKOS");
            string rutaAbrir = null;

            if (level == "SUCCESS")
            {
                if (!string.IsNullOrWhiteSpace(extraPath) && Directory.Exists(extraPath))
                    rutaAbrir = extraPath;
                else if (!string.IsNullOrWhiteSpace(extraPath) && File.Exists(extraPath))
                    rutaAbrir = Path.GetDirectoryName(extraPath);
                else
                    rutaAbrir = AppSettingsStore.GetInboxFolder();
            }

            ModernToastNotifier.MostrarNotificacion(title, message, rutaAbrir, isError, badge);
        }

        private void AbrirVentanaPrincipal()
        {
            if (!UserProfileStore.TryLoad(out PeerInfo profile))
            {
                using (var welcome = new WelcomeRegistrationForm())
                {
                    if (welcome.ShowDialog() != DialogResult.OK || welcome.RegisteredProfile == null)
                        return;
                    profile = welcome.RegisteredProfile;
                }
            }

            var form = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            if (form == null || form.IsDisposed)
            {
                form = new MainForm(profile);
                form.Show();
            }
            else
            {
                if (form.WindowState == FormWindowState.Minimized)
                    form.WindowState = FormWindowState.Normal;
                form.Show();
                form.BringToFront();
                form.Activate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _pipeCts?.Cancel(); } catch (Exception ex) { OnkosLogger.Warn("TrayHostContext", "Error cancelando CTS.", ex); }
                try { _pipeCts?.Dispose(); } catch (Exception ex) { OnkosLogger.Warn("TrayHostContext", "Error disponiendo CTS.", ex); }
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }

    public class PeerInfo
    {
        public string FullName { get; set; }
        public string Area { get; set; }
        public string IPv4 { get; set; }
        public string MachineName { get; set; }
        public DateTime LastSeenUtc { get; set; }

        public string DisplayText => string.IsNullOrWhiteSpace(Area)
            ? FullName
            : FullName + " — " + Area;

        public override string ToString() => DisplayText;
    }

    public static class UserProfileStore
    {
        private static readonly string ProfilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ONKOS", "perfil.txt");

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
                if (!File.Exists(ProfilePath))
                    return false;

                var lines = File.ReadAllLines(ProfilePath, Encoding.UTF8);
                if (lines.Length < 2)
                    return false;

                string name = lines[0].Trim();
                string area = lines[1].Trim();
                if (IsIgnoredSystemName(name) || string.IsNullOrWhiteSpace(area))
                    return false;

                profile = new PeerInfo
                {
                    FullName = name,
                    Area = area,
                    MachineName = Environment.MachineName,
                    IPv4 = NetworkManager.GetLocalIPv4()
                };

                return true;
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("UserProfileStore", "Error leyendo perfil de usuario.", ex);
                return false;
            }
        }

        public static void Save(string fullName, string area)
        {
            string dir = Path.GetDirectoryName(ProfilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string cleanName = (fullName ?? string.Empty).Replace("|", "/").Trim();
            string cleanArea = (area ?? string.Empty).Replace("|", "/").Trim();

            File.WriteAllLines(
                ProfilePath,
                new[] { cleanName, cleanArea },
                new UTF8Encoding(false));
        }
    }

    public static class AppSettingsStore
    {
        private static readonly string SettingsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ONKOS", "ruta_recibidos.txt");

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const int SHCNE_UPDATEDIR = 0x00001000;
        private const int SHCNF_IDLIST = 0x0000;
        private const int SHCNF_PATHW = 0x0005;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int wEventId, int uFlags, [MarshalAs(UnmanagedType.LPWStr)] string dwItem1, IntPtr dwItem2);

        public static string GetDefaultInboxFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Archivos Recibidos ONKOS");
        }

        public static string GetInboxFolder()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string saved = File.ReadAllText(SettingsPath, Encoding.UTF8).Trim();
                    if (!string.IsNullOrWhiteSpace(saved))
                        return saved;
                }
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("AppSettingsStore", "Error leyendo ruta_recibidos.txt; usando Escritorio por defecto.", ex);
            }

            string defaultDesktop = GetDefaultInboxFolder();
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(SettingsPath, defaultDesktop, Utf8NoBom);
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("AppSettingsStore", "No se pudo inicializar ruta_recibidos.txt.", ex);
            }

            return defaultDesktop;
        }

        public static void SetInboxFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return;

            string cleanPath = folderPath.Trim();
            string dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(SettingsPath, cleanPath, Utf8NoBom);
            EnsureInboxFolderReady(cleanPath);
        }

        public static void EnsureInboxFolderReady(string customFolder = null)
        {
            try
            {
                string targetFolder = string.IsNullOrWhiteSpace(customFolder) ? GetInboxFolder() : customFolder;
                Directory.CreateDirectory(targetFolder);

                string iconSource = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recibidos.ico");
                string localIcon = Path.Combine(targetFolder, "recibidos.ico");

                if (File.Exists(iconSource))
                {
                    try
                    {
                        if (File.Exists(localIcon))
                            File.SetAttributes(localIcon, FileAttributes.Normal);
                        File.Copy(iconSource, localIcon, true);
                        File.SetAttributes(localIcon, FileAttributes.Hidden | FileAttributes.System);
                    }
                    catch (Exception ex)
                    {
                        OnkosLogger.Warn("AppSettingsStore", "No se pudo copiar recibidos.ico.", ex);
                    }
                }

                string iniPath = Path.Combine(targetFolder, "desktop.ini");
                string iconRef = File.Exists(localIcon) ? "recibidos.ico,0" : (iconSource + ",0");

                if (File.Exists(iniPath))
                    File.SetAttributes(iniPath, FileAttributes.Normal);

                File.WriteAllLines(iniPath, new[]
                {
                    "[.ShellClassInfo]",
                    "IconResource=" + iconRef,
                    "IconFile=" + iconRef,
                    "IconIndex=0",
                    "InfoTip=Carpeta de archivos recibidos por ONKOS File Transfer"
                }, Encoding.Unicode);

                File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System);

                var di = new DirectoryInfo(targetFolder);
                di.Attributes = (di.Attributes | FileAttributes.ReadOnly) & ~FileAttributes.Normal;

                SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, targetFolder, IntPtr.Zero);
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("AppSettingsStore", "Error preparando carpeta de recibidos.", ex);
            }
        }
    }
}
