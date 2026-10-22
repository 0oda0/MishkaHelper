; Inno Setup script. Built by CI (.github/workflows/release.yml) from the published single-file exe.
#define AppVersion GetEnv("APP_VERSION")
#if AppVersion == ""
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{7C1E5F3A-4B2D-4E8A-9F61-3D2A8B6C9E10}
AppName=Mishka Helper
AppVersion={#AppVersion}
AppPublisher=Mishka
DefaultDirName={autopf}\Mishka Helper
DefaultGroupName=Mishka Helper
OutputDir=..\dist
OutputBaseFilename=MishkaHelper-Setup-{#AppVersion}
SetupIconFile=..\src\Assets\app.ico
UninstallDisplayIcon={app}\MishkaHelper.exe
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
WizardStyle=modern

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Ярлык на рабочем столе"; GroupDescription: "Дополнительно:"

[Files]
Source: "..\publish\MishkaHelper.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Mishka Helper"; Filename: "{app}\MishkaHelper.exe"
Name: "{autodesktop}\Mishka Helper"; Filename: "{app}\MishkaHelper.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MishkaHelper.exe"; Description: "Запустить Mishka Helper"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
Filename: "schtasks.exe"; Parameters: "/delete /tn MishkaHelper /f"; Flags: runhidden; RunOnceId: "DelTask"
Filename: "taskkill.exe"; Parameters: "/im MishkaHelper.exe /f"; Flags: runhidden; RunOnceId: "Kill"
