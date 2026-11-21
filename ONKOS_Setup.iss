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

[Files]
Source: "ONKOSFileTransfer\bin\Release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ONKOS File Transfer 2.0"; Filename: "{app}\ONKOSFileTransfer.exe"
Name: "{group}\Desinstalar ONKOS File Transfer 2.0"; Filename: "{uninstallexe}"
Name: "{autodesktop}\ONKOS File Transfer 2.0"; Filename: "{app}\ONKOSFileTransfer.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el &Escritorio"; GroupDescription: "Accesos directos adicionales:"

[Registry]
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ONKOSFileTransfer"; ValueData: """{app}\ONKOSFileTransfer.exe"" --tray"; Flags: uninsdeletevalue

[Run]
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""ONKOS File Transfer TCP 5050"" dir=in action=allow protocol=TCP localport=5050"; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""ONKOS Discovery UDP 5051"" dir=in action=allow protocol=UDP localport=5051"; Flags: runhidden
Filename: "{app}\ONKOSFileTransfer.exe"; Description: "Abrir ONKOS File Transfer 2.0 ahora"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM ONKOSFileTransfer.exe"; Flags: runhidden; RunOnceId: "KillOnkosUi"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS File Transfer TCP 5050"""; Flags: runhidden; RunOnceId: "DelFwTcp"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS Discovery UDP 5051"""; Flags: runhidden; RunOnceId: "DelFwUdp"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
  InboxDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{commonappdata}\ONKOS');
    if DirExists(DataDir) then
      DelTree(DataDir, True, True, True);

    InboxDir := ExpandConstant('{userdesktop}\Archivos Recibidos ONKOS');
    if DirExists(InboxDir) then
      DelTree(InboxDir, True, True, True);
  end;
end;
