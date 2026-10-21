; Compilar con Inno Setup 6. El modelo y los motores se descargan desde la app.
#ifndef AppSource
  #error AppSource debe apuntar a la publicación win-x64 autocontenida
#endif
#ifndef SetupOutput
  #define SetupOutput "..\artifacts"
#endif
#ifndef AppVersionText
  #define AppVersionText "0.3.3-dev"
#endif
[Setup]
AppId={{9AD610A8-C95C-460B-B5BD-2F45CC736DC0}
AppName=Forja 50
AppVersion={#AppVersionText}
AppPublisher=Forja 50
DefaultDirName={localappdata}\Programs\Forja 50
UsePreviousAppDir=yes
DefaultGroupName=Forja 50
UsePreviousGroup=no
UsePreviousTasks=no
DisableWelcomePage=yes
DisableDirPage=no
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#SetupOutput}
OutputBaseFilename=Forja-50-{#AppVersionText}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Mi IA Local.exe
CloseApplications=yes
SetupLogging=yes
SetupIconFile=..\src\MiIALocal.Desktop\Assets\app.ico
[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"
[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Excludes: "portable.flag,datos\*"; Flags: ignoreversion recursesubdirs createallsubdirs
[InstallDelete]
Type: files; Name: "{userprograms}\Mi IA Local\Mi IA Local.lnk"
Type: dirifempty; Name: "{userprograms}\Mi IA Local"
Type: files; Name: "{autodesktop}\Mi IA Local.lnk"
[Icons]
Name: "{group}\Forja 50"; Filename: "{app}\Mi IA Local.exe"
Name: "{autodesktop}\Forja 50"; Filename: "{app}\Mi IA Local.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\Mi IA Local.exe"; Flags: nowait skipifsilent; Check: LaunchAfterInstall
; La limpieza siempre es optativa. La desinstalación silenciosa conserva los datos.
[Code]
var
  CleanupRequested: Boolean;
  DesktopShortcutCheck: TNewCheckBox;
  LaunchCheck: TNewCheckBox;
  DownloadHint: TNewStaticText;

procedure InitializeWizard;
begin
  DesktopShortcutCheck := TNewCheckBox.Create(WizardForm);
  DesktopShortcutCheck.Parent := WizardForm.SelectDirPage;
  DesktopShortcutCheck.Left := WizardForm.DirEdit.Left;
  DesktopShortcutCheck.Top := WizardForm.DirEdit.Top + WizardForm.DirEdit.Height + ScaleY(24);
  DesktopShortcutCheck.Width := WizardForm.DirEdit.Width + WizardForm.DirBrowseButton.Width;
  DesktopShortcutCheck.Height := ScaleY(22);
  DesktopShortcutCheck.Caption := 'Crear un acceso directo en el escritorio';
  DesktopShortcutCheck.Checked := True;
  DesktopShortcutCheck.TabOrder := WizardForm.DirBrowseButton.TabOrder + 1;

  LaunchCheck := TNewCheckBox.Create(WizardForm);
  LaunchCheck.Parent := WizardForm.SelectDirPage;
  LaunchCheck.Left := DesktopShortcutCheck.Left;
  LaunchCheck.Top := DesktopShortcutCheck.Top + ScaleY(28);
  LaunchCheck.Width := DesktopShortcutCheck.Width;
  LaunchCheck.Height := ScaleY(22);
  LaunchCheck.Caption := 'Abrir Forja al terminar';
  LaunchCheck.Checked := True;
  LaunchCheck.TabOrder := DesktopShortcutCheck.TabOrder + 1;

  DownloadHint := TNewStaticText.Create(WizardForm);
  DownloadHint.Parent := WizardForm.SelectDirPage;
  DownloadHint.Left := DesktopShortcutCheck.Left;
  DownloadHint.Top := LaunchCheck.Top + ScaleY(32);
  DownloadHint.Width := DesktopShortcutCheck.Width;
  DownloadHint.Height := ScaleY(44);
  DownloadHint.WordWrap := True;
  DownloadHint.Caption := 'El modelo de IA se descarga después desde Forja. Podés elegir otra carpeta para guardarlo; no se descargará ni iniciará automáticamente.';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := PageID = wpSelectTasks;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  if CurPageID = wpSelectDir then
  begin
    if DesktopShortcutCheck.Checked then
      WizardSelectTasks('desktopicon')
    else
      WizardSelectTasks('!desktopicon');
  end;
  Result := True;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpSelectDir then
  begin
    WizardForm.PageNameLabel.Caption := 'Instalar Forja 50';
    WizardForm.PageDescriptionLabel.Caption := 'Elegí la carpeta y las opciones de instalación.';
    WizardForm.SelectDirBrowseLabel.Caption := 'Podés cambiar la carpeta con Examinar. Cuando esté todo listo, pulsá Instalar.';
    WizardForm.NextButton.Caption := '&Instalar';
  end;
end;

function LaunchAfterInstall: Boolean;
begin
  Result := not WizardSilent and LaunchCheck.Checked;
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  CleanupRequested := False;
  if not UninstallSilent and (DirExists(ExpandConstant('{localappdata}\Forja 50')) or
    DirExists(ExpandConstant('{localappdata}\MiIALocal'))) then
    CleanupRequested := MsgBox(
      '¿Querés eliminar también los datos descargados por Forja?' + #13#10#13#10 +
      'Se abrirá Administrar almacenamiento para elegir modelos, motores, caché, conversaciones y ajustes.' + #13#10 +
      'Los archivos importados y los repositorios están protegidos. Podés cerrar esa ventana para conservar los datos.' + #13#10#13#10 +
      'Si elegís No, se conservarán todos los datos de Forja.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  Executed: Boolean;
begin
  if (CurUninstallStep = usUninstall) and CleanupRequested then
  begin
    Executed := Exec(ExpandConstant('{app}\Mi IA Local.exe'), '--uninstall-cleanup',
      ExpandConstant('{app}'), SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode);
    if (not Executed) or (ResultCode <> 0) then
      MsgBox('No se pudo completar la limpieza. Si Forja sigue abierta, cerrala y usá Administrar almacenamiento antes de desinstalar. Los datos restantes se conservan.', mbInformation, MB_OK);
  end;
end;
