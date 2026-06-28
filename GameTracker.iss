; Inno Setup script for GameTracker
; Build with: ISCC.exe GameTracker.iss  (or just run build-installer.bat)

#define MyAppName "LazerGuanas Game Hunter"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "LazerGuanas"
#define MyAppExeName "GameTracker.exe"
#define MyPublishDir "bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{8F2A6B33-1E5C-4B6A-9D3F-9F1C5E8A2B40}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=installer
OutputBaseFilename=LazerGuanas-Game-Hunter-Setup-{#MyAppVersion}
SetupIconFile=iguana-eye.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableDirPage=no
DisableProgramGroupPage=yes
; Close the running app during a silent auto-update; we relaunch it ourselves.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";              Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}";    Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";        Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Interactive install: offer to launch from the finish page.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
; Silent install (auto-update): relaunch the app automatically.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: IsSilentRun

[Code]
function IsSilentRun(): Boolean;
begin
  Result := WizardSilent();
end;
