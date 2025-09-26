; Redis Stream Inspector – Windows installer (Inno Setup)
; - Installs to Program Files
; - Adds {app} to system PATH (appends, avoids duplicates)
; - Removes from PATH on uninstall
; Requires admin.

#define AppName "Redis Stream Inspector"
#define AppExe  "redis-inspector.exe"
#define AppVer  "0.3.2"

[Setup]
AppId={{F5E9BDB9-97F7-4F0F-8E6F-2A11B2E28C9C}
AppName={#AppName}
AppVersion={#AppVer}
AppPublisher=Cristiano Guilloux
DefaultDirName={pf}\Redis Stream Inspector
DefaultGroupName={#AppName}
OutputDir=dist
OutputBaseFilename=RedisStreamInspector-{#AppVer}-Setup
Compression=lzma
SolidCompression=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
ChangesEnvironment=yes

[Files]
; Copy the published single-file EXE
Source: "src\RedisInspector.CLI\publish\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Optional start menu shortcut
Name: "{group}\{#AppName} (Shell)"; Filename: "cmd.exe"; Parameters: "/K ""{app}\{#AppExe} --help"""

[Tasks]
Name: "addtopath"; Description: "Add installation folder to the system PATH"; Flags: checkedonce

[Code]
const
  PathKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';

function DirInPath(const Dir, Path: string): Boolean;
var
  UDir, UPath: string;
begin
  UDir := ';' + UpperCase(Dir) + ';';
  UPath := ';' + UpperCase(Path) + ';';
  Result := Pos(UDir, UPath) > 0;
end;

procedure AddToSystemPath(AppDir: string);
var
  PathVal: string;
begin
  if not RegQueryStringValue(HKLM, PathKey, 'Path', PathVal) then PathVal := '';
  if DirInPath(AppDir, PathVal) then Exit;
  if (PathVal <> '') and (PathVal[Length(PathVal)] <> ';') then PathVal := PathVal + ';';
  PathVal := PathVal + AppDir;
  RegWriteStringValue(HKLM, PathKey, 'Path', PathVal);
end;

procedure RemoveFromSystemPath(AppDir: string);
var
  PathVal, UPath, UApp: string;
begin
  if not RegQueryStringValue(HKLM, PathKey, 'Path', PathVal) then Exit;
  UPath := ';' + UpperCase(PathVal) + ';';
  UApp  := ';' + UpperCase(AppDir) + ';';
  StringChangeEx(UPath, UApp, ';', True);
  if (Length(UPath) > 0) and (UPath[1] = ';') then Delete(UPath, 1, 1);
  if (Length(UPath) > 0) and (UPath[Length(UPath)] = ';') then Delete(UPath, Length(UPath), 1);
  RegWriteStringValue(HKLM, PathKey, 'Path', UPath);
end;

//procedure NotifyEnvChanged;
//begin
//  // Broadcast environment change so new PATH is picked up by new consoles
//  SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, 'Environment',
//    SMTO_ABORTIFHUNG, 5000, nil);
//end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then
  begin
    AddToSystemPath(ExpandConstant('{app}'));
    // NotifyEnvChanged;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RemoveFromSystemPath(ExpandConstant('{app}'));
    //NotifyEnvChanged;
  end;
end;
