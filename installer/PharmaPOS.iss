; PharmaPOS Inno Setup installer script
; Requires: Inno Setup 6 (https://jrsoftware.org/isinfo.php)
; Build with: scripts\build-installer.ps1

#define MyAppName "PharmaPOS"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "PharmaPOS"
#define MyAppExeName "PharmaPOS.exe"
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef DistDataDir
  #define DistDataDir "..\artifacts\dist"
#endif

[Setup]
AppId={{A7C3E8F1-9B2D-4E6A-8F01-2C4D6E8A0B12}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=PharmaPOS-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0
SetupIconFile=..\src\PharmaPOS.WPF\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
InfoBeforeFile=INSTALL_NOTES.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Published application (self-contained win-x64)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Master-data SQL backup (medicines/salts/suppliers/manufacturers — no shop transactions)
Source: "{#DistDataDir}\PharmaPosDb_Master.bak"; DestDir: "{app}\Data"; Flags: ignoreversion
Source: "{#DistDataDir}\PharmaPosDb_Master.meta.json"; DestDir: "{app}\Data"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
  // LocalDB is required. Warn if sqllocaldb is missing (non-blocking for silent).
  if not FileExists(ExpandConstant('{sys}\SqlLocalDB.exe')) and
     not FileExists(ExpandConstant('{commonpf}\Microsoft SQL Server\150\Tools\Binn\SqlLocalDB.exe')) and
     not FileExists(ExpandConstant('{commonpf}\Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe')) then
  begin
    MsgBox('SQL Server LocalDB was not detected.'#13#10#13#10 +
           'PharmaPOS needs SQL Server Express LocalDB.'#13#10 +
           'Install LocalDB, then run PharmaPOS.'#13#10#13#10 +
           'Download: https://learn.microsoft.com/sql/database-engine/configure-windows/sql-server-express-localdb',
           mbInformation, MB_OK);
  end;
end;
