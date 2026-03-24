; Octolink Installer Script for Inno Setup

#ifndef MyAppVersion
#define MyAppVersion "1.0.0"
#endif

#define MyAppName "Octolink"
#define MyAppPublisher "Octolink"
#define MyAppURL "https://github.com/PouplarWesel/Octolink"
#define MyAppExeName "Octolink.exe"

[Setup]
AppId={{8F7E9A3C-5D2B-4E1F-A8C6-9B7D3E5F2A1C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\installer_output
OutputBaseFilename=OctolinkSetup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: ".\dependencies\ViGEmBus_1.22.0_x64_x86_arm64.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\ViGEmBus_1.22.0_x64_x86_arm64.exe"; Parameters: "/quiet /norestart"; StatusMsg: "Installing ViGEmBus Driver..."; Check: not IsViGEmBusInstalled; Flags: waituntilterminated
Filename: "netsh"; Parameters: "http add urlacl url=http://+:5000/ user=Everyone"; StatusMsg: "Configuring network permissions..."; Flags: runhidden waituntilterminated
Filename: "netsh"; Parameters: "http add urlacl url=http://+:5001/ user=Everyone"; Flags: runhidden waituntilterminated
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""Octolink HTTP"" dir=in action=allow protocol=TCP localport=5000"; StatusMsg: "Adding firewall rules..."; Flags: runhidden waituntilterminated
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""Octolink WebSocket"" dir=in action=allow protocol=TCP localport=5001"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""Octolink HTTP"""; Flags: runhidden
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""Octolink WebSocket"""; Flags: runhidden
Filename: "netsh"; Parameters: "http delete urlacl url=http://+:5000/"; Flags: runhidden
Filename: "netsh"; Parameters: "http delete urlacl url=http://+:5001/"; Flags: runhidden

[Code]
function IsViGEmBusInstalled(): Boolean;
begin
  Result :=
    RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ViGEmBus') or
    RegKeyExists(HKLM64, 'SYSTEM\CurrentControlSet\Services\ViGEmBus') or
    FileExists(ExpandConstant('{sys}\drivers\vigembus.sys'));
end;
