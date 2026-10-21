; Compilar con Inno Setup 6. El modelo y los motores se descargan desde la app.
#ifndef AppSource
  #error AppSource debe apuntar a la publicación win-x64 autocontenida
#endif
#ifndef SetupOutput
  #define SetupOutput "..\artifacts"
#endif
#ifndef AppVersionText
  #define AppVersionText "0.1.5-dev"
#endif
[Setup]
AppId={{9AD610A8-C95C-460B-B5BD-2F45CC736DC0}
AppName=Mi IA Local
AppVersion={#AppVersionText}
AppPublisher=Mi IA Local
DefaultDirName={localappdata}\Programs\Mi IA Local
DefaultGroupName=Mi IA Local
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
[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; Flags: checkedonce
[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Excludes: "portable.flag,datos\*"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Mi IA Local"; Filename: "{app}\Mi IA Local.exe"
Name: "{autodesktop}\Mi IA Local"; Filename: "{app}\Mi IA Local.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\Mi IA Local.exe"; Description: "Abrir Mi IA Local"; Flags: nowait postinstall skipifsilent
; Los datos de LocalAppData\MiIALocal se conservan al desinstalar.
