#define MyAppName "ONKOS File Transfer"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "Clínica ONKOS"
#define MyAppExeName "ONKOSFileTransfer.exe"
#define MyServiceExeName "ONKOS.TransferService.exe"
#define MyServiceName "ONKOSTransferService"

[Setup]
AppId={{7B6D9A12-3C8F-4E5B-9A21-6D1F8E4C2B90}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\ONKOS\FileTransfer
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=InstallerOutput
OutputBaseFilename=ONKOSFileTransfer_Setup_{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el Escritorio"; GroupDescription: "Accesos directos:"

[Files]
Source: "ONKOSFileTransfer\bin\Release\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "ONKOSFileTransfer\bin\Release\*.config"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "ONKOSFileTransfer\bin\Release\onkos.ico"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "ONKOSFileTransfer\bin\Release\recibidos.ico"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "ONKOSTransferService\bin\Release\{#MyServiceExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "ONKOSTransferService\bin\Release\*.config"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\onkos.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\onkos.ico"; Tasks: desktopicon

[Registry]
; Inicia la bandeja (--tray) automáticamente para TODOS los usuarios al iniciar sesión
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ONKOSFileTransferTray"; ValueData: """{app}\{#MyAppExeName}"" --tray"; Flags: uninsdeletevalue

[Run]
; Detener procesos previos si existen
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; StatusMsg: "Deteniendo instancias previas..."
Filename: "sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden; StatusMsg: "Deteniendo servicio previo..."
Filename: "sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden

; Registrar e iniciar el servicio de Windows con reinicio automático ante fallos
Filename: "sc.exe"; Parameters: "create {#MyServiceName} binPath= ""\""{app}\{#MyServiceExeName}\"""" start= auto DisplayName= ""ONKOS Transfer Service"""; Flags: runhidden; StatusMsg: "Instalando servicio ONKOS..."
Filename: "sc.exe"; Parameters: "failure {#MyServiceName} reset= 86400 actions= restart/5000/restart/15000/restart/30000"; Flags: runhidden
Filename: "sc.exe"; Parameters: "start {#MyServiceName}"; Flags: runhidden; StatusMsg: "Iniciando servicio ONKOS..."

; Reglas de Firewall (TCP 5050 transferencias y UDP 5051 descubrimiento)
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS File Transfer TCP 5050"""; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""ONKOS File Transfer TCP 5050"" dir=in action=allow protocol=TCP localport=5050"; Flags: runhidden; StatusMsg: "Configurando Firewall..."
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS File Transfer UDP 5051"""; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""ONKOS File Transfer UDP 5051"" dir=in action=allow protocol=UDP localport=5051"; Flags: runhidden

; Iniciar la bandeja en segundo plano al terminar la instalación
Filename: "{app}\{#MyAppExeName}"; Parameters: "--tray"; Flags: nowait postinstall skipifsilent; Description: "Iniciar ONKOS en segundo plano (bandeja del sistema)"

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"
Filename: "sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden; RunOnceId: "StopSvc"
Filename: "sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden; RunOnceId: "DelSvc"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS File Transfer TCP 5050"""; Flags: runhidden; RunOnceId: "DelFwTcp"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ONKOS File Transfer UDP 5051"""; Flags: runhidden; RunOnceId: "DelFwUdp"
