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
; Not offered at all on an unsigned build, where there is no certificate to trust.
#ifdef IncludeSigningCertificate
Name: "trustcertificate"; Description: "Trust the signing certificate on this computer (removes the unknown-publisher warning)"; GroupDescription: "Optional:"
#endif
Name: "startwithwindows"; Description: "Start MacRando when Windows starts"; GroupDescription: "Optional:"; Flags: unchecked

[Files]
Source: "bin\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\MacRando.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\MacRandoTray.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion
; The public certificate, and the script that installs it, are only part of a signed
; build. package.ps1 passes /DIncludeSigningCertificate when it exported a certificate from
; a signed executable, and omits it otherwise.
;
; A compile-time conditional rather than a Check: function, because Inno Setup validates
; that every [Files] source exists while compiling, before any Check is evaluated. Listing
; the certificate unconditionally therefore made the installer impossible to compile
; whenever the build was unsigned, which is every GitHub-hosted run, since the signing
; certificate does not exist there. The Release workflow therefore failed on all 33 of its
; runs and never produced a release. The unsigned build is the entire purpose of that
; workflow, so the certificate has to be genuinely optional rather than conditionally
; copied at install time.
#ifdef IncludeSigningCertificate
Source: "dist\MacRando-Public.cer"; DestDir: "{app}"; Flags: ignoreversion
Source: "trust-certificate.ps1"; DestDir: "{app}"; Flags: ignoreversion
#endif

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Nothing is written here on purpose.
;
; MacRando's manifest requires administrator, and Explorer cannot elevate a Run key
; entry: it asks for the process, the elevation cannot be satisfied from that path, and
; the launch produces nothing. Windows records the attempt and its completion in the same
; second with PID 0, meaning no process was ever created. On the machine this was found on
; that was 10 attempts across 3 days, every one of them silent, with no error dialog and
; nothing in MacRando's log because the application never started to write one.
;
; A Run value was therefore never a usable registration for this application. Start with
; Windows is a scheduled task registered at logon with the highest run level, created
; below when the option is ticked and removed on uninstall. The application registers the
; same task from its tray menu, so the two paths agree on the task name.

[Run]
; shellexec is required, not a preference. MacRando.exe embeds
; requestedExecutionLevel="requireAdministrator", and a postinstall entry runs after the
; wizard closes, by which point Setup has dropped back to the signed-in user's token. The
; default launcher is CreateProcess, which cannot start a process whose manifest asks for
; a higher integrity level, so it failed with "CreateProcess failed; code 740. The
; requested operation requires elevation" and the user was met with an error instead of
; the application. ShellExecute honours the manifest and raises the usual prompt.
; Nothing tested this: no test runs the installer, and a silent install skips postinstall
; entries entirely, so the failure only ever appeared for a person installing interactively.
Filename: "{app}\{#AppExeName}"; Description: "Start {#AppName}"; \
    Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; Remove the logon task. Leaving it behind would launch an executable that is no longer
; installed, at every logon, silently.
;
; schtasks rather than PowerShell: an Inno parameter value containing braces is read as a
; constant reference, because { } is how Inno writes constants. A PowerShell one-liner with
; try and catch in it does not survive that, and it failed to compile with "Unknown
; constant" until it was replaced.
Filename: "schtasks.exe"; Parameters: "/Delete /TN MacRando /F"; \
    Flags: runhidden; RunOnceId: "RemoveLogonTask"
; Also remove the Run value, because a machine upgrading from an earlier version still has
; one. It never worked, but it would otherwise keep appearing in startup diagnostics as
; though it were doing something.
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
  TaskScript: String;
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('startwithwindows') then
    begin
      { A scheduled task at logon with the highest run level, rather than a Run key value.
        Explorer cannot elevate a Run key entry, so the Run value never worked for an
        application that requires administrator. The task is registered for the account
        running Setup, which is the person whose session will log on.

        The path is passed through an environment variable rather than written into the
        script, because the installation directory is user-visible and may contain spaces
        or a quote.

        Every statement is a single complete literal. Splitting one statement across two
        literals with a trailing '+' does work, but Pascal concatenates with no separator,
        so a missing space in the continuation silently splits a PowerShell command in
        two. That cannot happen when each literal is a whole statement, and it lets the
        script be checked by reading each line rather than by reconstructing it. }
      TaskScript :=
        '$ErrorActionPreference = ''Stop'';' + #13#10 +
        '$action = New-ScheduledTaskAction -Execute $env:MR_EXE -Argument ''--startup'';' + #13#10 +
        '$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME;' + #13#10 +
        '$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest;' + #13#10 +
        '$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew;' + #13#10 +
        'Register-ScheduledTask -TaskName ''MacRando'' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null;';
      Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
           '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' + TaskScript + '"',
           '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      if ResultCode <> 0 then
      begin
        MsgBox('MacRando could not be set to start when Windows starts (error ' +
               IntToStr(ResultCode) + '). You can turn this on later from the tray menu.' + #13#10 + #13#10 +
               'It has to be a scheduled task rather than a startup entry, because MacRando ' +
               'requires administrator and Windows cannot elevate a startup entry.',
               mbError, MB_OK);
      end;
    end;

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
