#define MyAppName "ONKOS File Transfer 2.0"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "Clínica ONKOS"
#define MyAppExeName "ONKOSFileTransfer.exe"
#define MyServiceExeName "ONKOS.TransferService.exe"
#define MyServiceName "ONKOSTransferService"

[Setup]
AppId={{8B5E9A2C-4D1F-4C3A-9E7B-2F6A1D8C5E40}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\ONKOS\FileTransfer
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=Instalador_ONKOS_File_Transfer_2.0
SetupIconFile=ONKOSFileTransfer\onkos.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern
PrivilegesRequired=admin
ShowLanguageDialog=no
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Dirs]
Name: "{commonappdata}\ONKOS"; Permissions: users-modify
Name: "{commonappdata}\ONKOS\logs"; Permissions: users-modify
Name: "{commonappdata}\ONKOS\Inbox"; Permissions: users-modify

[Files]
Source: "ONKOS.UI\bin\Release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "ONKOS.Service\bin\Release\*"; DestDir: "{app}\Servicio"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "ONKOSFileTransfer\onkos.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "ONKOSFileTransfer\recibidos.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\onkos.ico"
Name: "{group}\Carpeta Archivos Recibidos ONKOS"; Filename: "{commonappdata}\ONKOS\Inbox"; IconFilename: "{app}\recibidos.ico"
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\onkos.ico"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el &Escritorio"; GroupDescription: "Accesos directos adicionales:"

[Run]
Filename: "sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden
Filename: "sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden
Filename: "sc.exe"; Parameters: "create {#MyServiceName} binPath= ""{app}\Servicio\{#MyServiceExeName}"" obj= ""NT AUTHORITY\LocalService"" start= delayed-auto DisplayName= ""ONKOS Transfer Service"""; Flags: runhidden
Filename: "sc.exe"; Parameters: "description {#MyServiceName} ""Servicio local cifrado (TLS 1.2+) de recepción de archivos de ONKOS en puerto TCP 5050."""; Flags: runhidden
Filename: "sc.exe"; Parameters: "failure {#MyServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/5000"; Flags: runhidden
Filename: "sc.exe"; Parameters: "start {#MyServiceName}"; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS Transfer TCP 5050"""; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""ONKOS Transfer TCP 5050"" dir=in action=allow protocol=TCP localport=5050 remoteip=LocalSubnet profile=private,domain"; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS Discovery UDP 5051"""; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""ONKOS Discovery UDP 5051"" dir=in action=allow protocol=UDP localport=5051 remoteip=LocalSubnet profile=private,domain"; Flags: runhidden
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillOnkosUi"
Filename: "sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden; RunOnceId: "StopOnkosSvc"
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyServiceExeName}"; Flags: runhidden; RunOnceId: "KillOnkosSvc"
Filename: "sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden; RunOnceId: "DeleteOnkosSvc"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS Transfer TCP 5050"""; Flags: runhidden; RunOnceId: "DelFwTcp"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS Discovery UDP 5051"""; Flags: runhidden; RunOnceId: "DelFwUdp"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  Exec('taskkill.exe', '/F /IM ONKOSFileTransfer.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('sc.exe', 'stop ONKOSTransferService', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM ONKOS.TransferService.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
