; Inno Setup script for ImgViewer.
; Build a real Windows installer (ImgViewer-1.0.0-Setup.exe) from the published app.
;
; HOW TO BUILD (on Windows):
;   1. Install Inno Setup 6.3+  ->  https://jrsoftware.org/isdl.php
;   2. Make sure the app has been published to ..\publish\win-x64
;      (run  build.sh  on Linux, or  dotnet publish ...  on Windows)
;   3. Open this file in Inno Setup and click  Build > Compile
;      (or run:  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" ImgViewer.iss )
;   4. The installer appears in ..\dist\ImgViewer-1.0.0-Setup.exe

#define MyAppName "ImgViewer"
; Version can be injected by CI:  ISCC.exe /DMyAppVersion=1.1.0 ImgViewer.iss
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "ImgViewer"
#define MyAppExe "ImgViewer.exe"

[Setup]
; A stable AppId so upgrades replace the previous install (keep this GUID forever).
AppId={{B8735E9A-21A3-4EF9-A1AE-75F1E303F336}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExe}
SetupIconFile=..\assets\icon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=ImgViewer-{#MyAppVersion}-Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
; Korean UI (ships with Inno Setup's Languages folder).
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "associate"; Description: "Register ImgViewer as an image viewer (confirm defaults in Windows Settings)"; GroupDescription: "File associations:"

[Files]
; Everything produced by the self-contained publish.
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
; Register per-user file associations in the *original* (non-elevated) user's hive.
Filename: "{app}\{#MyAppExe}"; Parameters: "--register"; Tasks: associate; Flags: runasoriginaluser runhidden
; Offer to launch when finished.
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; ([UninstallRun] doesn't allow runasoriginaluser; the uninstaller runs elevated.)
Filename: "{app}\{#MyAppExe}"; Parameters: "--unregister"; Flags: runhidden; RunOnceId: "UnregisterAssoc"
