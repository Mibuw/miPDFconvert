<#
.SYNOPSIS
    Zentrales Build-Skript fuer miPDFconvert.

.DESCRIPTION
    Baut die gesamte Loesung in der richtigen Reihenfolge und erzeugt am Ende
    das Inno-Setup:

      1. Druckermonitor miPDFport.dll (C++)     - Release x64 + ARM64
      2. PrinterSetup.exe (.NET Framework)      -> build\PrinterSetup
      3. miPDFconvert (Publish)                 -> build\publish
      4. Inno Setup (miPDFconvert.iss)          -> miPDFconvertSetup\Release\miPDFconvertSetup_<Version>.exe

    MSBuild und der Inno-Compiler (ISCC.exe) werden automatisch gesucht.

.PARAMETER Configuration
    Build-Konfiguration (Standard: Release).

.PARAMETER SkipNative
    Ueberspringt den nativen Druckermonitor (z. B. wenn unveraendert).

.PARAMETER SkipSetup
    Ueberspringt den Inno-Setup-Schritt (nur Binaries bauen).

.PARAMETER IsccPath
    Optionaler expliziter Pfad zu ISCC.exe.

.PARAMETER PlatformToolset
    Ueberschreibt das PlatformToolset des Druckermonitors (z. B. 'v143'), falls
    das im Projekt eingestellte v145 nicht installiert ist.

.PARAMETER Sign
    Signiert die eigenen Binaries und das Setup mit einem Code-Signing-Zertifikat
    (Certum SimplySign) inkl. Zeitstempel. Erfordert eine verbundene SimplySign-
    Desktop-Session. Ohne diesen Schalter wird nichts signiert.

.PARAMETER SignSubject
    Subject-Name (CN) des Zertifikats fuer 'signtool /n'. Standard: Umgebungs-
    variable MIPDF_SIGN_SUBJECT. Wird bewusst NICHT im Skript hinterlegt, damit
    keine persoenlichen Daten im (oeffentlichen) Repository stehen.

.PARAMETER SignToolPath
    Optionaler expliziter Pfad zu signtool.exe (sonst automatische Suche im SDK).

.PARAMETER TimestampUrl
    Zeitstempel-Server fuer die Signatur (Standard: http://time.certum.pl).

.EXAMPLE
    .\build.ps1
.EXAMPLE
    .\build.ps1 -Sign -SignSubject "Wolfgang Mitterbucher"
#>

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipNative,
    [switch]$SkipSetup,
    [string]$IsccPath,
    [string]$PlatformToolset,
    [switch]$Sign,
    [string]$SignSubject = $env:MIPDF_SIGN_SUBJECT,
    [string]$SignToolPath,
    [string]$TimestampUrl = 'http://time.certum.pl'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# ---------------------------------------------------------------------------
#  Hilfsfunktionen
# ---------------------------------------------------------------------------
function Write-Step($text) {
    Write-Host ''
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Invoke-Checked($file, $arguments, $desc) {
    Write-Host "    $file $arguments" -ForegroundColor DarkGray
    & $file @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$desc fehlgeschlagen (Exit-Code $LASTEXITCODE)."
    }
}

function Find-MSBuild {
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $path = & $vswhere -latest -requires Microsoft.Component.MSBuild `
                   -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($path -and (Test-Path $path)) { return $path }
    }
    throw 'MSBuild.exe wurde nicht gefunden. Bitte Visual Studio Build Tools installieren oder MSBuild in den PATH aufnehmen.'
}

function Find-Iscc {
    if ($IsccPath) {
        if (Test-Path $IsccPath) { return $IsccPath }
        throw "ISCC.exe nicht gefunden unter '$IsccPath'."
    }
    $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles        'Inno Setup 6\ISCC.exe')
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw 'ISCC.exe (Inno Setup 6) wurde nicht gefunden. Mit -IsccPath angeben oder Inno Setup installieren (https://jrsoftware.org/isdl.php).'
}

function Get-SignTool {
    if ($SignToolPath) {
        if (Test-Path $SignToolPath) { return $SignToolPath }
        throw "signtool.exe nicht gefunden unter '$SignToolPath'."
    }
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $base = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $base) {
        $all = Get-ChildItem $base -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue
        $st = $all | Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
        if (-not $st) { $st = $all | Sort-Object FullName -Descending | Select-Object -First 1 }
        if ($st) { return $st.FullName }
    }
    throw 'signtool.exe nicht gefunden. Windows 10/11 SDK installieren oder -SignToolPath angeben.'
}

function Invoke-SignFiles([string[]]$files) {
    # Signiert die uebergebenen (existierenden) Dateien mit dem Certum-Zertifikat.
    # Der Subject-Name wird bewusst NICHT hier hinterlegt, sondern ueber -SignSubject
    # bzw. die Umgebungsvariable MIPDF_SIGN_SUBJECT hereingereicht.
    $existing = @($files | Where-Object { Test-Path $_ })
    if ($existing.Count -eq 0) { return }
    if (-not $SignSubject) {
        throw ("Signieren angefordert (-Sign), aber kein Zertifikats-Subject gesetzt. " +
               "Bitte -SignSubject '<Name>' angeben oder `$env:MIPDF_SIGN_SUBJECT setzen.")
    }
    $signtool = Get-SignTool
    Write-Step "Signiere $($existing.Count) Datei(en) (Certum SimplySign)"
    $signArgs = @('sign', '/tr', $TimestampUrl, '/td', 'sha256', '/fd', 'sha256', '/n', $SignSubject) + $existing
    & $signtool @signArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Signieren fehlgeschlagen (Exit-Code $LASTEXITCODE). Ist SimplySign Desktop verbunden und die Session freigeschaltet?"
    }
}

# ---------------------------------------------------------------------------
#  Projektpfade
# ---------------------------------------------------------------------------
$monitorProject = Join-Path $root 'printer\miPDFport\miPDFport.vcxproj'
$printerSetup   = Join-Path $root 'printer\PrinterSetup\PrinterSetup.csproj'
$appProject     = Join-Path $root 'miPDFconvert\miPDFconvert.csproj'
$issFile        = Join-Path $root 'miPDFconvertSetup\miPDFconvert.iss'

$msbuild = Find-MSBuild
Write-Host "MSBuild: $msbuild" -ForegroundColor DarkGray

$sw = [System.Diagnostics.Stopwatch]::StartNew()

# ---------------------------------------------------------------------------
#  1. Druckermonitor (x64 + ARM64)
# ---------------------------------------------------------------------------
if (-not $SkipNative) {
    foreach ($platform in @('x64', 'ARM64')) {
        Write-Step "Baue miPDFport.dll [$Configuration|$platform]"
        $args = @($monitorProject, "/p:Configuration=$Configuration", "/p:Platform=$platform", '/t:Build', '/m', '/v:minimal', '/nologo')
        if ($PlatformToolset) { $args += "/p:PlatformToolset=$PlatformToolset" }
        Invoke-Checked $msbuild $args 'Druckermonitor-Build'
    }
} else {
    Write-Step 'Druckermonitor uebersprungen (-SkipNative).'
}

# ---------------------------------------------------------------------------
#  2. PrinterSetup (.NET Framework)
# ---------------------------------------------------------------------------
Write-Step 'Baue PrinterSetup'
Invoke-Checked 'dotnet' @('build', $printerSetup, '-c', $Configuration, '--nologo', '-v', 'minimal') 'PrinterSetup-Build'

# ---------------------------------------------------------------------------
#  3. miPDFconvert veroeffentlichen (Publish-Profil) -> build\publish
# ---------------------------------------------------------------------------
Write-Step 'Publish miPDFconvert'
# Leeren, damit keine Reste frueherer Builds ins Setup gelangen
$publishDir = Join-Path $root 'build\publish'
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Invoke-Checked 'dotnet' @('publish', $appProject, '-c', $Configuration, '-p:PublishProfile=FolderProfile', '--nologo', '-v', 'minimal') 'dotnet publish'

# ---------------------------------------------------------------------------
#  3b. Optionale Code-Signatur der EIGENEN Binaries (vor dem Packen).
#      Fremd-DLLs (log4net, miPDFlib, System.*) werden nicht neu signiert.
# ---------------------------------------------------------------------------
if ($Sign) {
    Invoke-SignFiles @(
        (Join-Path $root 'build\publish\miPDFconvert.dll'),
        (Join-Path $root 'build\publish\miPDFconvert.exe'),
        (Join-Path $root "build\PrinterSetup\$Configuration\net472\PrinterSetup.exe"),
        (Join-Path $root 'printer\miPDFport\bin\x64\miPDFport.dll'),
        (Join-Path $root 'printer\miPDFport\bin\ARM64\miPDFport.dll')
    )
}

# ---------------------------------------------------------------------------
#  4. Inno Setup kompilieren
# ---------------------------------------------------------------------------
if (-not $SkipSetup) {
    $iscc = Find-Iscc
    Write-Host "ISCC: $iscc" -ForegroundColor DarkGray
    Write-Step 'Kompiliere Inno Setup'
    # Bei -Sign wird das ISPP-Symbol SIGN definiert -> die [Setup]-Direktiven
    # SignTool/SignedUninstaller in der .iss werden aktiv (nutzt den lokal
    # konfigurierten Inno-Sign-Tool-Namen 'certum').
    $isccArgs = @($issFile)
    if ($Sign) { $isccArgs += '/DSIGN' }
    Invoke-Checked $iscc $isccArgs 'Inno Setup'
} else {
    Write-Step 'Inno-Setup-Schritt uebersprungen (-SkipSetup).'
}

$sw.Stop()
Write-Host ''
Write-Host ("Fertig in {0:n1} s." -f $sw.Elapsed.TotalSeconds) -ForegroundColor Green
