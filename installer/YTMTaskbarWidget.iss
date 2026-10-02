; installer/YTMTaskbarWidget.iss
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#define MyAppName "YouTube Music Taskbar Widget"
#define MyAppPublisher "Tan Yit Shen"
#define MyAppURL "https://github.com/51-Shenn/ytmusic-taskbar-widget"
#define MyAppExeName "YTMTaskbarWidget.exe"

[Setup]
AppId={{8F2C7A15-4B6E-4D9A-9C31-7E5B0D2A6F84}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={autopf}\YTMTaskbarWidget
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=YTMTaskbarWidget-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start with Windows"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "YTMTaskbarWidget"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // The widget runs silently in the taskbar; close it so the exe isn't file-locked.
  Exec('taskkill', '/IM YTMTaskbarWidget.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
