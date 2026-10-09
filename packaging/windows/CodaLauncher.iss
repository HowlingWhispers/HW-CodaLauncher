#ifndef AppVersion
  #define AppVersion "0.7.26"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\..\dist\payload"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\dist\release"
#endif
#ifndef Bootstrapper
  #define Bootstrapper "..\..\dist\MicrosoftEdgeWebview2Setup.exe"
#endif

[Setup]
AppId={{60308D26-7772-43DD-95DC-E08776AD50B4}
AppName=CodaLauncher
AppVersion={#AppVersion}
AppPublisher=Howling Whispers
AppPublisherURL=https://thehowlingwhispers.com
AppSupportURL=https://github.com/HowlingWhispers/HW-CodaLauncher/issues
DefaultDirName={localappdata}\Programs\CodaLauncher
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=CodaLauncher-v{#AppVersion}-win64-Setup
SetupIconFile=CodaLauncher.ico
UninstallDisplayIcon={app}\CodaLauncher.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=no
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=Coda has your installation paperwork ready.%n%nThis will install CodaLauncher on your computer, with desktop and Start Menu shortcuts.%n%nYour worlds and settings stay separate from the launcher. If needed, Setup installs Microsoft WebView2 using your internet connection.
FinishedLabel=CodaLauncher is installed. Coda has filed your shortcuts.%n%nYour adventures are waiting.

[Tasks]
Name: "desktopicon"; Description: "Put CodaLauncher on my &desktop"; GroupDescription: "Shortcuts:"; Flags: checkedonce

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Bootstrapper}"; Flags: dontcopy

[Icons]
Name: "{userprograms}\CodaLauncher"; Filename: "{app}\CodaLauncher.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\CodaLauncher"; Filename: "{app}\CodaLauncher.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\CodaLauncher.exe"; Description: "&Launch CodaLauncher"; Flags: nowait postinstall skipifsilent

[Code]
function HasWebView2: Boolean;
var
  Version: String;
  Key: String;
begin
  Key := 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result := (RegQueryStringValue(HKLM32, Key, 'pv', Version) and
    (Version <> '') and (Version <> '0.0.0.0'));
  if not Result then
    Result := (RegQueryStringValue(HKCU, Key, 'pv', Version) and
      (Version <> '') and (Version <> '0.0.0.0'));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if HasWebView2 then exit;
  ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
  if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'),
    '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Result := 'Coda could not start Microsoft WebView2 setup. Please retry Setup.'
  else if not HasWebView2 then
    Result := 'Microsoft WebView2 could not be installed (code ' + IntToStr(ExitCode) + '). Please check your internet connection and retry Setup.';
end;

// Never add an UninstallDelete rule for player data or a wildcard under {app}.
// Inno removes only its installed files; the self-updater continues using the ZIP.
