; Hyprism installer (Inno Setup 6). Built by ..\build.ps1:  ISCC /DArch=x64 Hyprism.iss
;
; - Per-user install (no admin): %LocalAppData%\Programs\Hyprism. Admins can choose "all users" in the first page.
; - Upgrades in place: running a newer setup over an older one closes Hyprism, replaces the files, keeps your
;   settings/profiles (%AppData%\Hyprism) and starts Hyprism again. The in-app updater runs it with /SILENT /UPDATE=1.
; - Uninstall runs `Hyprism.exe --uninstall` first, which removes Hyprism's sign-in entries, unregisters its Explorer
;   extension and deletes downloaded tools, and optionally restores the original Windows look and deletes settings.

#ifndef Arch
  #define Arch "x64"
#endif
#define SourceDir "..\publish\" + Arch
#define AppExe "Hyprism.exe"
#define AppName "Hyprism"
; Version comes from the built exe, so the installer can never disagree with the app.
#define AppVersion GetVersionNumbersString(SourceDir + "\" + AppExe)
#define AppVersionShort Copy(AppVersion, 1, RPos(".", AppVersion) - 1)

[Setup]
AppId={{6E0D8A3B-5B7E-4E9C-9A55-3F1C2B7D9E41}
AppName={#AppName}
AppVersion={#AppVersionShort}
AppVerName={#AppName} {#AppVersionShort}
AppPublisher=Hyprism contributors
AppComments=Hyprland vibes for Windows.
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed={#Arch}compatible
ArchitecturesInstallIn64BitMode={#Arch}compatible
MinVersion=10.0.19045
OutputDir=Output
OutputBaseFilename=Hyprism-Setup-{#AppVersionShort}-{#Arch}
SetupIconFile=..\src\Hyprism.App\Assets\Hyprism.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Privacy policy (+ MIT license) on a page that must be accepted before installing.
LicenseFile=PrivacyPolicy.txt
; We close Hyprism ourselves (PrepareToInstall); it lives in the tray, so Restart Manager can't ask it nicely.
CloseApplications=no
RestartApplications=no
SetupLogging=yes

[Messages]
WizardLicense=Privacy Policy
LicenseLabel=Please read Hyprism's Privacy Policy and license before continuing.
LicenseLabel3=Hyprism collects no personal data and has no telemetry. You must accept the Privacy Policy and license to install Hyprism.
LicenseAccepted=I &accept the Privacy Policy and license
LicenseNotAccepted=I &do not accept

[Tasks]
Name: "startup"; Description: "Start Hyprism with Windows (in the tray)"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Files from older versions that no longer ship (the publish folder is copied whole each release).
Type: filesandordirs; Name: "{app}\*.pdb"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\THIRD_PARTY_LICENSES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "PrivacyPolicy.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Hyprism"; ValueData: """{app}\{#AppExe}"" --tray"; Tasks: startup; Flags: uninsdeletevalue

[Run]
; Interactive install: offer to launch.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; Silent update from the in-app updater: always relaunch.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: IsUpdate

[UninstallDelete]
; Anything the app wrote next to itself (logs, caches). Settings in %AppData% are handled by --uninstall.
Type: filesandordirs; Name: "{app}"

[Code]
function IsUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

procedure StopHyprism;
var
  Code: Integer;
begin
  { Hyprism is a tray app: close it so its files can be replaced or removed. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExe} /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(800);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopHyprism;
  Result := '';
end;

function InitializeUninstall: Boolean;
var
  Args: String;
  Code: Integer;
  Exe: String;
begin
  Result := True;
  StopHyprism;
  Args := '--uninstall';
  if not UninstallSilent then
  begin
    if MsgBox('Restore your original Windows look?' + #13#10#13#10 +
              'Hyprism will stop the apps it manages and put back your original wallpaper, accent color and the ' +
              'config files it changed.' + #13#10#13#10 +
              'Apps it installed (TranslucentTB, Lively, ...) stay installed; remove them in Settings > Apps if you want.',
              mbConfirmation, MB_YESNO) = IDYES then
      Args := Args + ' --restore';
    if MsgBox('Also delete your Hyprism settings, profiles and backups?' + #13#10#13#10 +
              'Choose No to keep them for a future reinstall.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      Args := Args + ' --purge';
  end;
  { unins000.exe /DRYRUN=1 only logs what cleanup would do (%TEMP%\hyprism-uninstall.log): for testing. }
  if ExpandConstant('{param:DRYRUN|0}') = '1' then
    Args := Args + ' --dry-run';
  { Runs before any file is removed, while Hyprism.exe and the module code still exist. }
  Exe := ExpandConstant('{app}\{#AppExe}');
  if FileExists(Exe) then
    Exec(Exe, Args, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code);
end;
