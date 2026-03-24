; Octolink Installer Script for Inno Setup
; Download Inno Setup from: https://jrsoftware.org/isinfo.php

#define MyAppName "Octolink"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Octolink"
#define MyAppURL "https://github.com/PouplarWesel/Octolink"
#define MyAppExeName "Octolink.exe"

[Setup]
; Unique app identifier - DO NOT change this between versions
AppId={{8F7E9A3C-5D2B-4E1F-A8C6-9B7D3E5F2A1C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Output settings
OutputDir=..\installer_output
OutputBaseFilename=OctolinkSetup
; SetupIconFile=..\Octolink\icon.ico  ; Uncomment if you have an icon.ico file
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Require admin for ViGEmBus installation
PrivilegesRequired=admin
; Minimum Windows version (Windows 10)
MinVersion=10.0
; Architecture
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Uninstaller
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "installvigem"; Description: "Install ViGEmBus Driver (required for virtual controllers)"; GroupDescription: "Required Components:"; Flags: checkedonce

[Files]
; Main application files from publish folder
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; ViGEmBus installer (download separately - see BuildInstaller.bat)
Source: ".\dependencies\ViGEmBus_1.22.0_x64_x86_arm64.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall; Tasks: installvigem

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Install ViGEmBus driver silently
Filename: "{tmp}\ViGEmBus_1.22.0_x64_x86_arm64.exe"; Parameters: "/quiet /norestart"; StatusMsg: "Installing ViGEmBus Driver..."; Tasks: installvigem; Flags: waituntilterminated
; Register URL ACLs for HTTP/WebSocket servers
Filename: "netsh"; Parameters: "http add urlacl url=http://+:5000/ user=Everyone"; StatusMsg: "Configuring network permissions..."; Flags: runhidden waituntilterminated
Filename: "netsh"; Parameters: "http add urlacl url=http://+:5001/ user=Everyone"; Flags: runhidden waituntilterminated
; Add firewall rules
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""Octolink HTTP"" dir=in action=allow protocol=TCP localport=5000"; StatusMsg: "Adding firewall rules..."; Flags: runhidden waituntilterminated
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""Octolink WebSocket"" dir=in action=allow protocol=TCP localport=5001"; Flags: runhidden waituntilterminated
; Launch app after install
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove firewall rules on uninstall
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""Octolink HTTP"""; Flags: runhidden
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""Octolink WebSocket"""; Flags: runhidden
; Remove URL ACLs
Filename: "netsh"; Parameters: "http delete urlacl url=http://+:5000/"; Flags: runhidden
Filename: "netsh"; Parameters: "http delete urlacl url=http://+:5001/"; Flags: runhidden

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Any post-install actions can go here
  end;
end;
