#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define AppName "Codex Usage Companion"
#define AppExeName "CodexUsageCompanion.App.exe"
#define AppId "{{D999B8D7-59CE-4A77-AC33-709759688782}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Codex Usage Companion contributors
DefaultDirName={localappdata}\Programs\Codex Usage Companion
DefaultGroupName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename=CodexUsageCompanion-{#AppVersion}-win-x64-setup
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#AppExeName}
SetupLogging=yes
WizardStyle=modern
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription={#AppName} installer

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; AppUserModelID: "CodexUsageCompanion.Desktop"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Intentionally empty: user settings, encrypted credentials, deduplication
; state, queued sync, and history live outside {app} and are preserved.

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExistingExe: String;
  ResultCode: Integer;
begin
  Result := '';
  ExistingExe := ExpandConstant('{app}\{#AppExeName}');
  if FileExists(ExistingExe) then
  begin
    if not Exec(
      ExistingExe,
      '--prepare-upgrade "{#AppVersion}"',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode
    ) or (ResultCode <> 0) then
    begin
      Result :=
        'Codex Usage Companion could not validate and back up the current settings. ' +
        'The upgrade was stopped without replacing the installed files.';
    end;
  end;
end;
