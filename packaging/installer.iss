; WinLaunch Enhanced installer (Inno Setup 6.5+). Built by packaging\build.ps1,
; which passes /DAppVersion and /DSourceDir. Installs per user (no admin
; prompt); "all users" is offered on the first page.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\WinLaunchEnhanced"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{3B6F1C2E-8A47-4E0B-9F1D-5C2A7E9B4D61}
AppName=WinLaunch Enhanced
AppVersion={#AppVersion}
AppVerName=WinLaunch Enhanced {#AppVersion}
AppPublisher=DavidHiFi
AppPublisherURL=https://github.com/DavidHiFi/WinLaunch-Enhanced
AppSupportURL=https://github.com/DavidHiFi/WinLaunch-Enhanced/issues
AppUpdatesURL=https://github.com/DavidHiFi/WinLaunch-Enhanced/releases
AppCopyright=Copyright (C) 2022 MrC0rrupted, 2026 DavidHiFi. MIT license.
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\WinLaunch Enhanced
DefaultGroupName=WinLaunch Enhanced
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=WinLaunchEnhanced-{#AppVersion}-Setup
SetupIconFile=..\src\WinLaunch.Enhanced\Assets\WinLaunchEnhanced.ico
UninstallDisplayIcon={app}\WinLaunchEnhanced.exe
UninstallDisplayName=WinLaunch Enhanced
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dynamic windows11
CloseApplications=yes
RestartApplications=no
SetupMutex=WinLaunchEnhancedSetup
LicenseFile=..\LICENSE
ShowLanguageDialog=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start WinLaunch Enhanced when I sign in"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "Add a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\WinLaunch Enhanced"; Filename: "{app}\WinLaunchEnhanced.exe"; AppUserModelID: "DavidHiFi.WinLaunchEnhanced"; Comment: "Open the launcher"
Name: "{autodesktop}\WinLaunch Enhanced"; Filename: "{app}\WinLaunchEnhanced.exe"; AppUserModelID: "DavidHiFi.WinLaunchEnhanced"; Comment: "Open the launcher"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WinLaunch Enhanced"; ValueData: """{app}\WinLaunchEnhanced.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\WinLaunchEnhanced.exe"; Description: "Open WinLaunch Enhanced now"; Flags: nowait postinstall skipifsilent
Filename: "{app}\WinLaunchEnhanced.exe"; Parameters: "--background"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{app}\WinLaunchEnhanced.exe"; Parameters: "--quit"; Flags: runhidden waituntilterminated; RunOnceId: "QuitApp"

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
// The app targets .NET 10 and rolls forward to newer major runtimes, so any
// Windows Desktop runtime 10 or later in the shared dotnet folder will do.
function HasDesktopRuntime10: Boolean;
var
  Rec: TFindRec;
  Major: Integer;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\*'), Rec) then
  try
    repeat
      if (Rec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        Major := StrToIntDef(Copy(Rec.Name, 1, Pos('.', Rec.Name + '.') - 1), 0);
        if Major >= 10 then
          Result := True;
      end;
    until not FindNext(Rec);
  finally
    FindClose(Rec);
  end;
end;

function InitializeSetup: Boolean;
var
  Code: Integer;
begin
  Result := True;
  if not HasDesktopRuntime10 then
  begin
    if SuppressibleMsgBox('WinLaunch Enhanced needs the .NET 10 Desktop Runtime (about 60 MB, from Microsoft).' + #13#10 + #13#10 +
        'Open the download page now? Install it, then run this setup again.', mbConfirmation, MB_YESNO, IDNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, Code);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Exe: String;
begin
  Result := '';
  Exe := ExpandConstant('{app}\WinLaunchEnhanced.exe');
  if FileExists(Exe) then
  begin
    { Ask a running copy to quit so its files can be replaced. }
    Exec(Exe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Sleep(1200);
  end;
end;
