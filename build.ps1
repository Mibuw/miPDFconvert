<#
.SYNOPSIS
    Zentrales Build-Skript fuer miPDFconvert.

.DESCRIPTION
    Baut die gesamte Loesung in der richtigen Reihenfolge und erzeugt am Ende
    das Inno-Setup:

      1. Native Druckermonitor-DLLs (miMonitor, miMonitorUI) - Release Win32 + x64
      2. SetupHelper (.NET Framework)            -> build\publish
      3. miPDFconvert + miPDFconvertBase (Publish) -> build\publish
      4. Ghostscript (offizielle Artifex-Binaries, unveraendert) -> build\ghostscript
      5. Inno Setup (miPDFconvert.iss)           -> miPDFconvertSetup\Release\miPDFconvertSetup.exe

    MSBuild, der Inno-Compiler (ISCC.exe) und 7-Zip werden automatisch gesucht.

.PARAMETER Configuration
    Build-Konfiguration (Standard: Release).

.PARAMETER SkipNative
    Ueberspringt die nativen C++-Projekte (z. B. wenn unveraendert).

.PARAMETER SkipSetup
    Ueberspringt den Inno-Setup-Schritt (nur Binaries bauen).

.PARAMETER IsccPath
    Optionaler expliziter Pfad zu ISCC.exe.

.PARAMETER PlatformToolset
    Ueberschreibt das PlatformToolset der nativen C++-Projekte (z. B. 'v143'
    oder 'v144'), falls das im Projekt eingestellte v145 nicht installiert ist.
    Leer = im Projekt eingestellter Wert (v145).

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
    .\build.ps1 -SkipNative
.EXAMPLE
    .\build.ps1 -PlatformToolset v144
.EXAMPLE
    .\build.ps1 -Sign -SignSubject "Wolfgang Mitterbucher"
.EXAMPLE
    $env:MIPDF_SIGN_SUBJECT = "Wolfgang Mitterbucher"; .\build.ps1 -Sign
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
#  Mitgeliefertes Ghostscript (AGPL-3.0) - feste Version, per SHA256 geprueft.
#  Bei einem Ghostscript-Update (v. a. Sicherheits-Fixes) nur diesen Block
#  anpassen. Hashes stehen im jeweiligen Release unter
#  https://github.com/ArtifexSoftware/ghostpdl-downloads/releases
# ---------------------------------------------------------------------------
$GhostscriptVersion = '10.08.0'
$GhostscriptTag     = 'gs10080'
$GhostscriptFiles   = @{
    'gs10080w64.exe'             = '52a91b8bf09298788d7a57b9206127026c23eacd75405f0a131e26dc381dce50'
    'gs10080w32.exe'             = '42f9e97ea2580bc24e2604cc03a2213bdba6094869dbe9f8ede50f8dd86b7463'
    # Quellcode - wird als "Corresponding Source" (AGPL) mit dem Release veroeffentlicht
    'ghostscript-10.08.0.tar.xz' = 'c20492bc8ebb96c87fa2e52a0926e1cda8cde95d66145e018ac713fed5da38cf'
}

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

function Find-7Zip {
    $cmd = Get-Command 7z.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($c in @((Join-Path $env:ProgramFiles '7-Zip\7z.exe'), (Join-Path ${env:ProgramFiles(x86)} '7-Zip\7z.exe'))) {
        if (Test-Path $c) { return $c }
    }
    throw '7z.exe (7-Zip) wurde nicht gefunden. Wird zum Entpacken der Ghostscript-Installer benoetigt (https://www.7-zip.org).'
}

function Find-VCRedistCrt([string]$arch) {
    # App-lokale VC++-Laufzeit fuer Ghostscript aus dem Redist-Ordner von Visual Studio
    # (offiziell weitergebbare Dateien) - so laeuft das mitgelieferte Ghostscript auch
    # ohne systemweit installiertes VC++ Redistributable der passenden Bitness.
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $vsPath = & $vswhere -latest -products * -property installationPath | Select-Object -First 1
        if ($vsPath) {
            $crt = Get-ChildItem (Join-Path $vsPath 'VC\Redist\MSVC') -Directory -ErrorAction SilentlyContinue |
                   Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
                   Sort-Object { [version]$_.Name } -Descending |
                   ForEach-Object { Get-ChildItem (Join-Path $_.FullName $arch) -Directory -Filter 'Microsoft.VC*.CRT' -ErrorAction SilentlyContinue } |
                   Select-Object -First 1
            if ($crt) { return $crt.FullName }
        }
    }
    throw "VC++-Redist-Dateien ($arch) nicht gefunden. Bitte Visual Studio mit C++-Workload installieren."
}

function Ensure-Ghostscript {
    # Laedt die offiziellen Ghostscript-Installer (fest eingestellte Version, SHA256-geprueft)
    # in einen Cache unter build\ghostscript\download und entpackt daraus nur die Dateien,
    # die zur Laufzeit gebraucht werden: gswinXXc.exe + gsdllXX.dll (Init-Dateien und
    # Fonts sind dort einkompiliert) sowie den Lizenztext. Die Binaries bleiben unveraendert.
    $gsRoot   = Join-Path $root 'build\ghostscript'
    $cacheDir = Join-Path $gsRoot 'download'
    if (-not (Test-Path $cacheDir)) { New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null }

    Write-Step "Ghostscript $GhostscriptVersion bereitstellen"
    foreach ($file in $GhostscriptFiles.Keys) {
        $target = Join-Path $cacheDir $file
        if (-not (Test-Path $target)) {
            $url = "https://github.com/ArtifexSoftware/ghostpdl-downloads/releases/download/$GhostscriptTag/$file"
            Write-Host "    Download $url" -ForegroundColor DarkGray
            Invoke-WebRequest -Uri $url -OutFile "$target.part" -UseBasicParsing
            Move-Item "$target.part" $target -Force
        }
        $hash = (Get-FileHash $target -Algorithm SHA256).Hash
        if ($hash -ne $GhostscriptFiles[$file]) {
            Remove-Item $target -Force
            throw "SHA256 von '$file' stimmt nicht (erwartet $($GhostscriptFiles[$file]), erhalten $hash). Datei wurde verworfen."
        }
    }

    $sevenZip = Find-7Zip
    $variants = @(
        [pscustomobject]@{ Arch = 'x64'; Installer = "${GhostscriptTag}w64.exe"; Exe = 'gswin64c.exe'; Dll = 'gsdll64.dll'
                           Crt = @('vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll') },
        [pscustomobject]@{ Arch = 'x86'; Installer = "${GhostscriptTag}w32.exe"; Exe = 'gswin32c.exe'; Dll = 'gsdll32.dll'
                           Crt = @('vcruntime140.dll', 'msvcp140.dll') }
    )
    foreach ($v in $variants) {
        $outDir = Join-Path $gsRoot $v.Arch
        if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null

        Invoke-Checked $sevenZip @('e', '-y', "-o$outDir", (Join-Path $cacheDir $v.Installer),
                                   "bin\$($v.Exe)", "bin\$($v.Dll)") "Entpacken von $($v.Installer)"
        foreach ($f in @($v.Exe, $v.Dll)) {
            if (-not (Test-Path (Join-Path $outDir $f))) { throw "'$f' fehlt nach dem Entpacken von $($v.Installer)." }
        }

        $crtDir = Find-VCRedistCrt $v.Arch
        foreach ($f in $v.Crt) { Copy-Item (Join-Path $crtDir $f) $outDir -Force }
        Write-Host "    $($v.Arch): $($v.Exe), $($v.Dll), $($v.Crt -join ', ')" -ForegroundColor DarkGray
    }

    # Lizenztext (identisch in beiden Installern) und Herkunfts-/Quellcode-Hinweis
    Invoke-Checked $sevenZip @('e', '-y', "-o$gsRoot", (Join-Path $cacheDir "${GhostscriptTag}w64.exe"), 'doc\COPYING') 'Entpacken von COPYING'
    @"
Ghostscript $GhostscriptVersion
===================

This folder contains the UNMODIFIED Ghostscript $GhostscriptVersion binaries
(gswin64c.exe/gsdll64.dll or gswin32c.exe/gsdll32.dll) taken from the official
Artifex Software Windows installers, plus the Microsoft Visual C++ runtime DLLs
they need. miPDFconvert runs Ghostscript as a separate process.

Copyright (C) Artifex Software, Inc. - https://www.ghostscript.com/
License: GNU Affero General Public License v3 (see COPYING in this folder).

Complete corresponding source code for exactly this version:
  https://github.com/Mibuw/miPDFconvert/releases  (asset ghostscript-$GhostscriptVersion.tar.xz)
  https://github.com/ArtifexSoftware/ghostpdl-downloads/releases/tag/$GhostscriptTag
"@ | Set-Content -Path (Join-Path $gsRoot 'README.txt') -Encoding ascii
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
$nativeProjects = @(
    (Join-Path $root 'miPortMon\monitor\miMonitor.vcxproj'),
    (Join-Path $root 'miPortMon\monitorUI\miMonitorUI.vcxproj')
)
$setupHelper   = Join-Path $root 'miPDFSetupHelper\SetupHelper.csproj'
$appProjects   = @(
    (Join-Path $root 'miPDFconvert\miPDFconvert.csproj'),
    (Join-Path $root 'miPDFconvertBase\miPDFconvertBase.csproj')
)
$issFile       = Join-Path $root 'miPDFconvertSetup\miPDFconvert.iss'

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
#  3b. Optionale Code-Signatur der EIGENEN Binaries (vor dem Packen)
#      Fremd-DLLs (log4net, System.*, VC++-Runtime) sind bereits vom jeweiligen
#      Hersteller signiert und werden nicht neu signiert. Die Ghostscript-
#      Binaries stammen von Artifex und werden bewusst NICHT mit dem eigenen
#      Zertifikat signiert.
# ---------------------------------------------------------------------------
if ($Sign) {
    Invoke-SignFiles @(
        (Join-Path $root 'build\publish\miPDFconvert.dll'),
        (Join-Path $root 'build\publish\miPDFconvert.exe'),
        (Join-Path $root 'build\publish\miPDFconvertBase.dll'),
        (Join-Path $root 'build\publish\miPDFconvertBase.exe'),
        (Join-Path $root 'build\publish\SetupHelper.exe'),
        (Join-Path $root 'miPortMon\miMonitor\Release\Win32\miMonitor.dll'),
        (Join-Path $root 'miPortMon\miMonitor\Release\Win32\miMonitorUI.dll'),
        (Join-Path $root 'miPortMon\miMonitor\Release\x64\miMonitor.dll'),
        (Join-Path $root 'miPortMon\miMonitor\Release\x64\miMonitorUI.dll')
    )
}

# ---------------------------------------------------------------------------
#  4./5. Ghostscript bereitstellen und Inno Setup kompilieren
# ---------------------------------------------------------------------------
if (-not $SkipSetup) {
    Ensure-DriverFiles
    Ensure-Ghostscript
    $iscc = Find-Iscc
    Write-Host "ISCC: $iscc" -ForegroundColor DarkGray
    Write-Step 'Kompiliere Inno Setup'
    # Bei -Sign wird das ISPP-Symbol SIGN definiert -> die [Setup]-Direktiven
    # SignTool/SignedUninstaller in der .iss werden aktiv (nutzt den lokal
    # konfigurierten Inno-Sign-Tool-Namen 'certum').
    $isccArgs = @($issFile)
    if ($Sign) { $isccArgs += '/DSIGN' }
    Invoke-Checked $iscc $isccArgs 'Inno Setup'
    $output = Join-Path $root 'miPDFconvertSetup\Release\miPDFconvertSetup.exe'
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
