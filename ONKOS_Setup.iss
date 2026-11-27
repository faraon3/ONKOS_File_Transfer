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
Name: "{commonappdata}\ONKOS\logs"; Permissions: users-modify
Name: "{commonappdata}\ONKOS\Inbox"; Permissions: users-modify

[Files]
Source: "ONKOS.UI\bin\Release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "ONKOS.Service\bin\Release\*"; DestDir: "{app}\Servicio"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "ONKOSFileTransfer\onkos.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "ONKOSFileTransfer\recibidos.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\ONKOS File Transfer 2.0"; Filename: "{app}\ONKOSFileTransfer.exe"; IconFilename: "{app}\onkos.ico"
Name: "{group}\Carpeta Archivos Recibidos ONKOS"; Filename: "{commonappdata}\ONKOS\Inbox"; IconFilename: "{app}\recibidos.ico"
Name: "{group}\Desinstalar ONKOS File Transfer 2.0"; Filename: "{uninstallexe}"
Name: "{userdesktop}\ONKOS File Transfer 2.0"; Filename: "{app}\ONKOSFileTransfer.exe"; IconFilename: "{app}\onkos.ico"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el &Escritorio"; GroupDescription: "Accesos directos adicionales:"

[Run]
Filename: "sc.exe"; Parameters: "create ONKOSTransferService binPath= ""{app}\Servicio\ONKOS.TransferService.exe"" start= auto DisplayName= ""ONKOS Transfer Service"""; Flags: runhidden
Filename: "sc.exe"; Parameters: "start ONKOSTransferService"; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS Transfer TCP 5050"""; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""ONKOS Transfer TCP 5050"" dir=in action=allow protocol=TCP localport=5050"; Flags: runhidden
Filename: "{app}\ONKOSFileTransfer.exe"; Description: "Abrir ONKOS File Transfer 2.0"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM ONKOSFileTransfer.exe"; Flags: runhidden; RunOnceId: "KillOnkosUi"
Filename: "taskkill.exe"; Parameters: "/F /IM ONKOS.TransferService.exe"; Flags: runhidden; RunOnceId: "KillOnkosSvc"
Filename: "sc.exe"; Parameters: "stop ONKOSTransferService"; Flags: runhidden; RunOnceId: "StopOnkosSvc"
Filename: "sc.exe"; Parameters: "delete ONKOSTransferService"; Flags: runhidden; RunOnceId: "DeleteOnkosSvc"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS Transfer TCP 5050"""; Flags: runhidden; RunOnceId: "DelFwTcp"
