<#
.SYNOPSIS
    Zentrales Build-Skript fuer miPDFConvert.

.DESCRIPTION
    Baut die gesamte Loesung in der richtigen Reihenfolge und erzeugt am Ende
    das Inno-Setup:

      1. Native Druckermonitor-DLLs (miMonitor, miMonitorUI) - Release Win32 + x64
      2. SetupHelper (.NET Framework)            -> build\publish
      3. miPDFConvert + miPDFConvertBase (Publish) -> build\publish
      4. Inno Setup (miPDFConvert.iss)           -> miPDFConvertSetup\Release\miPDFConvertSetup.exe

    MSBuild und der Inno-Compiler (ISCC.exe) werden automatisch gesucht.

.PARAMETER Configuration
    Build-Konfiguration (Standard: Release).

.PARAMETER SkipNative
    Ueberspringt die nativen C++-Projekte (z. B. wenn unveraendert).

.PARAMETER SkipSetup
    Ueberspringt den Inno-Setup-Schritt (nur Binaries bauen).

.PARAMETER IsccPath
    Optionaler expliziter Pfad zu ISCC.exe.

.PARAMETER PlatformToolset
    Ueberschreibt das PlatformToolset der nativen C++-Projekte (z. B. 'v144'
    oder 'v145'), falls das im Projekt eingestellte v143 (VS 2022) nicht
    installiert ist. Leer = im Projekt eingestellter Wert.

.EXAMPLE
    .\build.ps1
.EXAMPLE
    .\build.ps1 -SkipNative
.EXAMPLE
    .\build.ps1 -PlatformToolset v144
#>

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipNative,
    [switch]$SkipSetup,
    [string]$IsccPath,
    [string]$PlatformToolset
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
    # 1) bereits im PATH?
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    # 2) ueber vswhere (mit Visual Studio installiert)
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

function Ensure-DriverFiles {
    # Die Microsoft-PostScript-Treiberdateien (Pscript5) sind Microsoft-Komponenten und
    # werden NICHT im Repository mitgeliefert. Sie werden hier aus dem lokalen Windows-
    # DriverStore nach lib\miMonitor gestaged, bevor das Setup gepackt wird. Diese Dateien
    # sind auf jedem Windows vorhanden (Inbox-Treiber). Sind sie bereits vorhanden, passiert
    # nichts.
    $driverFiles = @('pscript5.dll', 'ps5ui.dll', 'pscript.hlp', 'pscript.ntf')
    $targets = @(
        [pscustomobject]@{ Dir = (Join-Path $root 'lib\miMonitor\Win32'); Arch = 'x86';   Sub = 'I386'  },
        [pscustomobject]@{ Dir = (Join-Path $root 'lib\miMonitor\Win64'); Arch = 'amd64'; Sub = 'Amd64' }
    )
    foreach ($t in $targets) {
        $missing = $driverFiles | Where-Object { -not (Test-Path (Join-Path $t.Dir $_)) }
        if (-not $missing) { continue }

        Write-Step "Stage Microsoft PostScript-Treiberdateien ($($t.Arch)) aus dem Windows-DriverStore"
        $repo = Get-ChildItem "$env:SystemRoot\System32\DriverStore\FileRepository" -Directory `
                    -Filter "ntprint.inf_$($t.Arch)_*" -ErrorAction SilentlyContinue |
                Sort-Object Name | Select-Object -First 1
        if (-not $repo) {
            throw ("Inbox-PostScript-Treiber (ntprint.inf_$($t.Arch)_*) im Windows-DriverStore nicht gefunden. " +
                   "Bitte diese Dateien manuell nach '$($t.Dir)' legen: " + ($driverFiles -join ', '))
        }
        $srcDir = Join-Path $repo.FullName $t.Sub
        if (-not (Test-Path $t.Dir)) { New-Item -ItemType Directory -Force -Path $t.Dir | Out-Null }
        foreach ($f in $driverFiles) {
            $src = Join-Path $srcDir $f
            if (-not (Test-Path $src)) {
                throw "Treiberdatei '$f' nicht gefunden unter '$src'. Bitte manuell nach '$($t.Dir)' legen."
            }
            Copy-Item $src (Join-Path $t.Dir $f) -Force
            Write-Host "    $f" -ForegroundColor DarkGray
        }
    }
}

# ---------------------------------------------------------------------------
#  Projektpfade
# ---------------------------------------------------------------------------
$nativeProjects = @(
    (Join-Path $root 'miPortMon\monitor\miMonitor.vcxproj'),
    (Join-Path $root 'miPortMon\monitorUI\miMonitorUI.vcxproj')
)
$setupHelper   = Join-Path $root 'miPDFSetupHelper\SetupHelper.csproj'
$appProjects   = @(
    (Join-Path $root 'miPDFConvert\miPDFConvert.csproj'),
    (Join-Path $root 'miPDFConvertBase\miPDFConvertBase.csproj')
)
$issFile       = Join-Path $root 'miPDFConvertSetup\miPDFConvert.iss'

$msbuild = Find-MSBuild
Write-Host "MSBuild: $msbuild" -ForegroundColor DarkGray

$sw = [System.Diagnostics.Stopwatch]::StartNew()

# ---------------------------------------------------------------------------
#  1. Native Druckermonitor-DLLs (Win32 + x64)
# ---------------------------------------------------------------------------
if (-not $SkipNative) {
    foreach ($proj in $nativeProjects) {
        foreach ($platform in @('Win32', 'x64')) {
            Write-Step "Baue $(Split-Path $proj -Leaf) [$Configuration|$platform]"
            $args = @(
                $proj,
                "/p:Configuration=$Configuration",
                "/p:Platform=$platform",
                '/t:Build',
                '/m',
                '/v:minimal',
                '/nologo'
            )
            if ($PlatformToolset) { $args += "/p:PlatformToolset=$PlatformToolset" }
            Invoke-Checked $msbuild $args 'Native Build'
        }
    }
} else {
    Write-Step 'Native Projekte uebersprungen (-SkipNative).'
}

# ---------------------------------------------------------------------------
#  2. SetupHelper (.NET Framework) -> build\publish
# ---------------------------------------------------------------------------
Write-Step 'Baue SetupHelper (.NET Framework)'
# Hinweis: beim direkten Bauen der .csproj muss der interne Plattformname
# 'AnyCPU' (ohne Leerzeichen) verwendet werden, damit die OutputPath-Bedingung
# 'Release|AnyCPU' in der csproj greift (ueber die Solution waere es 'Any CPU').
Invoke-Checked $msbuild @(
    $setupHelper,
    "/p:Configuration=$Configuration",
    '/p:Platform=AnyCPU',
    '/t:Restore;Build',
    '/m',
    '/v:minimal',
    '/nologo'
) 'SetupHelper Build'

# ---------------------------------------------------------------------------
#  3. .NET-Anwendungen veroeffentlichen (Publish-Profil) -> build\publish
# ---------------------------------------------------------------------------
foreach ($proj in $appProjects) {
    Write-Step "Publish $(Split-Path $proj -Leaf)"
    Invoke-Checked 'dotnet' @(
        'publish',
        $proj,
        '-c', $Configuration,
        '-p:PublishProfile=FolderProfile',
        '--nologo',
        '-v', 'minimal'
    ) 'dotnet publish'
}

# ---------------------------------------------------------------------------
#  4. Inno Setup kompilieren
# ---------------------------------------------------------------------------
if (-not $SkipSetup) {
    Ensure-DriverFiles
    $iscc = Find-Iscc
    Write-Host "ISCC: $iscc" -ForegroundColor DarkGray
    Write-Step 'Kompiliere Inno Setup'
    Invoke-Checked $iscc @($issFile) 'Inno Setup'
    $output = Join-Path $root 'miPDFConvertSetup\Release\miPDFConvertSetup.exe'
    if (Test-Path $output) {
        Write-Host ''
        Write-Host "Setup erstellt: $output" -ForegroundColor Green
    }
} else {
    Write-Step 'Inno-Setup-Schritt uebersprungen (-SkipSetup).'
}

$sw.Stop()
Write-Host ''
Write-Host ("Fertig in {0:n1} s." -f $sw.Elapsed.TotalSeconds) -ForegroundColor Green
