<#
.SYNOPSIS
    Builds the pk2-mixer distribution artifacts.

.DESCRIPTION
    Produces:
      * Windows: portable self-contained single-file ZIP (no installer)
      * Linux:   AppImage (built with appimagetool) + portable .tar.gz
      * macOS:   pk2-mixer.app bundles per architecture (.zip, and .dmg on macOS)

    Linux AppImages are produced natively on Linux, or on Windows through WSL
    (appimagetool is downloaded automatically). macOS bundles are ad-hoc signed
    and packed on macOS; -AllowUnsignedMacOS enables a cross-built .app.zip for
    inspection (it must be signed once on a Mac before it will launch).

    Artifacts land in dist/ and dist/SHA256SUMS.txt is refreshed at the end.

.EXAMPLE
    pwsh tools/dist.ps1                     # native target(s) for this host
    pwsh tools/dist.ps1 -Targets windows    # portable Windows ZIP only
    pwsh tools/dist.ps1 -Targets linux      # AppImage + tar.gz (WSL on Windows)
    pwsh tools/dist.ps1 -Targets macos      # .app/.zip/.dmg (run on macOS)
    pwsh tools/dist.ps1 -Targets all        # everything this host can build
#>
[CmdletBinding()]
param(
    [string[]]$Targets = @(),
    [string]$Configuration = 'Release',
    [string]$Version,
    [string]$OutputRoot,
    [string[]]$WindowsArchitectures = @('x64'),
    [string[]]$LinuxArchitectures = @('x64'),
    [string[]]$MacArchitectures = @('arm64', 'x64'),
    [string]$WslDistro,
    [switch]$SkipAppImage,
    [switch]$SkipArchive,
    [switch]$AllowUnsignedMacOS,
    [switch]$NoClean
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $OutputRoot) {
    $OutputRoot = Join-Path $PSScriptRoot '..\dist'
}

$script:Root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$script:AppProject = Join-Path $script:Root 'src/Pk2Mixer.App/Pk2Mixer.App.csproj'
$script:OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$script:Icon = Join-Path $script:Root 'assets/pk2-mixer.png'
$script:Notices = Join-Path $script:Root 'assets/THIRD-PARTY-NOTICES.txt'
$script:DistReadme = Join-Path $script:Root 'assets/DIST-README.txt'
$script:Utf8NoBom = [System.Text.UTF8Encoding]::new($false)

function Get-HostOS {
    if ($env:OS -eq 'Windows_NT') { return 'windows' }
    if ($PSVersionTable.ContainsKey('Platform') -and $PSVersionTable['Platform'] -eq 'Unix') {
        if ($IsMacOS) { return 'macos' }
        return 'linux'
    }
    return 'windows'
}

function Write-Step([string]$message) {
    Write-Host "[dist] $message" -ForegroundColor Cyan
}

function Write-Warn([string]$message) {
    Write-Host "[dist] WARNING: $message" -ForegroundColor Yellow
}

function Invoke-Native([string]$exe, [string[]]$arguments, [string]$what) {
    Write-Step $what
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$what failed (exit $LASTEXITCODE)"
    }
}

function Get-AppVersion {
    if ($Version) { return $Version }
    try {
        $value = & dotnet msbuild $script:AppProject -getProperty:Version -nologo
        if ($LASTEXITCODE -eq 0 -and $value) {
            $text = ($value | Out-String).Trim()
            if ($text) { return $text }
        }
    } catch {
        # fall back to the props file below
    }
    [xml]$props = Get-Content (Join-Path $script:Root 'Directory.Build.props')
    return $props.Project.PropertyGroup.Version
}

function Publish-App([string]$rid, [string]$outDir, [bool]$singleFile) {
    if (Test-Path $outDir) {
        Remove-Item $outDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $arguments = @(
        'publish', $script:AppProject,
        '-c', $Configuration,
        '-r', $rid,
        '--self-contained', 'true',
        '-o', $outDir,
        ("-p:Version={0}" -f $script:Version),
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-p:ContinuousIntegrationBuild=true'
    )
    if ($singleFile) {
        $arguments += @(
            '-p:PublishSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:EnableCompressionInSingleFile=true'
        )
    } else {
        $arguments += '-p:PublishSingleFile=false'
    }

    Invoke-Native 'dotnet' $arguments "publishing $rid (single-file=$singleFile)"
}

function Copy-CommonDocs([string]$directory) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    Copy-Item (Join-Path $script:Root 'README.md') (Join-Path $directory 'README.md') -Force
    Copy-Item $script:Notices (Join-Path $directory 'THIRD-PARTY-NOTICES.txt') -Force
    Copy-Item $script:DistReadme (Join-Path $directory 'DISTRIBUTION.txt') -Force
}

function New-ZipWithUnixModes {
    param(
        [string]$SourceDirectory,
        [string]$ZipPath,
        [string[]]$Executables = @()
    )
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path $ZipPath) {
        Remove-Item $ZipPath -Force
    }

    $root = [System.IO.Path]::GetFullPath($SourceDirectory)
    $archive = [System.IO.Compression.ZipFile]::Open(
        $ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $files = Get-ChildItem -Path $root -Recurse -File
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($root.Length).TrimStart([char[]]@('\', '/')) -replace '\\', '/'
            $entry = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $file.FullName, $relative)
            # 0100644 or 0100755, stored in the high 16 bits (as zip tools do)
            $mode = 0x81A4
            if ($Executables -contains $relative) {
                $mode = 0x81ED
            }
            $entry.ExternalAttributes = [System.BitConverter]::ToInt32(
                [System.BitConverter]::GetBytes([int64]$mode * 65536), 0)
        }
    } finally {
        $archive.Dispose()
    }
}

function Set-ZipUnixHost([string]$ZipPath) {
    # .NET writes "version made by = MS-DOS" even when ExternalAttributes hold
    # Unix modes, and unzip then ignores the permission bits. Flip the host byte
    # of every central-directory record to 3 (Unix) so macOS unzip restores +x.
    $bytes = [System.IO.File]::ReadAllBytes($ZipPath)
    $eocd = -1
    for ($i = $bytes.Length - 22; $i -ge 0; $i--) {
        if ($bytes[$i] -eq 0x50 -and $bytes[$i + 1] -eq 0x4B -and
            $bytes[$i + 2] -eq 0x05 -and $bytes[$i + 3] -eq 0x06) {
            $eocd = $i
            break
        }
    }
    if ($eocd -lt 0) {
        throw "zip end-of-central-directory record not found: $ZipPath"
    }

    $count = [System.BitConverter]::ToUInt16($bytes, $eocd + 10)
    $offset = [System.BitConverter]::ToUInt32($bytes, $eocd + 16)
    for ($entry = 0; $entry -lt $count; $entry++) {
        if ([System.BitConverter]::ToUInt32($bytes, $offset) -ne 0x02014b50) {
            throw "bad zip central-directory record at $offset"
        }
        $bytes[$offset + 5] = 3   # high byte of "version made by" = Unix
        $nameLength = [System.BitConverter]::ToUInt16($bytes, $offset + 28)
        $extraLength = [System.BitConverter]::ToUInt16($bytes, $offset + 30)
        $commentLength = [System.BitConverter]::ToUInt16($bytes, $offset + 32)
        $offset += 46 + $nameLength + $extraLength + $commentLength
    }
    [System.IO.File]::WriteAllBytes($ZipPath, $bytes)
}

function Get-WslDistro {
    if ($script:HostOS -ne 'windows') { return $null }
    if ($WslDistro) { return $WslDistro }
    try {
        $output = & wsl.exe -l -q 2>$null
        if ($LASTEXITCODE -ne 0) { return $null }
        foreach ($line in $output) {
            $name = ($line -replace "`0", '').Trim()
            if ($name) { return $name }
        }
    } catch {
        return $null
    }
    return $null
}

function ConvertTo-WslPath([string]$path) {
    $full = [System.IO.Path]::GetFullPath($path)
    if ($full -match '^([A-Za-z]):\\(.*)$') {
        $drive = $Matches[1].ToLowerInvariant()
        $rest = $Matches[2] -replace '\\', '/'
        return "/mnt/$drive/$rest"
    }
    return ($full -replace '\\', '/')
}

function Quote-Bash([string]$value) {
    return "'" + ($value -replace "'", "'\''") + "'"
}

function Invoke-WslBash([string]$distro, [string]$command) {
    & wsl.exe -d $distro -- bash -lc $command
    if ($LASTEXITCODE -ne 0) {
        throw "WSL command failed (exit $LASTEXITCODE): $command"
    }
}

function New-AppDir([string]$appDir, [string]$publishDir) {
    if (Test-Path $appDir) {
        Remove-Item $appDir -Recurse -Force
    }
    $bin = Join-Path $appDir 'usr/bin'
    New-Item -ItemType Directory -Force -Path $bin | Out-Null
    Copy-Item (Join-Path $publishDir '*') $bin -Recurse -Force

    $publishedExe = Join-Path $bin 'Pk2Mixer'
    if (Test-Path $publishedExe) {
        Move-Item $publishedExe (Join-Path $bin 'pk2-mixer')
    }
    if (-not (Test-Path (Join-Path $bin 'pk2-mixer'))) {
        throw "linux publish did not produce a Pk2Mixer executable"
    }

    $appRun = @"
#!/bin/sh
HERE="`$(dirname "`$(readlink -f "`$0")")"
exec "`$HERE/usr/bin/pk2-mixer" "`$@"
"@
    [System.IO.File]::WriteAllText((Join-Path $appDir 'AppRun'),
        $appRun.Replace("`r`n", "`n"), $script:Utf8NoBom)

    $desktop = @"
[Desktop Entry]
Type=Application
Name=pk2-mixer
Comment=Manage Railroad Tycoon II PK2 archives
Exec=pk2-mixer
Icon=pk2-mixer
Categories=Utility;
Terminal=false
StartupWMClass=pk2-mixer
"@
    [System.IO.File]::WriteAllText((Join-Path $appDir 'pk2-mixer.desktop'),
        $desktop.Replace("`r`n", "`n"), $script:Utf8NoBom)

    $applications = Join-Path $appDir 'usr/share/applications'
    $icons = Join-Path $appDir 'usr/share/icons/hicolor/256x256/apps'
    New-Item -ItemType Directory -Force -Path $applications | Out-Null
    New-Item -ItemType Directory -Force -Path $icons | Out-Null
    Copy-Item $script:Icon (Join-Path $appDir 'pk2-mixer.png') -Force
    Copy-Item $script:Icon (Join-Path $icons 'pk2-mixer.png') -Force
    Copy-Item (Join-Path $appDir 'pk2-mixer.desktop') (Join-Path $applications 'pk2-mixer.desktop') -Force

    $docs = Join-Path $appDir 'usr/share/doc/pk2-mixer'
    Copy-CommonDocs $docs
}

function New-WindowsPackage([string]$arch) {
    $rid = "win-$arch"
    $outDir = Join-Path $script:OutputRoot $rid
    if (-not $NoClean -and (Test-Path $outDir)) {
        Remove-Item $outDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $publish = Join-Path $outDir 'publish'
    Publish-App $rid $publish $true
    $exe = Join-Path $publish 'Pk2Mixer.exe'
    if (-not (Test-Path $exe)) {
        throw "Windows publish did not produce Pk2Mixer.exe"
    }

    $stage = Join-Path $outDir 'package'
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Copy-Item $exe (Join-Path $stage 'Pk2Mixer.exe')
    Copy-CommonDocs $stage

    $zip = Join-Path $outDir ("pk2-mixer-{0}-win-{1}-portable.zip" -f $script:Version, $arch)
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Step "wrote $zip"
}

function New-LinuxPackage([string]$arch) {
    $rid = "linux-$arch"
    $outDir = Join-Path $script:OutputRoot $rid
    if (-not $NoClean -and (Test-Path $outDir)) {
        Remove-Item $outDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $publish = Join-Path $outDir 'publish'
    Publish-App $rid $publish $false
    $appDir = Join-Path $outDir 'Pk2Mixer.AppDir'
    New-AppDir $appDir $publish

    $wsl = $null
    if ($script:HostOS -eq 'windows') {
        $wsl = Get-WslDistro
    }

    $appImage = Join-Path $outDir ("pk2-mixer-{0}-linux-{1}.AppImage" -f $script:Version, $arch)
    if ($SkipAppImage) {
        Write-Step "AppImage skipped (-SkipAppImage)"
    } elseif ($script:HostOS -eq 'linux') {
        Invoke-Native 'bash' @((Join-Path $PSScriptRoot 'make-appimage.sh'), $appDir, $appImage) 'building AppImage'
    } elseif ($wsl) {
        Write-Step "building AppImage through WSL ($wsl)"
        $command = 'bash ' + (Quote-Bash (ConvertTo-WslPath (Join-Path $PSScriptRoot 'make-appimage.sh'))) +
                   ' ' + (Quote-Bash (ConvertTo-WslPath $appDir)) +
                   ' ' + (Quote-Bash (ConvertTo-WslPath $appImage))
        Invoke-WslBash $wsl $command
    } else {
        Write-Warn ("AppImage not built (no Linux host or WSL distro). Run " +
            "'tools/make-appimage.sh `"$appDir`" `"$appImage`"' on Linux.")
    }

    if (-not $SkipArchive) {
        $tarball = Join-Path $outDir ("pk2-mixer-{0}-linux-{1}.tar.gz" -f $script:Version, $arch)
        if ($script:HostOS -eq 'linux') {
            Push-Location $publish
            try {
                Invoke-Native 'tar' @('-czf', $tarball, '.') 'creating portable tar.gz'
            } finally {
                Pop-Location
            }
            Write-Step "wrote $tarball"
        } elseif ($wsl) {
            $publishWsl = Quote-Bash (ConvertTo-WslPath $publish)
            $tarWsl = Quote-Bash (ConvertTo-WslPath $tarball)
            $command = "rm -rf /tmp/pk2mixer-tar && mkdir -p /tmp/pk2mixer-tar && " +
                       "cp -a $publishWsl/. /tmp/pk2mixer-tar/ && " +
                       "chmod 755 /tmp/pk2mixer-tar/Pk2Mixer && " +
                       "find /tmp/pk2mixer-tar -type f ! -name Pk2Mixer -exec chmod 644 {} + && " +
                       "tar -C /tmp/pk2mixer-tar -czf $tarWsl . && rm -rf /tmp/pk2mixer-tar"
            Invoke-WslBash $wsl $command
            Write-Step "wrote $tarball"
        } else {
            Write-Warn "portable tar.gz skipped (no Linux host or WSL distro)"
        }
    }
}

function New-MacPackage([string]$arch) {
    $rid = "osx-$arch"
    $outDir = Join-Path $script:OutputRoot $rid

    if ($script:HostOS -ne 'macos' -and -not $AllowUnsignedMacOS) {
        Write-Warn ("macOS $arch skipped: build on macOS (ad-hoc signing needed) or " +
            "pass -AllowUnsignedMacOS to create an unsigned test bundle.")
        return
    }

    if (-not $NoClean -and (Test-Path $outDir)) {
        Remove-Item $outDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $publish = Join-Path $outDir 'publish'
    Publish-App $rid $publish $false

    if ($script:HostOS -eq 'macos') {
        Invoke-Native 'bash' @(
            (Join-Path $PSScriptRoot 'make-macos-bundle.sh'),
            $publish, $outDir, $script:Version, $arch, $script:Icon
        ) "building macOS .app/.zip/.dmg ($arch)"
        return
    }

    $stage = Join-Path $outDir 'package'
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    $appRoot = Join-Path $stage 'pk2-mixer.app'
    $macosDir = Join-Path $appRoot 'Contents/MacOS'
    $resources = Join-Path $appRoot 'Contents/Resources'
    New-Item -ItemType Directory -Force -Path $macosDir | Out-Null
    New-Item -ItemType Directory -Force -Path $resources | Out-Null
    Copy-Item (Join-Path $publish '*') $macosDir -Recurse -Force

    $publishedExe = Join-Path $macosDir 'Pk2Mixer'
    if (Test-Path $publishedExe) {
        Move-Item $publishedExe (Join-Path $macosDir 'pk2-mixer')
    } else {
        throw "macOS publish did not produce a Pk2Mixer executable"
    }
    Copy-Item $script:Icon (Join-Path $resources 'pk2-mixer.png') -Force
    Copy-CommonDocs (Join-Path $appRoot 'Contents/Resources/docs')

    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key><string>en</string>
    <key>CFBundleExecutable</key><string>pk2-mixer</string>
    <key>CFBundleIdentifier</key><string>io.github.pk2mixer</string>
    <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
    <key>CFBundleName</key><string>pk2-mixer</string>
    <key>CFBundleDisplayName</key><string>pk2-mixer</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>$script:Version</string>
    <key>CFBundleVersion</key><string>$script:Version</string>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
"@
    [System.IO.File]::WriteAllText((Join-Path $appRoot 'Contents/Info.plist'),
        $plist.Replace("`r`n", "`n"), $script:Utf8NoBom)

    $zip = Join-Path $outDir ("pk2-mixer-{0}-macos-{1}.zip" -f $script:Version, $arch)
    New-ZipWithUnixModes -SourceDirectory $stage -ZipPath $zip `
        -Executables @('pk2-mixer.app/Contents/MacOS/pk2-mixer')
    Set-ZipUnixHost $zip
    Write-Step "wrote $zip (unsigned cross-build; run codesign on macOS before distribution)"
}

function Update-Checksums {
    New-Item -ItemType Directory -Force -Path $script:OutputRoot | Out-Null
    Copy-Item $script:DistReadme (Join-Path $script:OutputRoot 'README.txt') -Force

    $files = Get-ChildItem -Path $script:OutputRoot -Recurse -File |
        Where-Object { $_.Name -match '\.(zip|AppImage|dmg|tar\.gz)$' } |
        Sort-Object FullName
    $lines = New-Object 'System.Collections.Generic.List[string]'
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($script:OutputRoot.Length).
            TrimStart([char[]]@('\', '/')) -replace '\\', '/'
        $hash = (Get-FileHash -Algorithm SHA256 -Path $file.FullName).Hash.ToLowerInvariant()
        $lines.Add("$hash  $relative")
    }
    [System.IO.File]::WriteAllLines((Join-Path $script:OutputRoot 'SHA256SUMS.txt'),
        $lines, $script:Utf8NoBom)
    Write-Step "SHA256SUMS.txt:"
    foreach ($line in $lines) {
        Write-Host "  $line"
    }
}

# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------
$script:HostOS = Get-HostOS
$script:Version = Get-AppVersion

# `powershell -File` passes "-Targets windows,linux" as one string, so accept
# both comma-separated and separate arguments.
$Targets = @($Targets |
    ForEach-Object { $_ -split ',' } |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ })
foreach ($target in $Targets) {
    if (@('windows', 'linux', 'macos', 'all') -notcontains $target) {
        throw "Unknown target '$target' (expected windows, linux, macos or all)"
    }
}

if (-not $Targets -or $Targets.Count -eq 0) {
    switch ($script:HostOS) {
        'windows' { $Targets = @('windows', 'linux') }
        'linux' { $Targets = @('linux') }
        'macos' { $Targets = @('macos') }
    }
}
if ($Targets -contains 'all') {
    $Targets = @('windows', 'linux', 'macos')
}
$Targets = @($Targets | Select-Object -Unique)

New-Item -ItemType Directory -Force -Path $script:OutputRoot | Out-Null
Write-Step "pk2-mixer $script:Version on $script:HostOS - targets: $($Targets -join ', ')"
Write-Step "output: $script:OutputRoot"

foreach ($target in $Targets) {
    switch ($target) {
        'windows' {
            foreach ($arch in $WindowsArchitectures) { New-WindowsPackage $arch }
        }
        'linux' {
            foreach ($arch in $LinuxArchitectures) { New-LinuxPackage $arch }
        }
        'macos' {
            foreach ($arch in $MacArchitectures) { New-MacPackage $arch }
        }
    }
}

Update-Checksums
Write-Step "done"
