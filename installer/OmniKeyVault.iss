; OmniKey Vault — Inno Setup Installer Script
; Compile with: ISCC.exe OmniKeyVault.iss
; Requires Inno Setup 6: https://jrsoftware.org/isdl.php

#define MyAppName "OmniKey Vault"
#define MyAppVersion "2.2.1"
#define MyAppPublisher "OmniKey Vault"
#define MyAppURL "https://github.com/mawenshui/omnikey-vault"
#define MyAppExeName "okv.exe"
#define MyAppExeBase "okv"

[Setup]
; AppId must remain constant across versions (for upgrades)
AppId={{B7F3E2A1-1234-5678-9ABC-DEF012345678}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
AppContact={#MyAppURL}
AppCopyright=Copyright (c) 2026 OmniKey Vault
AppComments=本地优先 · 端到端加密 · 开源凭据管理工具
DefaultDirName={autopf}\OmniKeyVault
DefaultGroupName=OmniKey Vault
DisableProgramGroupPage=yes
OutputDir=installer_output
OutputBaseFilename=OmniKeyVault-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName=OmniKey Vault
SetupIconFile=..\images\okv-icon.ico
LicenseFile=..\LICENSE.txt
; Version info embedded in the setup.exe
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}.0
VersionInfoCopyright=Copyright (c) 2026 OmniKey Vault
VersionInfoDescription=OmniKey Vault Installer
; Show installation progress
ShowLanguageDialog=auto
; v2.2.0: Auto-update support — force-close the running app during
; silent installs so files can be replaced without manual intervention.
CloseApplications=force
RestartApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "associateokv"; Description: "关联 .okv 文件"; GroupDescription: "文件关联:"

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Comment: "OmniKey Vault — 加密凭据管理工具"
Name: "{group}\使用手册"; Filename: "{app}\docs\使用手册.md"; Comment: "打开使用手册"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#MyAppExeName}"; Comment: "OmniKey Vault — 加密凭据管理工具"

[Registry]
; Associate .okv files (optional task)
Root: HKCR; Subkey: ".okv"; ValueType: string; ValueName: ""; ValueData: "OmniKeyVault.File"; Flags: uninsdeletevalue; Tasks: associateokv
Root: HKCR; Subkey: "OmniKeyVault.File"; ValueType: string; ValueName: ""; ValueData: "OmniKey Vault 加密金库"; Flags: uninsdeletekey; Tasks: associateokv
Root: HKCR; Subkey: "OmniKeyVault.File\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: associateokv
Root: HKCR; Subkey: "OmniKeyVault.File\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associateokv

[Run]
; Interactive install: show launch checkbox on the final wizard page
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
; Silent auto-update: always launch the updated app after install
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: IsSilentInstall

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

// v2.2.0: Returns True when the installer is running in /SILENT or /VERYSILENT
// mode (used for auto-update). In that case, the second [Run] entry launches
// the updated app automatically after installation completes.
function IsSilentInstall(): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    if (CompareText(ParamStr(I), '/SILENT') = 0) or
       (CompareText(ParamStr(I), '/VERYSILENT') = 0) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;
