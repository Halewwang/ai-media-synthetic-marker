#ifndef StageDir
  #error StageDir define is required
#endif
#ifndef AppVersion
  #error AppVersion define is required
#endif
#ifndef OutputDir
  #error OutputDir define is required
#endif

[Setup]
AppId={{9F630913-5706-4142-A1A4-C35B171938C8}
AppName=EMKE AI Marker
AppVersion={#AppVersion}
AppPublisher=EMKE
DefaultDirName={localappdata}\Programs\EMKE AI Marker
DefaultGroupName=EMKE AI Marker
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=emke-ai-marker-v{#AppVersion}-windows-x64-setup
SetupIconFile=..\..\src\Emke.AiMarker.App\Assets\emke-ai-marker.ico
UninstallDisplayIcon={app}\EMKE AI Marker.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}.0

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\EMKE AI Marker"; Filename: "{app}\EMKE AI Marker.exe"
Name: "{autodesktop}\EMKE AI Marker"; Filename: "{app}\EMKE AI Marker.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\EMKE AI Marker.exe"; Description: "启动 EMKE AI Marker"; Flags: nowait postinstall skipifsilent
