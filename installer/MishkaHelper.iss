; Inno Setup script. Built by CI (.github/workflows/release.yml) from ..\publish (the one build with everything).
; The parts are checkboxes: the choice goes to {app}\modules.ini, which the app reads at start.
; One AppId for all versions, so it upgrades in place - also over the old Lite / Jarvis / Secretary editions, whose contents it keeps.
#define AppVersion GetEnv("APP_VERSION")
#if AppVersion == ""
  #define AppVersion "0.0.0"
#endif
#define AppTitle "Mishka Helper"
#define PubDir "..\publish"
#define OutName "MishkaHelper-Setup-" + AppVersion
#define UninstKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\{7C1E5F3A-4B2D-4E8A-9F61-3D2A8B6C9E10}_is1"

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

[Types]
Name: "full"; Description: "Всё"
Name: "custom"; Description: "Выбрать самому"; Flags: iscustom

[Components]
Name: "tools"; Description: "Инструменты: управление ПК, ускорение, защита, колесо, виджеты, телефон"; Types: full custom; Flags: fixed
Name: "jarvis"; Description: "Джарвис — голосовой помощник и умные команды"; Types: full
Name: "secretary"; Description: "Секретарша — заметки, дела, сводки, расписание, почта"; Types: full
Name: "games"; Description: "Игровой помощник — база знаний по играм на русском"; Types: full

[Tasks]
Name: "desktopicon"; Description: "Ярлык на рабочем столе"; GroupDescription: "Дополнительно:"

[InstallDelete]
; Clean old binaries; settings/models live in the data folders.
Type: filesandordirs; Name: "{app}\runtimes"
Type: files; Name: "{app}\*.dll"

[Files]
Source: "{#PubDir}\*"; Excludes: "*.pdb,*.xml,modules.ini"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[UninstallDelete]
Type: files; Name: "{app}\modules.ini"

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
// What the copy being upgraded had: its modules.ini (chosen in the app), else the old edition by its name. '' = a fresh install.
function PreviousParts(): String;
var
  dir, name, ini: String;
  lines: TArrayOfString;
  i: Integer;
begin
  Result := '';
  if not RegQueryStringValue(HKLM64, '{#UninstKey}', 'InstallLocation', dir) then
    RegQueryStringValue(HKLM32, '{#UninstKey}', 'InstallLocation', dir);
  if not RegQueryStringValue(HKLM64, '{#UninstKey}', 'DisplayName', name) then
    RegQueryStringValue(HKLM32, '{#UninstKey}', 'DisplayName', name);
  ini := AddBackslash(dir) + 'modules.ini';
  if (dir <> '') and FileExists(ini) and LoadStringsFromFile(ini, lines) then
  begin
    Result := 'tools';
    for i := 0 to GetArrayLength(lines) - 1 do
    begin
      if Trim(lines[i]) = 'jarvis=1' then Result := Result + ',jarvis';
      if Trim(lines[i]) = 'secretary=1' then Result := Result + ',secretary';
      if Trim(lines[i]) = 'games=1' then Result := Result + ',games';
    end;
    Exit;
  end;
  // Editions before 2.0: the name said what was in it. The game helper is new: on where Jarvis was.
  if Pos('Lite', name) > 0 then Result := 'tools'
  else if Pos('Secretary', name) > 0 then Result := 'tools,secretary'
  else if Pos('Jarvis', name) > 0 then Result := 'tools,jarvis,games';
end;

procedure InitializeWizard();
var
  parts: String;
begin
  parts := PreviousParts();
  if parts <> '' then
  begin
    WizardSelectComponents('!jarvis,!secretary,!games');
    WizardSelectComponents(parts);
  end;
end;

function Flag(c: String): String;
begin
  if WizardIsComponentSelected(c) then Result := '1' else Result := '0';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveStringToFile(ExpandConstant('{app}\modules.ini'),
      'jarvis=' + Flag('jarvis') + #13#10 + 'secretary=' + Flag('secretary') + #13#10 + 'games=' + Flag('games') + #13#10, False);
end;

// Settings, notes and the multi-GB Jarvis models stay by default (reinstall keeps everything); ask once.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox('Удалить также настройки, заметки, базы игр и скачанные модели Джарвиса?' + #13#10 +
              '(%ProgramData%\MishkaHelper, %AppData%\MishkaHelper и %LocalAppData%\MishkaHelper)', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\MishkaHelper'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\MishkaHelper'), True, True, True);
      DelTree(ExpandConstant('{commonappdata}\MishkaHelper'), True, True, True);
    end;
end;
