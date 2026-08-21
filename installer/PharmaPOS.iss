; PharmaPOS Inno Setup installer
; Fresh install OR in-place shop update (same AppId).
; Build: scripts\build-installer.ps1
; Silent shop update (AppUpdateWorker):
;   Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /DIR="C:\Program Files\PharmaPOS"

#define MyAppName "PharmaPOS"
#define MyAppVersion "1.3.1"
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
UsePreviousAppDir=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=PharmaPOS-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0
CloseApplications=yes
RestartApplications=yes
SetupIconFile=..\src\PharmaPOS.WPF\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
InfoBeforeFile=INSTALL_NOTES.txt
AppMutex=PharmaPOS.SingleInstance
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Application binaries — replace on upgrade
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "appsettings.json,appsettings.Production.json,Data\*"

; Shop settings — keep existing on upgrade; write only on first install
Source: "{#PublishDir}\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist ignoreversion
Source: "{#PublishDir}\appsettings.Production.json"; DestDir: "{app}"; Flags: onlyifdoesntexist ignoreversion

; Master catalogue backup — first install only (shop LocalDB data is never overwritten)
Source: "{#DistDataDir}\PharmaPosDb_Master.bak"; DestDir: "{app}\Data"; Flags: onlyifdoesntexist ignoreversion
Source: "{#DistDataDir}\PharmaPosDb_Master.meta.json"; DestDir: "{app}\Data"; Flags: onlyifdoesntexist ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
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
