; C2 Agent Installer Script for Inno Setup
; Requires Inno Setup 6.x - Download from: https://jrsoftware.org/isdl.php

#define MyAppName "C2 Agent"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "POC Project"
#define MyAppExeName "Agent.exe"
#define MyAppURL "https://github.com/gleidsonbalcazar/poc_recorder"

[Setup]
; Basic Information
AppId={{C2AGENT-POC-2025-RECORDER-FFMPEG}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Installation Directory
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Output Configuration
OutputDir=releases
OutputBaseFilename=C2AgentSetup-v{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes

; Privileges
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

; Visual Style
WizardStyle=modern
; SetupIconFile=Agent\icon.ico
; UninstallDisplayIcon={app}\{#MyAppExeName}
; TODO: Create icon.ico and uncomment above lines

; License and Info
LicenseFile=LICENSE.txt
InfoBeforeFile=INSTALL_INFO.txt

; Architecture
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "startup"; Description: "Run {#MyAppName} on Windows startup"; GroupDescription: "Additional options:"
Name: "desktop"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
; Main executable
Source: "releases\C2Agent-v{#MyAppVersion}.exe"; DestDir: "{app}"; DestName: "{#MyAppExeName}"; Flags: ignoreversion

; Documentation (if exists)
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
; Start Menu shortcut
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: ""
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

; Desktop shortcut
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktop

[Run]
; Option to run after installation
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; Flags: nowait postinstall skipifsilent unchecked

[Registry]
; Startup registry entry
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"" http://localhost:8000"; Flags: uninsdeletevalue; Tasks: startup

[Code]
var
  ServerURLPage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  { Create custom page for server URL configuration }
  ServerURLPage := CreateInputQueryPage(wpSelectTasks,
    'Server Configuration', 'Configure C2 Server Connection',
    'Please specify the URL of the C2 server that this agent will connect to.');

  ServerURLPage.Add('Server URL (e.g., http://192.168.1.100:8000):', False);
  ServerURLPage.Values[0] := 'http://localhost:8000';
end;

function GetServerURL(Param: String): String;
begin
  Result := ServerURLPage.Values[0];
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ServerURL: String;
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    ServerURL := ServerURLPage.Values[0];

    { Update startup registry entry with server URL if task is selected }
    if IsTaskSelected('startup') then
    begin
      RegWriteStringValue(HKEY_CURRENT_USER,
        'Software\Microsoft\Windows\CurrentVersion\Run',
        '{#MyAppName}',
        '"' + ExpandConstant('{app}\{#MyAppExeName}') + '" ' + ServerURL);
    end;
  end;
end;

[UninstallDelete]
; Clean up local data on uninstall (optional - commented out for safety)
; Type: filesandordirs; Name: "{localappdata}\C2Agent"

[Messages]
; Custom messages
WelcomeLabel2=This will install [name/ver] on your computer.%n%n⚠️ WARNING: This is a POC (Proof of Concept) application for educational purposes only. Use responsibly and only on systems you own or have explicit permission to test.
