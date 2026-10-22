; Inno Setup script. Built by CI (.github/workflows/release.yml) for both editions:
;   FLAVOR=""     → Mishka Helper (with Jarvis), from ..\publish
;   FLAVOR="Lite" → Mishka Helper Lite, from ..\publish-lite
; Both share one AppId, so switching editions upgrades in place.
#define AppVersion GetEnv("APP_VERSION")
#if AppVersion == ""
  #define AppVersion "0.0.0"
#endif
#define Flavor GetEnv("FLAVOR")
#if Flavor == "Lite"
  #define AppTitle "Mishka Helper Lite"
  #define PubDir "..\publish-lite"
  #define OutName "MishkaHelper-Lite-Setup-" + AppVersion
#else
  #define AppTitle "Mishka Helper"
  #define PubDir "..\publish"
  #define OutName "MishkaHelper-Setup-" + AppVersion
#endif

[Setup]
AppId={{7C1E5F3A-4B2D-4E8A-9F61-3D2A8B6C9E10}
AppName={#AppTitle}
AppVersion={#AppVersion}
AppPublisher=Mishka
DefaultDirName={autopf}\Mishka Helper
DefaultGroupName=Mishka Helper
OutputDir=..\dist
OutputBaseFilename={#OutName}
SetupIconFile=..\src\Assets\app.ico
UninstallDisplayIcon={app}\MishkaHelper.exe
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
WizardStyle=modern
CloseApplications=force
DisableDirPage=auto

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Ярлык на рабочем столе"; GroupDescription: "Дополнительно:"

[InstallDelete]
; Clean old binaries (e.g. Jarvis libraries after switching to Lite); settings/models live in AppData.
Type: filesandordirs; Name: "{app}\runtimes"
Type: files; Name: "{app}\*.dll"

[Files]
Source: "{#PubDir}\*"; Excludes: "*.pdb,*.xml"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppTitle}"; Filename: "{app}\MishkaHelper.exe"
Name: "{autodesktop}\{#AppTitle}"; Filename: "{app}\MishkaHelper.exe"; Tasks: desktopicon

[Run]
; No skipifsilent: the in-app updater runs setup with /VERYSILENT and relies on this to restart the app.
Filename: "{app}\MishkaHelper.exe"; Description: "Запустить {#AppTitle}"; Flags: nowait postinstall shellexec

[UninstallRun]
Filename: "schtasks.exe"; Parameters: "/delete /tn MishkaHelper /f"; Flags: runhidden; RunOnceId: "DelTask"
Filename: "taskkill.exe"; Parameters: "/im MishkaHelper.exe /f"; Flags: runhidden; RunOnceId: "Kill"
Filename: "taskkill.exe"; Parameters: "/im llama-server.exe /f"; Flags: runhidden; RunOnceId: "KillLlama"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Mishka Helper Remote"""; Flags: runhidden; RunOnceId: "DelFw"

[Code]
// Settings, notes and the multi-GB Jarvis models stay by default (reinstall keeps everything); ask once.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox('Удалить также настройки, заметки и скачанные модели Джарвиса?' + #13#10 +
              '(%AppData%\MishkaHelper и %LocalAppData%\MishkaHelper)', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\MishkaHelper'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\MishkaHelper'), True, True, True);
    end;
end;
