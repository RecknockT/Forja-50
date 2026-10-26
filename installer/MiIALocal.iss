; Compilar con Inno Setup 6. El modelo y los motores se descargan desde la app.
#ifndef AppSource
  #error AppSource debe apuntar a la publicación win-x64 autocontenida
#endif
#ifndef SetupOutput
  #define SetupOutput "..\artifacts"
#endif
#ifndef AppVersionText
  #define AppVersionText "0.4.20-dev"
#endif
[Setup]
AppId={{9AD610A8-C95C-460B-B5BD-2F45CC736DC0}
AppName=Forja 50
AppVersion={#AppVersionText}
AppPublisher=Forja 50
DefaultDirName={code:DefaultInstallDir}
UsePreviousAppDir=no
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
UninstallDisplayIcon={app}\Forja 50.exe
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
; Previous executable name is removed only within the selected app directory.
Type: files; Name: "{app}\Mi IA Local.exe"
Type: files; Name: "{app}\Mi IA Local.dll"
Type: files; Name: "{app}\Mi IA Local.deps.json"
Type: files; Name: "{app}\Mi IA Local.runtimeconfig.json"
Type: files; Name: "{userprograms}\Mi IA Local\Mi IA Local.lnk"
Type: dirifempty; Name: "{userprograms}\Mi IA Local"
Type: files; Name: "{autodesktop}\Mi IA Local.lnk"
[Icons]
Name: "{group}\Forja 50"; Filename: "{app}\Forja 50.exe"
Name: "{autodesktop}\Forja 50"; Filename: "{app}\Forja 50.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\Forja 50.exe"; Flags: nowait skipifsilent; Check: LaunchAfterInstall
; La limpieza siempre es optativa. La desinstalación silenciosa conserva los datos.
[Code]
var
  CleanupRequested: Boolean;
  DesktopShortcutCheck: TNewCheckBox;
  LaunchCheck: TNewCheckBox;
  DownloadHint: TNewStaticText;
  PreviousInstallDir: String;

function RegisteredInstallDir: String;
begin
  Result := '';
  RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{9AD610A8-C95C-460B-B5BD-2F45CC736DC0}_is1', 'InstallLocation', Result);
end;

function IsLegacyDefault(Dir: String): Boolean;
begin
  Result := CompareText(RemoveBackslashUnlessRoot(Dir), ExpandConstant('{localappdata}\Programs\Mi IA Local')) = 0;
end;

function DefaultInstallDir(Param: String): String;
var
  Previous: String;
begin
  Previous := RegisteredInstallDir;
  Result := ExpandConstant('{localappdata}\Programs\Forja 50');
  { Preserve custom locations; the old default is the one being renamed. }
  if (Previous <> '') and not IsLegacyDefault(Previous) then Result := Previous;
end;

procedure RegisterExtraCloseApplicationsResources;
begin
  if IsLegacyDefault(PreviousInstallDir) then
    RegisterExtraCloseApplicationsResource(False, AddBackslash(PreviousInstallDir) + 'Mi IA Local.exe');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Uninstaller: String;
  ExitCode: Integer;
begin
  if (CurStep = ssInstall) and IsLegacyDefault(PreviousInstallDir) and
    (CompareText(RemoveBackslashUnlessRoot(WizardDirValue), RemoveBackslashUnlessRoot(PreviousInstallDir)) <> 0) then
  begin
    { Restart Manager has already dealt with open program files. Silent uninstall
      preserves model data and removes only files tracked by the old installer. }
    Uninstaller := AddBackslash(PreviousInstallDir) + 'unins000.exe';
    if not FileExists(Uninstaller) then
      RaiseException('No se encontró el desinstalador anterior. Desinstalá la versión anterior conservando sus datos y volvé a instalar Forja 50.');
    if not Exec(Uninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', PreviousInstallDir, SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      RaiseException('No se pudo actualizar la carpeta anterior. Cerrá Forja 50 y volvé a intentar.');
    if ExitCode <> 0 then RaiseException('La versión anterior no pudo retirarse. Sus datos se conservaron.');
  end;
end;

procedure InitializeWizard;
begin
  PreviousInstallDir := RegisteredInstallDir;
  { Older in-app updaters pass /DIR with the old default explicitly. }
  if IsLegacyDefault(WizardDirValue) then
    WizardForm.DirEdit.Text := ExpandConstant('{localappdata}\Programs\Forja 50');
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
    Executed := Exec(ExpandConstant('{app}\Forja 50.exe'), '--uninstall-cleanup',
      ExpandConstant('{app}'), SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode);
    if (not Executed) or (ResultCode <> 0) then
      MsgBox('No se pudo completar la limpieza. Si Forja sigue abierta, cerrala y usá Administrar almacenamiento antes de desinstalar. Los datos restantes se conservan.', mbInformation, MB_OK);
  end;
end;
