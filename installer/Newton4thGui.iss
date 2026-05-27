; Inno Setup script for Newton4thGui (PPA5500 + Rotronic logger)
; Compile with: ISCC.exe Newton4thGui.iss
; Output: installer\output\Newton4thGui-Setup-<version>.exe

#define MyAppName       "Newton4thGui"
#define MyAppPublisher  "Caltest"
#define MyAppVersion    "1.0.0"
#define MyAppExeName    "Newton4thGui.exe"
#define PublishDir      "publish"
#define PrereqDir       "prereq"

[Setup]
AppId={{6E2C9E8F-7B0A-4F4A-9C2D-PPA5500ROTRON}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=Newton4thGui-Setup-{#MyAppVersion}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\Newton4thGui\app.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Self-contained .NET 8 app (everything under publish\)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; FTDI CDM driver installer - exact filename (Inno [Run] doesn't expand wildcards)
Source: "{#PrereqDir}\CDM2123620_Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Install FTDI CDM driver silently before app launch. /quiet flag is what FTDI's
; CDM*_Setup.exe uses (it's a DPInst wrapper underneath).
Filename: "{tmp}\CDM2123620_Setup.exe"; Parameters: "/quiet"; \
    StatusMsg: "Installing FTDI USB driver..."; \
    Flags: waituntilterminated runhidden; \
    Check: FtdiInstallerPresent
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; \
    Flags: nowait postinstall skipifsilent

[Code]
function FtdiInstallerPresent: Boolean;
begin
  Result := FileExists(ExpandConstant('{tmp}\CDM2123620_Setup.exe'));
end;
