; ============================================================================
;  miPDFconvert - Inno Setup Script
;  Ersetzt das bisherige Visual Studio Setup-Projekt (miPDFconvertSetup.vdproj)
;
;  Bildet alles nach, was das alte Setup geboten hat:
;    * Installiert die komplette Publish-Ausgabe (miPDFconvert, miPDFconvertBase,
;      SetupHelper, Ghostscript gsdll32/64, alle Abhaengigkeiten)
;    * Legt die Druckertreiber-Dateien unter miMonitor\x86 und miMonitor\x64 ab
;    * Ruft SetupHelper.exe fuer die Treiber-Installation auf
;    * Prueft die Voraussetzungen (VC++ Redistributable 14, .NET 8 Desktop Runtime)
;    * Per-Machine Installation, Vorgaengerversionen werden ersetzt
;
;  Build:  ISCC.exe miPDFconvert.iss
;          (Inno Setup 6, https://jrsoftware.org/isdl.php)
; ============================================================================

#define MyAppName        "miPDFconvert"
#define MyAppVersion     "1.0.3"
#define MyAppPublisher   "Wolfgang Mitterbucher"
#define MyAppURL         "https://mitterbucher.com"
; UpgradeCode aus dem alten vdproj -> gleiche AppId sorgt fuer saubere Updates
#define MyAppId          "{{CC40866C-933B-4003-B1D2-73B281E517F9}"

; Relative Quellpfade (dieses Skript liegt in ...\miPDFconvert\miPDFconvertSetup)
#define PublishDir       "..\build\publish"
#define LibWin32Dir      "..\lib\miMonitor\Win32"
#define LibWin64Dir      "..\lib\miMonitor\Win64"
#define PortMonWin32Dir  "..\miPortMon\miMonitor\Release\Win32"
#define PortMonWin64Dir  "..\miPortMon\miMonitor\Release\x64"

; Downloadlinks fuer fehlende Voraussetzungen
#define VCRedistUrl      "https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist#visual-studio-2015-2017-2019-and-2022"
#define DotNetUrl        "https://dotnet.microsoft.com/download/dotnet/8.0/runtime"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppContact={#MyAppURL}
VersionInfoVersion={#MyAppVersion}
; Installation nach "Program Files (x86)\miPDF\miPDFconvert"
; (App ist x86 -> Setup laeuft im 32-Bit-Modus, {autopf} = Program Files (x86))
DefaultDirName={autopf32}\miPDF\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-Machine Installation, Adminrechte werden benoetigt (Treiber + COM)
PrivilegesRequired=admin
; Vorgaengerversion automatisch ersetzen (entspricht RemovePreviousVersions=TRUE)
UninstallDisplayIcon={app}\miPDFconvert.ico
UninstallDisplayName={#MyAppName}
SetupIconFile=miPDFconvert.ico
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=Release
OutputBaseFilename=miPDFconvertSetup_{#MyAppVersion}
; ARM64 wird (wie beim alten Setup) ueber die x64-Treiberdateien mitbedient

; Code-Signatur (nur wenn ISCC mit /DSIGN aufgerufen wird, z. B. build.ps1 -Sign).
; 'certum' ist ein in der Inno-Setup-IDE konfigurierter Sign-Tool-Name
; (Tools -> Configure Sign Tools). Der eigentliche signtool-Befehl inkl.
; Zertifikats-Subject liegt dort lokal - NICHT in diesem Repository.
#ifdef SIGN
SignTool=certum $f
SignedUninstaller=yes
#endif

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Files]
; --- Hauptanwendung: komplette Publish-Ausgabe nach {app} ---
;     enthaelt miPDFconvert.exe, miPDFconvertBase.exe, SetupHelper.exe,
;     gsdll32.dll, gsdll64.dll, alle abhaengigen DLLs, configs und runtimes\
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion; Excludes: "*.pdb"

; --- Programmsymbol ---
Source: "miPDFconvert.ico"; DestDir: "{app}"; Flags: ignoreversion

; --- Druckertreiber-Dateien: 32 Bit nach {app}\miMonitor\x86 ---
Source: "{#LibWin32Dir}\pscript5.dll";     DestDir: "{app}\miMonitor\x86"; Flags: ignoreversion
Source: "{#LibWin32Dir}\pscript.ntf";      DestDir: "{app}\miMonitor\x86"; Flags: ignoreversion
Source: "{#LibWin32Dir}\pscript.hlp";      DestDir: "{app}\miMonitor\x86"; Flags: ignoreversion
Source: "{#LibWin32Dir}\ps5ui.dll";        DestDir: "{app}\miMonitor\x86"; Flags: ignoreversion
Source: "{#LibWin32Dir}\ghostpdf.ppd";     DestDir: "{app}\miMonitor\x86"; Flags: ignoreversion
Source: "{#PortMonWin32Dir}\miMonitor.dll";   DestDir: "{app}\miMonitor\x86"; Flags: ignoreversion
Source: "{#PortMonWin32Dir}\miMonitorUI.dll"; DestDir: "{app}\miMonitor\x86"; Flags: ignoreversion

; --- Druckertreiber-Dateien: 64 Bit nach {app}\miMonitor\x64 ---
Source: "{#LibWin64Dir}\pscript5.dll";     DestDir: "{app}\miMonitor\x64"; Flags: ignoreversion
Source: "{#LibWin64Dir}\pscript.ntf";      DestDir: "{app}\miMonitor\x64"; Flags: ignoreversion
Source: "{#LibWin64Dir}\pscript.hlp";      DestDir: "{app}\miMonitor\x64"; Flags: ignoreversion
Source: "{#LibWin64Dir}\ps5ui.dll";        DestDir: "{app}\miMonitor\x64"; Flags: ignoreversion
Source: "{#LibWin64Dir}\ghostpdf.ppd";     DestDir: "{app}\miMonitor\x64"; Flags: ignoreversion
Source: "{#PortMonWin64Dir}\miMonitor.dll";   DestDir: "{app}\miMonitor\x64"; Flags: ignoreversion
Source: "{#PortMonWin64Dir}\miMonitorUI.dll"; DestDir: "{app}\miMonitor\x64"; Flags: ignoreversion

[Run]
; --- Reihenfolge wie im alten Setup (Install-Sequenz 1,2,3) ---
; 1) evtl. vorhandenen Treiber zuerst entfernen
Filename: "{app}\SetupHelper.exe"; Parameters: "/Driver=Remove"; \
    StatusMsg: "Entferne vorhandenen Druckertreiber..."; Flags: runhidden waituntilterminated
; 2) Druckertreiber installieren
Filename: "{app}\SetupHelper.exe"; Parameters: "/Driver=Add"; \
    StatusMsg: "Installiere Druckertreiber..."; Flags: runhidden waituntilterminated
; 3) optionale Zielanwendung in die Konfiguration schreiben
Filename: "{app}\SetupHelper.exe"; Parameters: "/TargetApp=""{code:GetTargetApp}"""; \
    StatusMsg: "Konfiguriere Zielanwendung..."; Check: HasTargetApp; Flags: runhidden waituntilterminated

[UninstallRun]
; --- Deinstallation (vor dem Loeschen der Dateien) ---
; Treiber entfernen
Filename: "{app}\SetupHelper.exe"; Parameters: "/Driver=Remove"; \
    RunOnceId: "RemoveDriver"; Flags: runhidden waituntilterminated

[UninstallDelete]
; Treiberordner restlos aufraeumen (falls SetupHelper Restdateien hinterlaesst)
Type: filesandordirs; Name: "{app}\miMonitor"

[Code]

var
  TargetPage: TInputFileWizardPage;

{ ---- Optionale Abfrage der Zielanwendung ---- }
procedure InitializeWizard;
begin
  TargetPage := CreateInputFilePage(wpSelectDir,
    'Zielanwendung',
    'An welche Anwendung soll die erzeugte PDF automatisch uebergeben werden?',
    'Optional: Waehlen Sie eine Anwendung, die die erzeugte PDF-Datei automatisch als Argument ' +
    'erhaelt (z. B. einen PDF-Viewer). Lassen Sie das Feld leer, um beim Drucken stattdessen ' +
    'einen "Speichern unter"-Dialog zu erhalten. Spaeter aenderbar in miPDFconvert.dll.config.');
  TargetPage.Add('Zielanwendung (optional):', 'Programme (*.exe)|*.exe|Alle Dateien (*.*)|*.*', '.exe');
end;

function GetTargetApp(Param: string): string;
begin
  Result := Trim(TargetPage.Values[0]);
end;

function HasTargetApp: Boolean;
begin
  Result := Trim(TargetPage.Values[0]) <> '';
end;

{ ---- Pruefung der Voraussetzungen vor der Installation ---- }

function VCRedistInstalled(): Boolean;
begin
  // VC++ 2015-2022 Redistributable - Laufzeit-DLL pruefen.
  // Setup laeuft 32-bit, daher wird der Systemordner auf SysWOW64 umgeleitet (x86-Runtime).
  Result := FileExists(ExpandConstant('{sys}\vcruntime140.dll'));
  { Zusaetzlich ueber Registry absichern (x86-Eintrag des Redist). }
  if not Result then
    Result := RegKeyExists(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x86') or
              RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x86');
end;

function HasDesktopRuntime8(BaseDir: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if DirExists(BaseDir) then
    if FindFirst(BaseDir + '\8.*', FindRec) then
    begin
      Result := True;
      FindClose(FindRec);
    end;
end;

function DotNet8DesktopInstalled(): Boolean;
begin
  { .NET 8 Desktop Runtime suchen. Die x86-App benoetigt die x86-Runtime
    (Program Files (x86)\dotnet); die x64-Runtime liegt in Program Files\dotnet. }
  Result := HasDesktopRuntime8(ExpandConstant('{commonpf32}\dotnet\shared\Microsoft.WindowsDesktop.App'));
  if not Result then
    Result := HasDesktopRuntime8(ExpandConstant('{sd}\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App'));
  { Registry-Fallback (vom .NET-Installer gesetzte InstalledVersions). }
  if not Result then
    Result := RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x86\sharedfx\Microsoft.WindowsDesktop.App') or
              RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x86\sharedfx\Microsoft.WindowsDesktop.App');
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  { --- Visual C++ Redistributable 14 --- }
  if not VCRedistInstalled() then
  begin
    { Unbeaufsichtigte Installation (z. B. winget: /VERYSILENT): keine Dialoge und
      KEIN Abbruch - sonst schlaegt der Silent-Install fehl. Nur protokollieren;
      die Laufzeitkomponente muss separat vorhanden sein bzw. als winget-Dependency
      installiert werden. }
    if WizardSilent() then
      Log('VC++ Redistributable 14 nicht gefunden - Silent-Installation, wird fortgesetzt.')
    else if MsgBox('Zur Ausfuehrung dieses Programms wird das Visual C++ Redistributable 14 ' +
              '(Visual Studio 2015-2022) benoetigt.' + #13#10#13#10 +
              'Moechten Sie jetzt die Downloadseite oeffnen? ' +
              '(Die Installation wird danach abgebrochen, damit Sie die Komponente nachinstallieren koennen.)',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', '{#VCRedistUrl}', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
      Result := False;
      Exit;
    end;
  end;

  { --- .NET 8 Desktop Runtime --- }
  if not DotNet8DesktopInstalled() then
  begin
    if WizardSilent() then
      Log('.NET 8 Desktop Runtime nicht gefunden - Silent-Installation, wird fortgesetzt.')
    else if MsgBox('Zur Ausfuehrung dieses Programms wird die .NET 8 Desktop Runtime benoetigt.' + #13#10#13#10 +
              'Moechten Sie jetzt die Downloadseite oeffnen? ' +
              '(Die Installation wird danach abgebrochen, damit Sie die Komponente nachinstallieren koennen.)',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', '{#DotNetUrl}', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
      Result := False;
      Exit;
    end;
  end;
end;
