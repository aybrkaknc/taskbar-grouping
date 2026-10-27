; TaskbarFolders Inno Setup Script

#define MyAppName "TaskbarFolders"
#define MyAppVersion "0.4.10"
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

[Run]
Filename: "{app}\Manager\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
