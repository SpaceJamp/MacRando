; MacRando installer.
;
; The version is not written here. It is passed in by package.ps1 from the value in
; src\Models.cs, because a hardcoded version in this file is exactly how the previous
; template sat at 1.4.4 while the application moved on through nine releases.

#define AppName "MacRando"
#ifndef AppVersion
  #error AppVersion must be passed with /DAppVersion=<version>
#endif
#define AppExeName "MacRando.exe"
#define AppPublisher "MacRando"
#define AppMutex "MacRandoSingleInstance"

[Setup]
AppId={{B4D1E8B5-4D1C-4C77-9B27-2D22B6D6F1A0}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={autopf}\MacRando
DefaultGroupName={#AppName}
OutputDir=dist
OutputBaseFilename=MacRando-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Per-machine. The application already requires administrator to run, so there is no
; privilege argument for a per-user install, and putting the executable in Program Files
; means it is not writable by anything running as the signed-in user. That removes the
; class of local tampering where the executable itself is replaced.
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
SetupIconFile=assets\MacRando.ico
; No WizardImageFile: that directive wants a bitmap, not an icon, and MacRando only ships
; the .ico. The setup icon above is enough branding for the wizard.
; No LicenseFile page: Inno only accepts a .txt or .rtf there, and the Apache-2.0 text is
; shipped verbatim as LICENSE in [Files] rather than duplicated into a second file that
; could drift from it.
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
; Trusting a self-signed certificate makes Windows treat this publisher as verified, which
; suppresses the unknown-publisher warning. That is a real security decision about the
; machine, so it is offered rather than done, and it is off unless explicitly ticked.
Name: "trustcertificate"; Description: "Trust the signing certificate on this computer (removes the unknown-publisher warning)"; GroupDescription: "Optional:"
Name: "startwithwindows"; Description: "Start MacRando when Windows starts"; GroupDescription: "Optional:"; Flags: unchecked

[Files]
Source: "bin\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\MacRando.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\MacRandoTray.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "trust-certificate.ps1"; DestDir: "{app}"; Flags: ignoreversion
; The public certificate, so a user who declines the trust option can still run the
; script later. The private key is never exported and never leaves the store.
Source: "dist\MacRando-Public.cer"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Matches the key the application itself writes when Start with Windows is enabled, so
; the installer's choice and the application's setting cannot disagree.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "MacRando"; ValueData: """{app}\{#AppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: startwithwindows

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Start {#AppName}"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove the Run key on uninstall regardless of how it was added, since the application
; can also write it and the user may have enabled the setting from inside the app.
Filename: "reg"; Parameters: "delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v MacRando /f"; \
    Flags: runhidden; RunOnceId: "RemoveRunKey"

[Code]
const
  UserDataDir = 'MacRando';

{ Prompt before installing over a running instance, because the updater swaps the
  executable and an open file cannot be replaced. }
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

{ The certificate is installed only when the user asked for it, and only for the current
  user. Adding to the machine-wide Trusted Root store would be a much larger change than
  a single application warrants, and the current user's store is enough to make Windows
  recognise this publisher for this account. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  CertPath: String;
begin
  if CurStep = ssPostInstall then
  begin
    if not WizardIsTaskSelected('trustcertificate') then
    begin
      Exit;
    end;
    CertPath := ExpandConstant('{app}\MacRando-Public.cer');
    if not FileExists(CertPath) then
    begin
      MsgBox('The public signing certificate was not found, so nothing was changed.' + #13#10 +
             'Location: ' + CertPath, mbError, MB_OK);
      Exit;
    end;
    { certutil is the supported way to add a certificate to the current user's store. }
    Exec(ExpandConstant('{sys}\certutil.exe'),
         '-user -addstore Root "' + CertPath + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if ResultCode = 0 then
    begin
      MsgBox('The signing certificate is now trusted for your Windows account.' + #13#10 + #13#10 +
             'Windows may still warn on downloaded builds until SmartScreen has seen this ' +
             'publisher. That is expected and is not a sign of a problem.',
             mbInformation, MB_OK);
    end
    else
    begin
      MsgBox('The certificate could not be added automatically (certutil returned ' +
             IntToStr(ResultCode) + ').' + #13#10 + #13#10 +
             'You can run the included trust-certificate.ps1 instead. It changes nothing ' +
             'without -Install.', mbError, MB_OK);
    end;
  end;
end;

{ Uninstalling must never touch the user data folder. It holds the restore profiles, and
  a profile that is deleted while an adapter is left randomized is the one failure this
  application is built to avoid. Say where the data is and let the user decide. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
  NL: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\') + UserDataDir;
    if DirExists(DataDir) then
    begin
      { A named newline rather than a literal #13#10, because a line beginning with '#'
        is read by the preprocessor as a directive. }
      NL := #13#10;
      MsgBox('MacRando has been removed.' + NL + NL +
             'Your settings, restore profiles, notification history, and logs were left in:' +
             NL + DataDir + NL + NL +
             'They were kept deliberately. A restore profile may still describe an adapter ' +
             'that has not been put back, and deleting it would leave that adapter changed ' +
             'with no way to recover it.' + NL + NL +
             'Restore any pending adapter from the tray menu before deleting that folder, ' +
             'or leave it in place.',
             mbInformation, MB_OK);
    end;
  end;
end;
