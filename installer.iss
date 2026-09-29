; Optional Inno Setup template for a signed release build.
; Requires Inno Setup 6+ and a separately code-signed MacRando.exe.

#define AppName "MacRando"
#define AppVersion "1.3.0"
#define AppExeName "MacRando.exe"

[Setup]
AppId={{B4D1E8B5-4D1C-4C77-9B27-2D22B6D6F1A0}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\MacRando
DefaultGroupName={#AppName}
OutputDir=..\dist
OutputBaseFilename=MacRando-{#AppVersion}-setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}

[Files]
Source: "..\bin\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\MacRando.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\MacRandoTray.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\trust-certificate.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"
