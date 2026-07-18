#ifndef AppVersion
  #error AppVersion must be supplied by Build-WindowsInstaller.ps1
#endif
#ifndef SourceDir
  #error SourceDir must be supplied by Build-WindowsInstaller.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by Build-WindowsInstaller.ps1
#endif

#define AppName "PromFlow Dispatcher"
#define AppExeName "PromFlow.Dispatcher.exe"

[Setup]
AppId={{6A45A2E9-87C5-4A69-9A98-66CB3DA781A2}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=PromFlow
DefaultDirName={autopf}\PromFlow Dispatcher
DefaultGroupName=PromFlow Dispatcher
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=PromFlow.Dispatcher-{#AppVersion}-win-x64-setup
SetupIconFile=..\..\..\Configurator.Desktop\Assets\avalonia-logo.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} installer

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
