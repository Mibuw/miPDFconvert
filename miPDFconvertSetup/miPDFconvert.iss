; ============================================================================
;  miPDFconvert - Inno Setup Script
;  Copyright (C) 2026 Wolfgang Mitterbucher (mitterbucher.com)
;  Licensed under the Apache License, Version 2.0 (see LICENSE).
;
;    * Installiert miPDFconvert (Publish-Ausgabe), den Druckermonitor
;      miPDFport.dll (x64/ARM64), die PPD und PrinterSetup.exe
;    * PrinterSetup.exe richtet den Drucker "miPDFconvert" ein; der PostScript-
;      Klassentreiber (pscript5) kommt aus dem Windows des Zielrechners
;    * Prueft die Voraussetzung .NET 8 Desktop Runtime
;    * Per-Machine Installation, Vorgaengerversionen (auch 1.x) werden ersetzt
;
;  Build:  ISCC.exe miPDFconvert.iss   (Inno Setup 6, https://jrsoftware.org/isdl.php)
; ============================================================================

#define MyAppName        "miPDFconvert"
#define MyAppVersion     "1.2.1"
#define MyAppPublisher   "Wolfgang Mitterbucher"
#define MyAppURL         "https://mitterbucher.com"
; UpgradeCode aus dem alten vdproj -> gleiche AppId sorgt fuer saubere Updates
#define MyAppId          "{{CC40866C-933B-4003-B1D2-73B281E517F9}"

; Relative Quellpfade (dieses Skript liegt in ...\miPDFconvert\miPDFconvertSetup)
#define PublishDir       "..\build\publish"
#define PrinterSetupDir  "..\build\PrinterSetup\Release\net472"
#define MonitorDir       "..\printer\miPDFport\bin"

; Downloadlink fuer fehlende Voraussetzung
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
; 64-Bit-Windows (x64 und ARM64)
ArchitecturesAllowed=x64compatible
; Installation nach "Program Files (x86)\miPDF\miPDFconvert" (die App ist x86)
DefaultDirName={autopf32}\miPDF\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-Machine Installation, Adminrechte werden benoetigt (Druckertreiber)
PrivilegesRequired=admin
UninstallDisplayIcon={app}\miPDFconvert.ico
UninstallDisplayName={#MyAppName}
SetupIconFile=miPDFconvert.ico
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=Release
OutputBaseFilename=miPDFconvertSetup_{#MyAppVersion}

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

[InstallDelete]
; Reste von miPDFconvert 1.x (Ghostscript, alter Druckermonitor, Hilfsprogramme)
Type: filesandordirs; Name: "{app}\gs"
Type: filesandordirs; Name: "{app}\miMonitor"
Type: files; Name: "{app}\SetupHelper.*"
Type: files; Name: "{app}\miPDFconvertBase.*"
Type: files; Name: "{app}\LICENSE.txt"

[Files]
; --- Anwendung: Publish-Ausgabe; eine vorhandene Konfiguration bleibt beim Update erhalten ---
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion; Excludes: "*.pdb,miPDFconvert.dll.config"
Source: "{#PublishDir}\miPDFconvert.dll.config"; DestDir: "{app}"; Flags: onlyifdoesntexist

; --- Drucker: Einrichtung, PPD und Druckermonitor ---
Source: "{#PrinterSetupDir}\PrinterSetup.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\printer\miPDFconvert.ppd"; DestDir: "{app}\printer"; Flags: ignoreversion
Source: "{#MonitorDir}\x64\miPDFport.dll"; DestDir: "{app}\printer\x64"; Flags: ignoreversion
Source: "{#MonitorDir}\ARM64\miPDFport.dll"; DestDir: "{app}\printer\ARM64"; Flags: ignoreversion

; --- Programmsymbol (LICENSE, NOTICE und THIRD-PARTY-NOTICES.md sind in der Publish-Ausgabe) ---
Source: "miPDFconvert.ico"; DestDir: "{app}"; Flags: ignoreversion

[UninstallRun]
Filename: "{app}\PrinterSetup.exe"; Parameters: "uninstall"; \
    RunOnceId: "RemovePrinter"; Flags: runhidden waituntilterminated

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\miPDFconvert\Spool"

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

function XmlEscape(Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '&', '&amp;', True);
  StringChangeEx(Result, '"', '&quot;', True);
  StringChangeEx(Result, '<', '&lt;', True);
  StringChangeEx(Result, '>', '&gt;', True);
end;

{ Schreibt die gewaehlte Zielanwendung nach TARGET_APPLICATION in miPDFconvert.dll.config. }
procedure SetTargetApplication(Target: String);
var
  ConfigFile: String;
  Lines: TArrayOfString;
  I, KeyPos, ValueStart, ValueEnd: Integer;
  Line: String;
begin
  ConfigFile := ExpandConstant('{app}\miPDFconvert.dll.config');
  if not LoadStringsFromFile(ConfigFile, Lines) then
    Exit;
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    Line := Lines[I];
    KeyPos := Pos('key="TARGET_APPLICATION"', Line);
    if KeyPos > 0 then
    begin
      ValueStart := KeyPos + Pos('value="', Copy(Line, KeyPos, MaxInt)) + Length('value="') - 1;
      ValueEnd := ValueStart + Pos('"', Copy(Line, ValueStart, MaxInt)) - 1;
      Lines[I] := Copy(Line, 1, ValueStart - 1) + XmlEscape(Target) + Copy(Line, ValueEnd, MaxInt);
      SaveStringsToUTF8File(ConfigFile, Lines, False);
      Exit;
    end;
  end;
end;

{ Richtet den Drucker ein; ein Fehler wird gemeldet (Details in PrinterSetup.log). }
procedure InstallPrinter;
var
  ResultCode: Integer;
begin
  WizardForm.StatusLabel.Caption := 'Richte den Drucker miPDFconvert ein...';
  if not Exec(ExpandConstant('{app}\PrinterSetup.exe'), 'install "' + ExpandConstant('{app}') + '"',
              ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
  begin
    Log('PrinterSetup install failed, exit code ' + IntToStr(ResultCode));
    if not WizardSilent() then
      MsgBox('Der Drucker miPDFconvert konnte nicht eingerichtet werden.' + #13#10 +
             'Details: ' + ExpandConstant('{commonappdata}\miPDFconvert\PrinterSetup.log'), mbError, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    InstallPrinter;
    if Trim(TargetPage.Values[0]) <> '' then
      SetTargetApplication(Trim(TargetPage.Values[0]));
  end;
end;

{ ---- Pruefung der Voraussetzungen vor der Installation ---- }

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

  { --- .NET 8 Desktop Runtime --- }
  if not DotNet8DesktopInstalled() then
  begin
    { Unbeaufsichtigte Installation (z. B. winget: /VERYSILENT): keine Dialoge und
      KEIN Abbruch - sonst schlaegt der Silent-Install fehl. Nur protokollieren. }
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
