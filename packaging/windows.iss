#ifndef AppVersion
  #define AppVersion "1.0.0-preview.1"
#endif
#ifndef FileVersion
  #define FileVersion "1.0.0.0"
#endif
#ifndef PayloadDir
  #error Supply PayloadDir from packaging/package.ps1
#endif
#ifndef OutputDir
  #error Supply OutputDir from packaging/package.ps1
#endif
#ifndef RepoRoot
  #error Supply RepoRoot from packaging/package.ps1
#endif

[Setup]
AppId={{D1A33E82-89F5-4850-A610-775AB82314F3}
AppName=CCP Classic
AppVersion={#AppVersion}
AppPublisher=CC labs Srls
VersionInfoVersion={#FileVersion}
DefaultDirName={localappdata}\Programs\CCP Classic
DefaultGroupName=CCP Classic
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
DisableProgramGroupPage=yes
LicenseFile={#RepoRoot}\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=CCP.Classic-{#AppVersion}-win-x64-setup
Compression=lzma2/fast
SolidCompression=yes
UninstallDisplayIcon={app}\CCP.Classic.App.exe
CloseApplications=yes
CloseApplicationsFilter=CCP.Classic.App.exe
RestartApplications=no
WizardStyle=modern
SetupIconFile={#RepoRoot}\src\CCP.Classic.App\Assets\app.ico

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\CCP Classic"; Filename: "{app}\CCP.Classic.App.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\CCP Classic"; Filename: "{app}\CCP.Classic.App.exe"; WorkingDir: "{app}"; Tasks: desktopicon
