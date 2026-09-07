; Script Inno Setup 6 pour Desktop Organize Maxxing
; Créé par Igrek
; Compilation : iscc installer.iss

[Setup]
AppId={{D0E5A7B8-9A21-4F93-87A4-E9723049F1B2}
AppName=Desktop Organize Maxxing
AppVersion=1.0.0
AppPublisher=Igrek
AppPublisherURL=https://github.com/DOMaxxing
DefaultDirName={autopf}\DesktopOrganizeMaxxing
DefaultGroupName=Desktop Organize Maxxing
AllowNoIcons=yes
OutputDir=publish\installer
OutputBaseFilename=DesktopOrganizeMaxxing_Setup
SetupIconFile=src\DesktopOrganizeMaxxing\Assets\desktop_organiser_maxxing_logo.ico
UninstallDisplayIcon={app}\DesktopOrganizeMaxxing.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
CloseApplicationsFilter=DesktopOrganizeMaxxing.exe

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
french.CreateDesktopIcon=Créer un raccourci sur le &bureau
english.CreateDesktopIcon=Create a &desktop shortcut
french.StartupTask=Lancer automatiquement au démarrage de Windows (Recommandé)
english.StartupTask=Start automatically with Windows (Recommended)
french.StartupGroup=Démarrage :
english.StartupGroup=Startup options:
french.LaunchProgram=Lancer Desktop Organize Maxxing
english.LaunchProgram=Launch Desktop Organize Maxxing

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "{cm:StartupTask}"; GroupDescription: "{cm:StartupGroup}"

[Files]
Source: "publish\standalone\DesktopOrganizeMaxxing.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\standalone\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Desktop Organize Maxxing"; Filename: "{app}\DesktopOrganizeMaxxing.exe"; IconFilename: "{app}\DesktopOrganizeMaxxing.exe"
Name: "{group}\{cm:UninstallProgram,Desktop Organize Maxxing}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Desktop Organize Maxxing"; Filename: "{app}\DesktopOrganizeMaxxing.exe"; IconFilename: "{app}\DesktopOrganizeMaxxing.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DesktopOrganizeMaxxing"; ValueData: """{app}\DesktopOrganizeMaxxing.exe"" --autostart"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\DesktopOrganizeMaxxing.exe"; Description: "{cm:LaunchProgram}"; Flags: nowait postinstall skipifsilent
