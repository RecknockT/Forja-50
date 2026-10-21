; Compilar con Inno Setup 6. El modelo y los motores se descargan desde la app.
#ifndef AppSource
  #error AppSource debe apuntar a la publicación win-x64 autocontenida
#endif
#ifndef SetupOutput
  #define SetupOutput "..\artifacts"
#endif
#ifndef AppVersionText
  #define AppVersionText "0.2.1-dev"
#endif
[Setup]
AppId={{9AD610A8-C95C-460B-B5BD-2F45CC736DC0}
AppName=Forja 50
AppVersion={#AppVersionText}
AppPublisher=Forja 50
DefaultDirName={localappdata}\Programs\Mi IA Local
DefaultGroupName=Forja 50
UsePreviousGroup=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#SetupOutput}
OutputBaseFilename=Mi-IA-Local-{#AppVersionText}-Setup
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
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; Flags: checkedonce
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
Filename: "{app}\Mi IA Local.exe"; Description: "Abrir Forja 50"; Flags: nowait postinstall skipifsilent
; La limpieza siempre es optativa. La desinstalación silenciosa conserva los datos.
[Code]
var
  CleanupRequested: Boolean;

function InitializeUninstall: Boolean;
begin
  Result := True;
  CleanupRequested := False;
  if not UninstallSilent and DirExists(ExpandConstant('{localappdata}\MiIALocal')) then
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
