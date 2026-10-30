; Installer for Better DeepL Translator. Build it with installer\build.ps1, which publishes the app
; and passes AppVersion and PublishDir to the compiler.

#ifndef AppVersion
  #error AppVersion is not defined. Build the installer with installer\build.ps1.
#endif
#ifndef PublishDir
  #error PublishDir is not defined. Build the installer with installer\build.ps1.
#endif

#define AppName "Better DeepL Translator"
#define AppExe  "Better DeepL Translator.exe"

[Setup]
; The ID ties updates and the uninstall entry together. It must never change.
AppId={{AB237F8A-B239-458C-BFC9-625A415608D7}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Naxterra
AppPublisherURL=https://github.com/Naxterra/Better-DeepL-Translator
VersionInfoVersion={#AppVersion}
; Installs for the current user only, so no administrator rights are needed.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Setup and the uninstaller wait until the running app is closed. The name matches the app's
; single-instance mutex.
AppMutex=DeepL-Translator-7F3A
SetupIconFile=..\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern dynamic
Compression=lzma2
SolidCompression=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=BetterDeepLTranslator-{#AppVersion}-setup

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
german.RuntimeMissing=Die .NET 11 Desktop Runtime (x64) wurde nicht gefunden. Ohne sie startet die App nicht.%n%nSie erhalten sie unter https://dotnet.microsoft.com/download/dotnet/11.0%n%nTrotzdem installieren?
english.RuntimeMissing=The .NET 11 Desktop Runtime (x64) was not found. The app does not start without it.%n%nYou can get it from https://dotnet.microsoft.com/download/dotnet/11.0%n%nInstall anyway?
german.DeleteSettings=Sollen auch Ihre Einstellungen und die gespeicherte DeepL-Anmeldung gelöscht werden?
english.DeleteSettings=Do you also want to delete your settings and the saved DeepL login?

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; Excludes: "*.pdb"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Removes the "Start with Windows" entry and undoes the deepl:// handler of an unfinished login
; while the app's files are still there.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall-cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "RegistryCleanup"

[Code]
function DesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\11.*'), FindRec);
  if Result then
    FindClose(FindRec);
end;

function InitializeSetup: Boolean;
begin
  Result := DesktopRuntimeInstalled or
    (SuppressibleMsgBox(CustomMessage('RuntimeMissing'), mbConfirmation, MB_YESNO, IDNO) = IDYES);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Settings and the encrypted login are the user's data, so they are only deleted on request.
  // A silent uninstall keeps them.
  if (CurUninstallStep = usPostUninstall) and
     (SuppressibleMsgBox(CustomMessage('DeleteSettings'), mbConfirmation, MB_YESNO, IDNO) = IDYES) then
    DelTree(ExpandConstant('{userappdata}\DeepL-Translator'), True, True, True);
end;
