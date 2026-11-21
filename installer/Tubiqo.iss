; Tubiqo installer (Inno Setup 6). Per-user install: no admin rights, nothing outside the user's profile.
; Built by tools/build-release.ps1, which passes:
;   /DAppVersion=x.y.z  /DSourceDir=<published app folder>  /DOutputDir=<installer output folder>

#ifndef AppVersion
  #error AppVersion must be defined (use tools/build-release.ps1)
#endif

[Setup]
AppId={{5E7A4D2C-3B9F-4C61-9A0E-7D2B1F6C8A43}
AppName=Tubiqo
AppVersion={#AppVersion}
AppVerName=Tubiqo {#AppVersion}
AppPublisher=Tubiqo
AppPublisherURL=https://github.com/Kandog/Tubiqo
DefaultDirName={localappdata}\Programs\Tubiqo
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=Tubiqo-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Close a running Tubiqo before replacing its files (used by in-app updates).
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\Tubiqo.exe
UninstallDisplayName=Tubiqo

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[InstallDelete]
; Remove files from the previous version so nothing stale is left behind.
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Tubiqo"; Filename: "{app}\Tubiqo.exe"
Name: "{autodesktop}\Tubiqo"; Filename: "{app}\Tubiqo.exe"; Tasks: desktopicon

[Run]
; Normal install: offer to start Tubiqo at the end.
Filename: "{app}\Tubiqo.exe"; Description: "Start Tubiqo"; Flags: nowait postinstall skipifsilent
; In-app update (silent): start the new version straight away.
Filename: "{app}\Tubiqo.exe"; Flags: nowait skipifnotsilent

[UninstallDelete]
; Site Data is wiped on every exit anyway; remove any leftover after a crash.
; Tubiqo Data (Watch History, settings, downloaded Rule Packs) stays unless the user deletes it.
Type: filesandordirs; Name: "{localappdata}\Tubiqo\site"
