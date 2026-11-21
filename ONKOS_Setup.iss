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
procedure StyleControlModern(Ctrl: TControl; BgColor, FgColor: TColor);
var
  I: Integer;
  SubCtrl: TControl;
begin
  if Ctrl = nil then Exit;

  if Ctrl is TWinControl then
  begin
    try
      TWinControl(Ctrl).Brush.Color := BgColor;
    except
    end;
  end;

  if Ctrl is TPanel then
  begin
    TPanel(Ctrl).Color := BgColor;
    TPanel(Ctrl).ParentBackground := False;
  end
  else if Ctrl is TNewStaticText then
  begin
    TNewStaticText(Ctrl).Font.Color := FgColor;
  end
  else if Ctrl is TLabel then
  begin
    TLabel(Ctrl).Font.Color := FgColor;
  end
  else if Ctrl is TNewCheckBox then
  begin
    TNewCheckBox(Ctrl).Font.Color := FgColor;
  end
  else if Ctrl is TNewRadioButton then
  begin
    TNewRadioButton(Ctrl).Font.Color := FgColor;
  end
  else if Ctrl is TNewEdit then
  begin
    TNewEdit(Ctrl).Color := $00231614;
    TNewEdit(Ctrl).Font.Color := FgColor;
  end;

  if Ctrl is TWinControl then
  begin
    for I := 0 to TWinControl(Ctrl).ControlCount - 1 do
    begin
      SubCtrl := TWinControl(Ctrl).Controls[I];
      StyleControlModern(SubCtrl, BgColor, FgColor);
    end;
  end;
end;

procedure ApplyModernDarkTheme();
var
  BgDark: TColor;
  BgCard: TColor;
  FgLight: TColor;
  AccentViolet: TColor;
begin
  // Colores en formato BGR de Delphi/Inno Setup (#0F101A, #181A28, #F6F7FC, #B894FF)
  BgDark := $001A100F;
  BgCard := $00281A18;
  FgLight := $00FCF7F6;
  AccentViolet := $00FF94B8;

  WizardForm.Color := BgDark;
  WizardForm.Font.Name := 'Segoe UI';
  WizardForm.Font.Color := FgLight;

  WizardForm.MainPanel.Color := BgCard;
  WizardForm.MainPanel.ParentBackground := False;

  StyleControlModern(WizardForm.MainPanel, BgCard, FgLight);
  StyleControlModern(WizardForm.InnerPage, BgDark, FgLight);
  StyleControlModern(WizardForm.WelcomePage, BgDark, FgLight);
  StyleControlModern(WizardForm.FinishedPage, BgDark, FgLight);
  StyleControlModern(WizardForm.SelectDirPage, BgDark, FgLight);
  StyleControlModern(WizardForm.SelectGroupPage, BgDark, FgLight);
  StyleControlModern(WizardForm.SelectTasksPage, BgDark, FgLight);
  StyleControlModern(WizardForm.InstallingPage, BgDark, FgLight);

  WizardForm.WelcomeLabel1.Font.Color := AccentViolet;
  WizardForm.WelcomeLabel1.Font.Size := 14;
  WizardForm.WelcomeLabel1.Font.Style := [fsBold];

  WizardForm.FinishedHeadingLabel.Font.Color := AccentViolet;
  WizardForm.FinishedHeadingLabel.Font.Size := 14;
  WizardForm.FinishedHeadingLabel.Font.Style := [fsBold];

  WizardForm.PageNameLabel.Font.Color := AccentViolet;
  WizardForm.PageNameLabel.Font.Style := [fsBold];
  WizardForm.PageDescriptionLabel.Font.Color := $00B6A098;
end;

procedure InitializeWizard();
begin
  ApplyModernDarkTheme();
end;

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
