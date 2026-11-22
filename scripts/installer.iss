#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\release"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{8F3C2A61-5D47-4B9E-A0C3-6E1D9B7F4A25}
AppName=MyDeck
AppVersion={#AppVersion}
AppVerName=MyDeck {#AppVersion}
AppPublisher=Harun Mutlu
AppPublisherURL=https://github.com/harun-mutlu/MyDeck
AppSupportURL=https://github.com/harun-mutlu/MyDeck/issues
AppUpdatesURL=https://github.com/harun-mutlu/MyDeck/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\MyDeck
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=MyDeck-Setup
SetupIconFile=..\windows\MyDeck.Server\MyDeck.ico
UninstallDisplayIcon={app}\MyDeck.exe
UninstallDisplayName=MyDeck
LicenseFile={#SourceDir}\LICENSE.txt
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=force
RestartApplications=no
ShowLanguageDialog=auto

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\MyDeck"; Filename: "{app}\MyDeck.exe"
Name: "{autodesktop}\MyDeck"; Filename: "{app}\MyDeck.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MyDeck.exe"; Description: "{cm:LaunchProgram,MyDeck}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM MyDeck.exe /F"; Flags: runhidden; RunOnceId: "StopMyDeck"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'MyDeck');
end;
