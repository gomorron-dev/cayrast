; Cayrast installer (Inno Setup 6)
;
; Chosen over WiX because this installer needs custom wizard pages - module selection
; and a theme picker - and building those into an MSI is genuinely painful. Inno also
; produces a single self-contained .exe, which is what users expect to download.
;
; Build with:  iscc Cayrast.iss /DAppVersion=1.0.0 /DSourceDir=..\..\artifacts\publish

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\..\artifacts\publish"
#endif

#define AppName "Cayrast"
#define AppPublisher "Cayrast Contributors"
#define AppUrl "https://github.com/cayrast/cayrast"
#define AppExeName "Cayrast.exe"

[Setup]
AppId={{9F2B4C81-3E7A-4D65-B0A1-7C5E9D2F8A34}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases

; Per-user install into LocalAppData. Deliberate: Cayrast needs no elevation, and an
; installer that asks for administrator rights to install a launcher is a bad trade.
; It also means uninstall genuinely removes everything without leaving system state.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\{#AppName}
DisableDirPage=no
DefaultGroupName={#AppName}
AllowNoIcons=yes

OutputDir=Output
OutputBaseFilename=Cayrast-{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; Cayrast targets Windows 10 1809 and later; WebView2 and the DWM backdrop APIs are
; not available below that.
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

LicenseFile=..\..\LICENSE
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Start {#AppName} when I sign in"; GroupDescription: "Startup:"; Flags: unchecked

[Types]
Name: "full"; Description: "Everything"
Name: "minimal"; Description: "Launcher only"
Name: "custom"; Description: "Choose what to install"; Flags: iscustom

[Components]
Name: "core"; Description: "Core launcher"; Types: full minimal custom; Flags: fixed
Name: "modules"; Description: "Official modules"; Types: full custom
Name: "modules\example"; Description: "Example module (for developers)"; Types: custom

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Components: core; \
  Flags: ignoreversion recursesubdirs createallsubdirs

Source: "{#SourceDir}\modules\example\*"; DestDir: "{app}\Modules\example"; \
  Components: modules\example; Flags: ignoreversion recursesubdirs skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Registry Run rather than a Startup-folder shortcut: it survives a profile roam and
; is what users expect to find when auditing what launches at sign-in.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "Cayrast"; ValueData: """{app}\{#AppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Start {#AppName} now"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Caches and logs are regenerable and would otherwise be left behind. User settings,
; themes, and installed modules in %APPDATA% are deliberately NOT removed - someone
; reinstalling after an update should not lose their configuration.
Type: filesandordirs; Name: "{localappdata}\{#AppName}\Cache"
Type: filesandordirs; Name: "{localappdata}\{#AppName}\Logs"
Type: filesandordirs; Name: "{localappdata}\{#AppName}\WebView2"

[Code]
{ Cayrast will not start without the WebView2 runtime. It ships with Windows 11 but
  not with every Windows 10 install, so this checks and points the user at it rather
  than letting them install something that silently fails to open. }
function IsWebView2Installed: Boolean;
var
  Version: string;
begin
  Result :=
    RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) or
    RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) or
    RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version);
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  if not IsWebView2Installed then
  begin
    if MsgBox('Cayrast needs the Microsoft Edge WebView2 Runtime, which is not installed.' + #13#10#13#10 +
              'It is a free Microsoft component and ships with Windows 11.' + #13#10#13#10 +
              'Open the download page now?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExecAsOriginalUser('open', 'https://developer.microsoft.com/microsoft-edge/webview2/', '', '', SW_SHOW, ewNoWait, ErrorCode);
    end;

    { Not fatal. A user may be installing ahead of the runtime deliberately, and
      blocking them outright would be presumptuous. }
    Result := MsgBox('Continue installing Cayrast anyway?', mbConfirmation, MB_YESNO) = IDYES;
  end;
end;

{ A running instance holds its files open, so uninstall and upgrade both need it
  closed. Asking is better than failing halfway through with a locked-file error. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ErrorCode: Integer;
begin
  Result := '';

  if CheckForMutexes('Local\cayrast-single-instance') then
  begin
    if MsgBox('Cayrast is running and must be closed to continue.' + #13#10#13#10 +
              'Close it now?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      Exec('taskkill.exe', '/IM Cayrast.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
      Sleep(1500);
    end
    else
      Result := 'Cayrast is still running. Close it and run setup again.';
  end;
end;
