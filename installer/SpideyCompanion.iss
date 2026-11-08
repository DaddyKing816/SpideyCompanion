; ============================================================================
;  Spidey Companion - Inno Setup installer script (v1.3.0)
;
;  Per-user install (no admin):  %LOCALAPPDATA%\SpideyCompanion
;  - matches the self-updater, which swaps the exe in that folder
;  - uninstallable from Windows Settings > Apps like a normal app
;  - Start Menu shortcut + start-with-Windows (optional task, default on)
;
;  HOW TO BUILD THE INSTALLER
;  This script is compiled on WINDOWS (Inno Setup is Windows-only):
;    1. Build the app:   dotnet build src -c Release -o build-out
;       then rename the outputs to the versioned names this script
;       expects:  SpideyCompanion-v1.3.0.exe  (+ the same for .config)
;       (this script reads the exe + config from ..\build-out, docs from
;        ..\package, sources from ..\src, relative to its own folder;
;        the GitHub Action does the rename automatically)
;    2. Compile:         "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\SpideyCompanion.iss
;       Output: installer\Output\SpideyCompanion-Setup-v1.4.0.exe
;  OR push a v* tag / run the "Build installer" GitHub Action in this
;  repo (.github/workflows/build-installer.yml) and download the result.
;
;  SmartScreen note: an unsigned installer still shows the Windows
;  "unknown publisher" prompt. Only a paid code-signing certificate
;  removes that - there is no script-side workaround.
; ============================================================================

#define AppName "Spidey Companion"
#define AppVersion "1.3.0"
#define ExeName "SpideyCompanion-v1.3.0.exe"
#define StageDir "..\build-out"
#define PkgDir "..\package"
#define SrcDir "..\src"

[Setup]
; AppId is this product's permanent identity - NEVER change it, or
; upgrades/uninstalls will stop matching earlier installs.
AppId={{0133FFD4-30BD-421B-BB17-87FA57E877C5}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Bailey Powell
DefaultDirName={localappdata}\SpideyCompanion
DefaultGroupName=Spidey Companion
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=SpideyCompanion-Setup-v{#AppVersion}
UninstallDisplayIcon={app}\{#ExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartIfNeededByRun=no

[Tasks]
Name: "autostart"; Description: "Start Spidey Companion automatically with Windows"; GroupDescription: "Startup:"; Flags: checkedonce

[InstallDelete]
; Clear superseded versioned exes from earlier installs (bat or setup)
; so the folder always holds exactly one companion build.
Type: files; Name: "{app}\SpideyCompanion-v*.exe"
Type: files; Name: "{app}\SpideyCompanion-v*.exe.config"
Type: files; Name: "{app}\SpiderCompanion.exe"
Type: filesandordirs; Name: "{localappdata}\SpiderCompanion"

[Files]
Source: "{#StageDir}\{#ExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\{#ExeName}.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PkgDir}\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PkgDir}\update-feed.txt"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
; Full source ships with the app, as the README promises.
Source: "{#SrcDir}\Program.cs"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\Buddy.cs"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\

[Icons]
Name: "{group}\Spidey Companion"; Filename: "{app}\{#ExeName}"
Name: "{group}\Read Me"; Filename: "{app}\README.txt"

[Registry]
; The self-updater reads this Run value back to find the exe name -
; keep the value name and quoted-path format exactly as they are.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SpideyCompanion"; ValueData: """{app}\{#ExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#ExeName}"; Description: "Start Spidey Companion now"; Flags: nowait postinstall skipifsilent

[Code]
{ Belt and braces alongside CloseApplications: make sure no older
  companion build is still running before files are replaced, and
  stop the app before uninstall removes its files. }
procedure StopCompanion;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM "SpideyCompanion*.exe" /F', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM SpiderCompanion.exe /F', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup(): Boolean;
begin
  StopCompanion;
  Result := True;
end;

function InitializeUninstall(): Boolean;
begin
  StopCompanion;
  Result := True;
end;
