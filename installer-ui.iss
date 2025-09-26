; Redis Stream Inspector — UI Installer (Inno Setup 6.x)
; Save as: installer\RedisStreamInspector.UI.iss

#define MyAppName       "Redis Stream Inspector"
#define MyAppVersion    "0.4.0"
#define MyPublisher     "Cristiano Guilloux"
#define MyExeName       "RedisInspector.UI.exe"
; This must point to the folder from your 'dotnet publish' step:
#define PublishDir      ".\publish\ui\win-x64"

[Setup]
AppId={{3B8D6C50-6F5E-4F9A-9C0F-9E88D5C9B0B1} 
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=.\output
OutputBaseFilename=RedisStreamInspector-UI-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
; If you only support 64-bit Windows, uncomment the next line:
; ArchitecturesAllowed=x64
PrivilegesRequired=admin
ChangesEnvironment=yes
DisableDirPage=no
DisableProgramGroupPage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &Desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Install everything published by dotnet into {app}
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
; Start Menu
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyExeName}"; WorkingDir: "{app}"
; Optional Desktop shortcut
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Offer to launch after install
Filename: "{app}\{#MyExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

; Optional: add an uninstaller entry icon (uses the EXE’s icon)
[Setup]
UninstallDisplayIcon={app}\{#MyExeName}
