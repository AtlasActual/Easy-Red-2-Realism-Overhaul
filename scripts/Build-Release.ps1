[CmdletBinding()]
param(
    [string]$GameDir = $env:ER2_GAME_DIR,
    [string]$MirrorRoot = 'C:\Users\antoi\Documents\Easy Red 2 AI mod'
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

foreach ($requiredPath in @($GameDir, $MirrorRoot, $compactStage, $noBepStage, $fullStage)) {
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

$runningGame = Get-Process -ErrorAction SilentlyContinue | Where-Object {
    try {
        $_.Path -and $_.Path.StartsWith($GameDir, [StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        $false
    }
}
if ($runningGame) {
    throw 'Easy Red 2 is running. Close it before deploying the consolidated DLL.'
}

$pluginRelativePath = 'BepInEx\plugins\ER2RealismOverhaul\ER2RealismOverhaul.dll'
$deployedDlls = @(
    (Join-Path $MirrorRoot 'bin\Release\net6.0\ER2RealismOverhaul.dll'),
    (Join-Path $GameDir $pluginRelativePath),
    (Join-Path $compactStage $pluginRelativePath),
    (Join-Path $noBepStage $pluginRelativePath),
    (Join-Path $fullStage $pluginRelativePath)
)

# Keep the historical game-root copy synchronized if it exists, even though BepInEx
# loads the DLL from BepInEx\plugins\ER2RealismOverhaul.
$legacyGameRootDll = Join-Path $GameDir 'ER2RealismOverhaul.dll'
if (Test-Path -LiteralPath $legacyGameRootDll) {
    $deployedDlls += $legacyGameRootDll
}

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
    },
    [pscustomobject]@{
        Stage = $compactStage
        Zip = Join-Path $releaseDir 'ER2RealismOverhaul.zip'
    }
)

foreach ($package in $packages) {
    Compress-Archive -Path (Join-Path $package.Stage '*') -DestinationPath $package.Zip -Force
}

$expectedHash = (Get-FileHash -LiteralPath $buildDll -Algorithm SHA256).Hash
foreach ($dll in $deployedDlls) {
    $item = Get-Item -LiteralPath $dll
    $hash = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    if ($item.VersionInfo.FileVersion -ne $expectedFileVersion -or $hash -ne $expectedHash) {
        throw "Consolidation verification failed for $dll"
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($package in $packages) {
    $archive = [IO.Compression.ZipFile]::OpenRead($package.Zip)
    try {
        $entry = $archive.Entries | Where-Object {
            $_.FullName.Replace('\', '/') -eq 'BepInEx/plugins/ER2RealismOverhaul/ER2RealismOverhaul.dll'
        } | Select-Object -First 1
        if (-not $entry) {
            throw "Package is missing the plugin DLL: $($package.Zip)"
        }

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

Write-Host ''
Write-Host "Consolidated v$modVersion successfully."
Write-Host "DLL SHA256: $expectedHash"
Write-Host "Verified DLL copies: $($deployedDlls.Count)"
Write-Host "Verified packages: $($packages.Count)"
