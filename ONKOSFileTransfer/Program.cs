using System;
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
using ONKOSFileTransfer.Network;

namespace ONKOSFileTransfer
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool esModoBandeja = args != null && args.Any(a =>
                string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/tray", StringComparison.OrdinalIgnoreCase));

            string nombreMutex = esModoBandeja
                ? "Global\\ONKOSFileTransfer_Tray_Mutex"
                : "Global\\ONKOSFileTransfer_UI_Mutex";

            using (var mutex = new Mutex(true, nombreMutex, out bool esPrimeraInstancia))
            {
                if (!esPrimeraInstancia)
                    return;

                AppSettingsStore.EnsureInboxFolderReady();

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
                string rutaInbox = AppSettingsStore.GetInboxFolder();
                AppSettingsStore.EnsureInboxFolderReady(rutaInbox);
                System.Diagnostics.Process.Start("explorer.exe", rutaInbox);
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, (s, e) =>
            {
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
            catch
            {
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
            try { pipeSecurity = CreatePipeSecurityForService(); } catch { }

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
                catch
                {
                    try { await Task.Delay(400, token); } catch { break; }
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
                try { _pipeCts?.Cancel(); } catch { }
                try { _pipeCts?.Dispose(); } catch { }
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
            catch
            {
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
                Encoding.UTF8);
        }
    }

    public static class AppSettingsStore
    {
        private static readonly string SettingsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ONKOS", "ruta_recibidos.txt");

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
            catch
            {
            }

            return GetDefaultInboxFolder();
        }

        public static void SetInboxFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return;

            string cleanPath = folderPath.Trim();
            string dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(SettingsPath, cleanPath, Encoding.UTF8);
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
                    catch
                    {
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
            catch
            {
            }
        }
    }
}
```[cite: 9]

#### 2. `ONKOS_Setup.iss` completo (aplica el ícono después de instalar y limpia atributos + carpetas al desinstalar)

```ini
[Setup]
AppId={{8B5E9A2C-4D1F-4C3A-9E7B-2F6A1D8C5E40}
AppName=ONKOS File Transfer 2.0
AppVersion=2.0
AppPublisher=ONKOS
DefaultDirName={autopf}\ONKOS\FileTransfer
DefaultGroupName=ONKOS File Transfer 2.0
OutputDir=Output
OutputBaseFilename=Instalador_ONKOS_File_Transfer_2.0
SetupIconFile=ONKOSFileTransfer\onkos.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=ONKOS File Transfer 2.0
UninstallDisplayIcon={app}\ONKOSFileTransfer.exe
WizardStyle=modern
PrivilegesRequired=admin
ShowLanguageDialog=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Dirs]
Name: "{commonappdata}\ONKOS"; Permissions: users-modify
Name: "{userdesktop}\Archivos Recibidos ONKOS"

[Files]
Source: "ONKOSFileTransfer\bin\Release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "ONKOSTransferService\bin\Release\*"; DestDir: "{app}\Servicio"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "ONKOSFileTransfer\onkos.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "ONKOSFileTransfer\recibidos.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\ONKOS File Transfer 2.0"; Filename: "{app}\ONKOSFileTransfer.exe"; IconFilename: "{app}\onkos.ico"
Name: "{group}\Carpeta Archivos Recibidos ONKOS"; Filename: "{userdesktop}\Archivos Recibidos ONKOS"; IconFilename: "{app}\recibidos.ico"
Name: "{group}\Desinstalar ONKOS File Transfer 2.0"; Filename: "{uninstallexe}"
Name: "{autodesktop}\ONKOS File Transfer 2.0"; Filename: "{app}\ONKOSFileTransfer.exe"; IconFilename: "{app}\onkos.ico"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el &Escritorio"; GroupDescription: "Accesos directos adicionales:"

[Registry]
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ONKOSFileTransfer"; ValueData: """{app}\ONKOSFileTransfer.exe"" --tray"; Flags: uninsdeletevalue

[Run]
Filename: "sc.exe"; Parameters: "create ONKOSTransferService binPath= ""{app}\Servicio\ONKOS.TransferService.exe"" start= auto DisplayName= ""ONKOS File Transfer Service"""; Flags: runhidden
Filename: "sc.exe"; Parameters: "start ONKOSTransferService"; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS Transfer TCP 5050"""; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""ONKOS Transfer TCP 5050"" dir=in action=allow protocol=TCP localport=5050"; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS Discovery UDP 5051"""; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""ONKOS Discovery UDP 5051"" dir=in action=allow protocol=UDP localport=5051"; Flags: runhidden
Filename: "{app}\ONKOSFileTransfer.exe"; Description: "Abrir ONKOS File Transfer 2.0 para registrar mi equipo"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM ONKOSFileTransfer.exe"; Flags: runhidden; RunOnceId: "KillOnkosUi"
Filename: "taskkill.exe"; Parameters: "/F /IM ONKOS.TransferService.exe"; Flags: runhidden; RunOnceId: "KillOnkosSvc"
Filename: "sc.exe"; Parameters: "stop ONKOSTransferService"; Flags: runhidden; RunOnceId: "StopOnkosSvc"
Filename: "sc.exe"; Parameters: "delete ONKOSTransferService"; Flags: runhidden; RunOnceId: "DeleteOnkosSvc"
Filename: "cmd.exe"; Parameters: "/C timeout /t 2 /nobreak >nul & attrib -r -s -h ""{userdesktop}\Archivos Recibidos ONKOS\desktop.ini"" 2>nul & attrib -r -s -h ""{userdesktop}\Archivos Recibidos ONKOS\recibidos.ico"" 2>nul & attrib -r ""{userdesktop}\Archivos Recibidos ONKOS"" 2>nul & rmdir /s /q ""{userdesktop}\Archivos Recibidos ONKOS"" 2>nul & attrib -r -s -h ""{commondesktop}\Archivos Recibidos ONKOS\desktop.ini"" 2>nul & attrib -r -s -h ""{commondesktop}\Archivos Recibidos ONKOS\recibidos.ico"" 2>nul & attrib -r ""{commondesktop}\Archivos Recibidos ONKOS"" 2>nul & rmdir /s /q ""{commondesktop}\Archivos Recibidos ONKOS"" 2>nul"; Flags: runhidden; RunOnceId: "CleanInboxFolders"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS Transfer TCP 5050"""; Flags: runhidden; RunOnceId: "DelFwTcp"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS Discovery UDP 5051"""; Flags: runhidden; RunOnceId: "DelFwUdp"

[Code]
procedure SHChangeNotify(wEventId: Integer; uFlags: Integer; dwItem1: Integer; dwItem2: Integer);
  external 'SHChangeNotify@shell32.dll stdcall';

procedure ConfigureInboxFolderIcon(const FolderPath, IconFilePath: string);
var
  DesktopIniPath, LocalIconPath: string;
  IniLines: TArrayOfString;
begin
  if not DirExists(FolderPath) then
    ForceDirectories(FolderPath);

  LocalIconPath := FolderPath + '\recibidos.ico';
  if FileExists(IconFilePath) then
  begin
    if FileExists(LocalIconPath) then
      SetFileAttributes(LocalIconPath, 0);
    FileCopy(IconFilePath, LocalIconPath, False);
    SetFileAttributes(LocalIconPath, FILE_ATTRIBUTE_HIDDEN or FILE_ATTRIBUTE_SYSTEM);
  end;

  DesktopIniPath := FolderPath + '\desktop.ini';
  if FileExists(DesktopIniPath) then
    SetFileAttributes(DesktopIniPath, 0);

  SetArrayLength(IniLines, 5);
  IniLines[0] := '[.ShellClassInfo]';
  IniLines[1] := 'IconResource=' + LocalIconPath + ',0';
  IniLines[2] := 'IconFile=' + LocalIconPath + ',0';
  IniLines[3] := 'IconIndex=0';
  IniLines[4] := 'InfoTip=Carpeta de archivos recibidos por ONKOS File Transfer';
  SaveStringsToFile(DesktopIniPath, IniLines, True);

  SetFileAttributes(DesktopIniPath, FILE_ATTRIBUTE_HIDDEN or FILE_ATTRIBUTE_SYSTEM);
  SetFileAttributes(FolderPath, GetFileAttributes(FolderPath) or FILE_ATTRIBUTE_READONLY);
end;

procedure RemoveInboxFolderSafe(const FolderPath: string);
var
  IniPath, IconPath: string;
begin
  if not DirExists(FolderPath) then
    Exit;

  IniPath := FolderPath + '\desktop.ini';
  IconPath := FolderPath + '\recibidos.ico';

  if FileExists(IniPath) then
    SetFileAttributes(IniPath, 0);
  if FileExists(IconPath) then
    SetFileAttributes(IconPath, 0);

  SetFileAttributes(FolderPath, GetFileAttributes(FolderPath) and not FILE_ATTRIBUTE_READONLY);
  DelTree(FolderPath, True, True, True);
end;

procedure InitializeWizard();
begin
  WizardForm.Font.Name := 'Segoe UI';
  WizardForm.WelcomeLabel1.Font.Color := $007C4DE0;
  WizardForm.FinishedHeadingLabel.Font.Color := $007C4DE0;
  WizardForm.PageNameLabel.Font.Color := $004A269C;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  IconPath: string;
begin
  if CurStep = ssPostInstall then
  begin
    IconPath := ExpandConstant('{app}\recibidos.ico');
    ConfigureInboxFolderIcon(ExpandConstant('{userdesktop}\Archivos Recibidos ONKOS'), IconPath);
    SHChangeNotify($08000000, 0, 0, 0);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{commonappdata}\ONKOS');
    if DirExists(DataDir) then
      DelTree(DataDir, True, True, True);

    RemoveInboxFolderSafe(ExpandConstant('{userdesktop}\Archivos Recibidos ONKOS'));
    RemoveInboxFolderSafe(ExpandConstant('{commondesktop}\Archivos Recibidos ONKOS'));
    SHChangeNotify($08000000, 0, 0, 0);
  end;
end;
```[cite: 23]

#### 3. `Desinstalar_ONKOS.bat` completo (limpia atributos de solo lectura/oculto/sistema antes de borrar la carpeta)

```bat
@echo off
chcp 65001 >nul
title Desinstalador Automatico - ONKOS File Transfer

net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo Desinstalando ONKOS File Transfer por completo...
echo.

taskkill /F /IM ONKOSFileTransfer.exe >nul 2>&1
taskkill /F /IM ONKOS.TransferService.exe >nul 2>&1

sc query ONKOSTransferService >nul 2>&1
if %errorlevel% equ 0 (
    sc stop ONKOSTransferService >nul 2>&1
    timeout /t 2 /nobreak >nul
    sc delete ONKOSTransferService >nul 2>&1
)

netsh advfirewall firewall delete rule name="ONKOS File Transfer" >nul 2>&1
netsh advfirewall firewall delete rule name="ONKOS File Transfer TCP 5050" >nul 2>&1
netsh advfirewall firewall delete rule name="ONKOS Transfer Service (TCP 5050)" >nul 2>&1
netsh advfirewall firewall delete rule name="ONKOS Discovery UDP 5051" >nul 2>&1

schtasks /Delete /TN "ONKOSFileTransfer" /F >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "ONKOSFileTransfer" /f >nul 2>&1
reg delete "HKLM\Software\Microsoft\Windows\CurrentVersion\Run" /v "ONKOSFileTransfer" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "ONKOSFileTransferTray" /f >nul 2>&1

if exist "C:\ONKOS" rmdir /s /q "C:\ONKOS"
if exist "%ProgramData%\ONKOS" rmdir /s /q "%ProgramData%\ONKOS"
if exist "%ProgramFiles%\ONKOS\FileTransfer" rmdir /s /q "%ProgramFiles%\ONKOS\FileTransfer"
if exist "%ProgramFiles%\ONKOS" rmdir /s /q "%ProgramFiles%\ONKOS"
if exist "%ProgramFiles(x86)%\ONKOS\FileTransfer" rmdir /s /q "%ProgramFiles(x86)%\ONKOS\FileTransfer"
if exist "%ProgramFiles(x86)%\ONKOS" rmdir /s /q "%ProgramFiles(x86)%\ONKOS"
if exist "%LocalAppData%\ONKOS" rmdir /s /q "%LocalAppData%\ONKOS"
if exist "%AppData%\ONKOS" rmdir /s /q "%AppData%\ONKOS"

if exist "%Public%\Desktop\ONKOS File Transfer 2.0.lnk" del /f /q "%Public%\Desktop\ONKOS File Transfer 2.0.lnk"
if exist "%Public%\Desktop\ONKOS File Transfer.lnk" del /f /q "%Public%\Desktop\ONKOS File Transfer.lnk"
if exist "%UserProfile%\Desktop\ONKOS File Transfer 2.0.lnk" del /f /q "%UserProfile%\Desktop\ONKOS File Transfer 2.0.lnk"
if exist "%UserProfile%\Desktop\ONKOS File Transfer.lnk" del /f /q "%UserProfile%\Desktop\ONKOS File Transfer.lnk"
if exist "%ProgramData%\Microsoft\Windows\Start Menu\Programs\ONKOS File Transfer 2.0" rmdir /s /q "%ProgramData%\Microsoft\Windows\Start Menu\Programs\ONKOS File Transfer 2.0"
if exist "%ProgramData%\Microsoft\Windows\Start Menu\Programs\ONKOS File Transfer.lnk" del /f /q "%ProgramData%\Microsoft\Windows\Start Menu\Programs\ONKOS File Transfer.lnk"
if exist "%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup\ONKOS Tray.lnk" del /f /q "%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup\ONKOS Tray.lnk"

if exist "%UserProfile%\Desktop\Archivos Recibidos ONKOS" (
    attrib -r -s -h "%UserProfile%\Desktop\Archivos Recibidos ONKOS\desktop.ini" >nul 2>&1
    attrib -r -s -h "%UserProfile%\Desktop\Archivos Recibidos ONKOS\recibidos.ico" >nul 2>&1
    attrib -r "%UserProfile%\Desktop\Archivos Recibidos ONKOS" >nul 2>&1
    rmdir /s /q "%UserProfile%\Desktop\Archivos Recibidos ONKOS"
)
if exist "%Public%\Desktop\Archivos Recibidos ONKOS" (
    attrib -r -s -h "%Public%\Desktop\Archivos Recibidos ONKOS\desktop.ini" >nul 2>&1
    attrib -r -s -h "%Public%\Desktop\Archivos Recibidos ONKOS\recibidos.ico" >nul 2>&1
    attrib -r "%Public%\Desktop\Archivos Recibidos ONKOS" >nul 2>&1
    rmdir /s /q "%Public%\Desktop\Archivos Recibidos ONKOS"
)

echo Desinstalacion completa finalizada.
timeout /t 2 /nobreak >nul
exit /b 0
```[cite: 11]

---

### Pasos para aplicar y verificar

<Steps>
  <Step subtitle="Reemplazo en repositorio" title="Pega el código en Program.cs, ONKOS_Setup.iss y Desinstalar_ONKOS.bat">
    Actualiza `ONKOSFileTransfer/Program.cs`, `ONKOS_Setup.iss` y `Desinstalar_ONKOS.bat` con los bloques anteriores[cite: 9, 11, 23].
    *Verification:* Ambos proyectos (`.csproj`) y `iscc ONKOS_Setup.iss` compilan sin errores en Release[cite: 3, 14, 23].
  </Step>
  <Step subtitle="Prueba de ícono al instalar" title="Instala en un equipo de prueba y revisa el escritorio">
    Ejecuta `Instalador_ONKOS_File_Transfer_2.0.exe` y observa la carpeta **Archivos Recibidos ONKOS** en el escritorio[cite: 3, 23].
    *Verification:* La carpeta muestra el ícono personalizado de recibidos (`recibidos.ico`) y no queda duplicada sin ícono[cite: 2, 23].
  </Step>
  <Step subtitle="Prueba de desinstalación limpia" title="Desinstala el programa y comprueba que no quede la carpeta">
    Ejecuta el desinstalador de ONKOS (o `Desinstalar_ONKOS.bat`) y revisa el escritorio y `C:\Users\Public\Desktop`[cite: 11, 23].
    *Verification:* La carpeta **Archivos Recibidos ONKOS** desaparece por completo sin dejar carpeta ni ícono residual.
  </Step>
</Steps>
