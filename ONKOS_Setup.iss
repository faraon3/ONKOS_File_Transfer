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
Name: "{commondesktop}\Archivos Recibidos ONKOS"

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
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS File Transfer TCP 5050"""; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""ONKOS File Transfer TCP 5050"" dir=in action=allow protocol=TCP localport=5050"; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS Discovery UDP 5051"""; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""ONKOS Discovery UDP 5051"" dir=in action=allow protocol=UDP localport=5051"; Flags: runhidden
Filename: "attrib.exe"; Parameters: "+r ""{userdesktop}\Archivos Recibidos ONKOS"""; Flags: runhidden
Filename: "attrib.exe"; Parameters: "+s +h ""{userdesktop}\Archivos Recibidos ONKOS\desktop.ini"""; Flags: runhidden
Filename: "{app}\ONKOSFileTransfer.exe"; Description: "Abrir ONKOS File Transfer 2.0 para registrar mi equipo"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM ONKOSFileTransfer.exe"; Flags: runhidden; RunOnceId: "KillOnkosUi"
Filename: "taskkill.exe"; Parameters: "/F /IM ONKOS.TransferService.exe"; Flags: runhidden; RunOnceId: "KillOnkosSvc"
Filename: "sc.exe"; Parameters: "stop ONKOSTransferService"; Flags: runhidden; RunOnceId: "StopOnkosSvc"
Filename: "sc.exe"; Parameters: "delete ONKOSTransferService"; Flags: runhidden; RunOnceId: "DeleteOnkosSvc"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS File Transfer TCP 5050"""; Flags: runhidden; RunOnceId: "DelFwTcp"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS Discovery UDP 5051"""; Flags: runhidden; RunOnceId: "DelFwUdp"

[Code]
procedure InitializeWizard();
begin
  WizardForm.Font.Name := 'Segoe UI';
  WizardForm.WelcomeLabel1.Font.Color := $007C4DE0;
  WizardForm.FinishedHeadingLabel.Font.Color := $007C4DE0;
  WizardForm.PageNameLabel.Font.Color := $004A269C;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  DesktopIniPath: string;
  IconPath: string;
  IniLines: TArrayOfString;
begin
  if CurStep = ssPostInstall then
  begin
    IconPath := ExpandConstant('{app}\recibidos.ico');
    DesktopIniPath := ExpandConstant('{userdesktop}\Archivos Recibidos ONKOS\desktop.ini');
    SetArrayLength(IniLines, 3);
    IniLines[0] := '[.ShellClassInfo]';
    IniLines[1] := 'IconResource=' + IconPath + ',0';
    IniLines[2] := 'InfoTip=Carpeta de archivos recibidos por ONKOS File Transfer';
    SaveStringsToFile(DesktopIniPath, IniLines, False);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
  InboxDir: string;
  PublicInboxDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{commonappdata}\ONKOS');
    if DirExists(DataDir) then
      DelTree(DataDir, True, True, True);

    InboxDir := ExpandConstant('{userdesktop}\Archivos Recibidos ONKOS');
    if DirExists(InboxDir) then
      DelTree(InboxDir, True, True, True);

    PublicInboxDir := ExpandConstant('{commondesktop}\Archivos Recibidos ONKOS');
    if DirExists(PublicInboxDir) then
      DelTree(PublicInboxDir, True, True, True);
  end;
end;
