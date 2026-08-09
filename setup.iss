[Setup]
AppName=PC Health Dashboard
AppVersion=1.0.0
DefaultDirName={pf}\PC Health Dashboard
DefaultGroupName=PC Health Dashboard
OutputDir=d:\PC_Health_Dashboard\Installer
OutputBaseFilename=PCHealthDashboard_Setup
Compression=lzma
SolidCompression=yes
SetupIconFile=d:\PC_Health_Dashboard\Assets\logo.ico
UninstallDisplayIcon={app}\PCHealthDashboard.exe

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "d:\PC_Health_Dashboard\Publish\*"; DestDir: "{app}"; Flags: ignoreversion recurse subdirs createallsubdirs

[Icons]
Name: "{group}\PC Health Dashboard"; Filename: "{app}\PCHealthDashboard.exe"
Name: "{commondesktop}\PC Health Dashboard"; Filename: "{app}\PCHealthDashboard.exe"; Tasks: desktopicon
