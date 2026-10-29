; TaskbarFolders Inno Setup Script

#define MyAppName "TaskbarFolders"
#define MyAppVersion "0.5.1"
#define MyAppPublisher "Gian Luca Schwekendiek"
#define MyAppURL "https://github.com/gianluca-schwekendiek/taskbar-grouping"
#define MyAppExeName "TaskbarFolders.Manager.exe"

[Setup]
AppId={{B8F4E2A1-3C5D-4E6F-A7B8-9C0D1E2F3A4B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
; Per user: everything the app touches is the user's (HKCU, %APPDATA%, their Start menu and
; taskbar), and only a process running as that user can unpin tiles and clean up after it.
; {autopf} resolves to %LOCALAPPDATA%\Programs here. ADR-008.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=TaskbarFolders-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start with Windows"; GroupDescription: "Other:"

[Files]
Source: "..\publish\Manager\*"; DestDir: "{app}\Manager"; Flags: ignoreversion recursesubdirs
Source: "..\publish\Launcher\*"; DestDir: "{app}\Launcher"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName} Manager"; Filename: "{app}\Manager\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\Manager\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "TaskbarFolders"; ValueData: """{app}\Manager\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart
; Explorer's "Taskbar Groups" menu is written by the Manager at run time; the installer only
; removes it on uninstall (dontcreatekey: nothing is created here).
Root: HKCU; Subkey: "Software\Classes\exefile\shell\TaskbarFolders.Groups"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\Classes\lnkfile\shell\TaskbarFolders.Groups"; Flags: uninsdeletekey dontcreatekey

[UninstallRun]
; Unpins the group tiles and removes the menu, Start menu entries, Windows' records of the
; program and the user's data, before the files below go. The uninstaller has already asked.
Filename: "{app}\Manager\{#MyAppExeName}"; Parameters: "--uninstall --no-confirm"; Flags: waituntilterminated; RunOnceId: "TaskbarFoldersUninstall"

[UninstallDelete]
; What the Manager cannot delete while it runs: its last log lines and the runtime files .NET
; unpacks for the single-file executables.
Type: filesandordirs; Name: "{userappdata}\TaskbarFolders"
Type: filesandordirs; Name: "{%TEMP}\.net\TaskbarFolders.Manager"
Type: filesandordirs; Name: "{%TEMP}\.net\TaskbarFolders.Launcher"
Type: dirifempty; Name: "{app}"

[Run]
Filename: "{app}\Manager\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  MachineUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8F4E2A1-3C5D-4E6F-A7B8-9C0D1E2F3A4B}_is1';

// Releases up to 0.4.x installed for all users under Program Files. A per-user setup can
// neither replace nor remove that copy, and two copies would fight over the same pins.
function InitializeSetup(): Boolean;
begin
  Result := True;
  if RegKeyExists(HKLM32, MachineUninstallKey) or (IsWin64 and RegKeyExists(HKLM64, MachineUninstallKey)) then
  begin
    MsgBox('An earlier version of TaskbarFolders is installed for all users.' + #13#10#13#10 +
      'Uninstall it first under Settings > Apps > Installed apps, then run this setup again.',
      mbError, MB_OK);
    Result := False;
  end;
end;
