; ============================================================================
;  GlideBar — one-click installer script (Inno Setup 6)
;  Builds GlideBar-Setup.exe: a per-user install (no admin / UAC prompt) that
;  drops the self-contained GlideBar.exe, adds Start-menu (+ optional desktop)
;  shortcuts, an optional "start with Windows" entry, and a clean uninstaller.
; ============================================================================

#define MyAppName "GlideBar"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Sanidhya Mishra"
#define MyAppExeName "GlideBar.exe"

[Setup]
AppId={{858A2FA6-29D7-4D0E-B9F9-1AB446702939}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppContact={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

; --- One-click experience: skip the interactive pages, no admin needed ---
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
ShowLanguageDialog=no

; --- Output + packaging ---
OutputDir=.
OutputBaseFilename=GlideBar-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; --- 64-bit app ---
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startupicon"; Description: "Start {#MyAppName} automatically when &Windows starts"; GroupDescription: "Startup:"

[Files]
Source: "..\dist\app\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; "Start with Windows" — per-user Run key, removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "{#MyAppName}"; \
    ValueData: """{app}\{#MyAppExeName}"""; \
    Tasks: startupicon; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
; Make sure the app isn't running when uninstalling.
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#MyAppExeName} /F"; \
    Flags: runhidden; RunOnceId: "KillGlideBar"
