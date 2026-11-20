[CmdletBinding()]
param(
    [string]$GameDir = 'E:\SteamLibrary\steamapps\common\Easy Red 2',
    [string]$ExpectedDllHash
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Version.props') -Raw)).Project.PropertyGroup.ModVersion
$zipPath = Join-Path $projectRoot "releases\ER2RealismOverhaul-v$version.zip"
$zipPlugin = 'BepInEx/plugins/ER2RealismOverhaul/'
$dll = 'ER2RealismOverhaul.dll'
$changelog = 'ER2RealismOverhaul-CHANGELOG.md'
$game = (Get-Item -LiteralPath $GameDir).FullName
$plugin = Join-Path $game 'BepInEx\plugins\ER2RealismOverhaul'
$token = [guid]::NewGuid().ToString('N')
$sources = Join-Path ([IO.Path]::GetTempPath()) "er2-release-$token"
$backup = Join-Path $projectRoot "artifacts\installation-backups\before-v$version-$(Get-Date -Format yyyyMMdd-HHmmss)-$($token.Substring(0,8))"

function Assert-GameClosed {
    if (Get-Process -Name 'Easy Red 2' -ErrorAction SilentlyContinue) {
        throw "Installation pending. Close Easy Red 2 and run scripts\Install-Release.ps1. The v$version packages are ready."
    }
}

function Set-VerifiedFile([string]$temporary, [string]$target) {
    if ([IO.File]::Exists($target)) {
        [IO.File]::Replace($temporary, $target, [System.Management.Automation.Language.NullString]::Value)
    } else {
        [IO.File]::Move($temporary, $target)
    }
}

Assert-GameClosed
Add-Type -AssemblyName System.IO.Compression.FileSystem
$null = [IO.Directory]::CreateDirectory($sources)
$changes = @()
$applied = @()
try {
    $hashes = @{}
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        foreach ($name in @($changelog, $dll)) {
            $entry = $zip.GetEntry($zipPlugin + $name)
            if (-not $entry) { throw "Missing $name in $zipPath" }
            $stream = $entry.Open()
            try { $hashes[$name] = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash } finally { $stream.Dispose() }
            $source = Join-Path $sources $name
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $source)
            if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $hashes[$name]) { throw "Package extraction failed for $name" }
        }
    } finally { $zip.Dispose() }

    $sourceDll = Join-Path $sources $dll
    if ((Get-Item -LiteralPath $sourceDll).VersionInfo.FileVersion -ne "$version.0" -or
        [Reflection.AssemblyName]::GetAssemblyName($sourceDll).Version.ToString() -ne "$version.0" -or
        ($ExpectedDllHash -and $hashes[$dll] -ne $ExpectedDllHash)) { throw 'Packaged DLL failed version or SHA256 verification.' }

    $files = @(@{ Name = $changelog; Target = (Join-Path $plugin $changelog); BackupName = $changelog })
    $legacy = Join-Path $game $dll
    if (Test-Path -LiteralPath $legacy -PathType Leaf) {
        $files += @{ Name = $dll; Target = $legacy; BackupName = "game-root-$dll" }
    }
    $files += @{ Name = $dll; Target = (Join-Path $plugin $dll); BackupName = $dll }

    # Save and verify the previous files before writing anything into the game.
    foreach ($file in $files) {
        $file.Before = $null
        if (Test-Path -LiteralPath $file.Target -PathType Leaf) {
            $file.Before = (Get-FileHash -LiteralPath $file.Target -Algorithm SHA256).Hash
            if ($file.Before -eq $hashes[$file.Name]) { continue }
            $null = [IO.Directory]::CreateDirectory($backup)
            $saved = Join-Path $backup $file.BackupName
            Copy-Item -LiteralPath $file.Target -Destination $saved
            if ((Get-FileHash -LiteralPath $saved -Algorithm SHA256).Hash -ne $file.Before) { throw "Backup verification failed for $($file.Target)" }
        }
        $file.Temporary = "$($file.Target).$token.tmp"
        $changes += $file
    }
    if (Test-Path -LiteralPath $backup) { Write-Host "Verified previous installation backup: $backup" }

    if ($changes.Count) {
        Assert-GameClosed
        $null = [IO.Directory]::CreateDirectory($plugin)
        foreach ($file in $changes) {
            # An encrypted Windows temp directory can be on a different volume from Steam.
            # Transfer package contents without copying the temp file's encryption attributes.
            [IO.File]::WriteAllBytes($file.Temporary, [IO.File]::ReadAllBytes((Join-Path $sources $file.Name)))
            if ((Get-FileHash -LiteralPath $file.Temporary -Algorithm SHA256).Hash -ne $hashes[$file.Name]) { throw "Copy verification failed for $($file.Name)" }
        }
        foreach ($file in $changes) {
            Assert-GameClosed
            $current = if ([IO.File]::Exists($file.Target)) { (Get-FileHash -LiteralPath $file.Target -Algorithm SHA256).Hash }
            if ($current -ne $file.Before) { throw "File changed during installation: $($file.Target)" }
            Set-VerifiedFile $file.Temporary $file.Target
            $applied += $file
        }
    }

    foreach ($file in $files) {
        if ((Get-FileHash -LiteralPath $file.Target -Algorithm SHA256).Hash -ne $hashes[$file.Name]) { throw "Installed checksum mismatch: $($file.Target)" }
    }
    $installed = Get-Item -LiteralPath (Join-Path $plugin $dll)
    if ($installed.VersionInfo.FileVersion -ne "$version.0") { throw 'Installed DLL version mismatch.' }
    Write-Host "Installed v$version at $($installed.FullName). SHA256 $($hashes[$dll])."
} catch {
    $failure = $_
    foreach ($file in $applied) {
        Assert-GameClosed
        if ((Get-FileHash -LiteralPath $file.Target -Algorithm SHA256).Hash -ne $hashes[$file.Name]) { continue }
        if ($file.Before) {
            Copy-Item -LiteralPath (Join-Path $backup $file.BackupName) -Destination $file.Temporary
            Set-VerifiedFile $file.Temporary $file.Target
        } else { [IO.File]::Delete($file.Target) }
    }
    throw $failure
} finally {
    foreach ($file in $changes) { if ([IO.File]::Exists($file.Temporary)) { [IO.File]::Delete($file.Temporary) } }
    foreach ($name in @($changelog, $dll)) { [IO.File]::Delete((Join-Path $sources $name)) }
    [IO.Directory]::Delete($sources)
}
