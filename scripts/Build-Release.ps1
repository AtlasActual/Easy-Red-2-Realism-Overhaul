[CmdletBinding()]
param(
    [string]$GameDir = $env:ER2_GAME_DIR,
    [string]$MirrorRoot = 'C:\Users\antoi\Documents\Easy Red 2 AI mod',
    [switch]$PackageOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'ER2RealismOverhaul.csproj'
$versionFile = Join-Path $projectRoot 'Version.props'

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $GameDir = 'E:\SteamLibrary\steamapps\common\Easy Red 2'
}

[xml]$versionXml = Get-Content -LiteralPath $versionFile -Raw
$modVersion = [string]$versionXml.Project.PropertyGroup.ModVersion
if ($modVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version.props contains invalid ModVersion '$modVersion'."
}

$expectedFileVersion = "$modVersion.0"
$buildDll = Join-Path $projectRoot 'bin\Release\net6.0\ER2RealismOverhaul.dll'
$testProject = Join-Path $projectRoot 'tests\CommanderPlannerTests\CommanderPlannerTests.csproj'
$releaseDir = Join-Path $projectRoot 'releases'
$compactStage = Join-Path $projectRoot "artifacts\ER2RealismOverhaul-v$modVersion"
$noBepStage = Join-Path $projectRoot "artifacts\ER2RealismOverhaul-v$modVersion-Windows-x64-No-BepInEx"
$fullStage = Join-Path $projectRoot "artifacts\ER2RealismOverhaul-v$modVersion-Windows-x64-BepInEx"

$requiredPaths = @($GameDir, $compactStage, $noBepStage, $fullStage)
foreach ($requiredPath in $requiredPaths) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required path does not exist: $requiredPath"
    }
}

Write-Host "Building ER2RealismOverhaul v$modVersion..."
& dotnet build $projectFile -c Release "/p:ER2GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) {
    throw "Plugin build failed with exit code $LASTEXITCODE."
}

Write-Host 'Running deterministic scenario tests...'
& dotnet run --project $testProject -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Scenario tests failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $buildDll)) {
    throw "Build output is missing: $buildDll"
}

$builtVersion = (Get-Item -LiteralPath $buildDll).VersionInfo.FileVersion
if ($builtVersion -ne $expectedFileVersion) {
    throw "Built DLL version is $builtVersion; expected $expectedFileVersion."
}

& (Join-Path $projectRoot 'tests\Test-PhysicsQueryBuffers.ps1') -GameDir $GameDir -PluginDll $buildDll

$pluginRelativePath = 'BepInEx\plugins\ER2RealismOverhaul\ER2RealismOverhaul.dll'
$deployedDlls = @(
    (Join-Path $compactStage $pluginRelativePath),
    (Join-Path $noBepStage $pluginRelativePath),
    (Join-Path $fullStage $pluginRelativePath)
)
foreach ($destination in $deployedDlls) {
    $destinationDirectory = Split-Path -Parent $destination
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item -LiteralPath $buildDll -Destination $destination -Force
}

function Sync-PackageDocuments {
    param(
        [Parameter(Mandatory)]
        [string]$Stage,

        [switch]$IncludeExtendedFiles
    )

    $pluginDirectory = Join-Path $Stage 'BepInEx\plugins\ER2RealismOverhaul'
    New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null

    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') `
        -Destination (Join-Path $Stage 'ER2RealismOverhaul-README.md') -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') `
        -Destination (Join-Path $Stage 'ER2RealismOverhaul-CHANGELOG.md') -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') `
        -Destination (Join-Path $pluginDirectory 'ER2RealismOverhaul-CHANGELOG.md') -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') `
        -Destination (Join-Path $Stage 'ER2RealismOverhaul-LICENSE.txt') -Force

    if ($IncludeExtendedFiles) {
        $docsDirectory = Join-Path $Stage 'docs'
        New-Item -ItemType Directory -Path $docsDirectory -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') `
            -Destination (Join-Path $Stage 'CHANGELOG.md') -Force
        Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') `
            -Destination (Join-Path $Stage 'changelog.txt') -Force
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\AI_VISUAL_DEBUG.md') `
            -Destination (Join-Path $docsDirectory 'AI_VISUAL_DEBUG.md') -Force
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\STUTTER_INVESTIGATION.md') `
            -Destination (Join-Path $docsDirectory 'STUTTER_INVESTIGATION.md') -Force
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\MP_FACTION_SELECTION_INVESTIGATION.md') `
            -Destination (Join-Path $docsDirectory 'MP_FACTION_SELECTION_INVESTIGATION.md') -Force
    }
}

Sync-PackageDocuments -Stage $compactStage
Sync-PackageDocuments -Stage $noBepStage -IncludeExtendedFiles
Sync-PackageDocuments -Stage $fullStage -IncludeExtendedFiles

New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
$packages = @(
    [pscustomobject]@{
        Stage = $compactStage
        Zip = Join-Path $releaseDir "ER2RealismOverhaul-v$modVersion.zip"
    },
    [pscustomobject]@{
        Stage = $noBepStage
        Zip = Join-Path $releaseDir "ER2RealismOverhaul-v$modVersion-Windows-x64-No-BepInEx.zip"
    },
    [pscustomobject]@{
        Stage = $fullStage
        Zip = Join-Path $releaseDir "ER2RealismOverhaul-v$modVersion-Windows-x64-BepInEx.zip"
    }
)

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($package in $packages) {
    # ZIP member names use '/' on every OS. Windows Compress-Archive wrote '\'
    # into the names, which several Linux extractors preserved as literal text.
    $stagePrefix = (Get-Item -LiteralPath $package.Stage).FullName + [IO.Path]::DirectorySeparatorChar
    $temporaryZip = $package.Zip + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $archive = [IO.Compression.ZipFile]::Open($temporaryZip, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($item in Get-ChildItem -LiteralPath $package.Stage -Recurse -Force | Sort-Object FullName) {
                $memberName = $item.FullName.Substring($stagePrefix.Length).Replace('\', '/')
                if ($item.PSIsContainer) {
                    $null = $archive.CreateEntry("$memberName/")
                } else {
                    $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $item.FullName, $memberName, [IO.Compression.CompressionLevel]::Optimal)
                }
            }
        } finally { $archive.Dispose() }
        if ([IO.File]::Exists($package.Zip)) {
            [IO.File]::Replace($temporaryZip, $package.Zip, [System.Management.Automation.Language.NullString]::Value)
        } else {
            [IO.File]::Move($temporaryZip, $package.Zip)
        }
    } finally { [IO.File]::Delete($temporaryZip) }
}

$expectedHash = (Get-FileHash -LiteralPath $buildDll -Algorithm SHA256).Hash
foreach ($dll in $deployedDlls) {
    $item = Get-Item -LiteralPath $dll
    $hash = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    if ($item.VersionInfo.FileVersion -ne $expectedFileVersion -or $hash -ne $expectedHash) {
        throw "Consolidation verification failed for $dll"
    }
}

foreach ($package in $packages) {
    $archive = [IO.Compression.ZipFile]::OpenRead($package.Zip)
    try {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($member in $archive.Entries) {
            $path = $member.FullName
            if ($path.Contains('\') -or $path -match '(^/|//|:|(^|/)\.{1,2}(/|$))' -or -not $names.Add($path)) {
                throw "Invalid ZIP path '$path' in $($package.Zip)"
            }
        }
        $pluginEntry = 'BepInEx/plugins/ER2RealismOverhaul/ER2RealismOverhaul.dll'
        $required = @($pluginEntry, 'BepInEx/plugins/ER2RealismOverhaul/ER2RealismOverhaul-CHANGELOG.md', 'INSTALL.txt')
        if ($package.Stage -eq $fullStage) {
            $required += @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'BepInEx/core/BepInEx.Unity.IL2CPP.dll', 'dotnet/coreclr.dll', 'dotnet/.version')
        }
        foreach ($path in $required) {
            if (-not $names.Contains($path)) { throw "Missing case-sensitive ZIP path '$path' in $($package.Zip)" }
        }

        $entry = $archive.GetEntry($pluginEntry)
        $stream = $entry.Open()
        try {
            $sha256 = [Security.Cryptography.SHA256]::Create()
            try {
                $entryHash = ([BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '')
            }
            finally {
                $sha256.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }

        if ($entryHash -ne $expectedHash) {
            throw "Package contains the wrong plugin DLL: $($package.Zip)"
        }
    }
    finally {
        $archive.Dispose()
    }
}

# The unversioned download is an exact copy of the verified compact package.
$aliasZip = Join-Path $releaseDir 'ER2RealismOverhaul.zip'
Copy-Item -LiteralPath $packages[0].Zip -Destination $aliasZip -Force
$packagePaths = @($packages | ForEach-Object { $_.Zip }) + @($aliasZip)
$hashes = foreach ($path in $packagePaths) {
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($path))"
}
if ($hashes[0].Substring(0, 64) -ne $hashes[-1].Substring(0, 64)) { throw 'Compact package alias checksum mismatch.' }
[IO.File]::WriteAllLines((Join-Path $releaseDir "ER2RealismOverhaul-v$modVersion-SHA256SUMS.txt"), [string[]]$hashes, [Text.Encoding]::ASCII)

Write-Host "Packaged v$modVersion successfully."
Write-Host "DLL SHA256: $expectedHash"
Write-Host "Verified packages: $($packagePaths.Count)"

if (-not $PackageOnly) {
    & (Join-Path $PSScriptRoot 'Install-Release.ps1') -GameDir $GameDir -ExpectedDllHash $expectedHash
    if ($MirrorRoot -and (Test-Path -LiteralPath $MirrorRoot -PathType Container)) {
        $mirrorDll = Join-Path $MirrorRoot 'bin\Release\net6.0\ER2RealismOverhaul.dll'
        New-Item -ItemType Directory -Path (Split-Path -Parent $mirrorDll) -Force | Out-Null
        Copy-Item -LiteralPath $buildDll -Destination $mirrorDll -Force
        if ((Get-FileHash -LiteralPath $mirrorDll -Algorithm SHA256).Hash -ne $expectedHash) { throw "Mirror checksum mismatch: $mirrorDll" }
    }
}
