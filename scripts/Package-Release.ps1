param(
    [ValidateSet('Release')][string]$Configuration = 'Release',
    [string]$OutDir = 'dist',
    [string]$ReleaseTag = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repositoryRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Building project in $Configuration mode..."
dotnet build (Join-Path $repositoryRoot 'UnityPackageImporter/UnityPackageImporter.csproj') -c $Configuration --no-incremental
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$binDir = Join-Path $repositoryRoot "UnityPackageImporter/bin/$Configuration"
$distributionFiles = [ordered]@{
    'rml_mods/UnityPackageImporter.dll' = (Join-Path $binDir 'UnityPackageImporter.dll')
    'rml_libs/YamlDotNet.dll' = (Join-Path $binDir 'YamlDotNet.dll')
    'LICENSE' = (Join-Path $repositoryRoot 'LICENSE')
    'THIRD_PARTY_NOTICES.md' = (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md')
    'README.md' = (Join-Path $repositoryRoot 'README.md')
}
foreach ($source in $distributionFiles.Values) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing distribution file: $source" }
}

# Read version from assembly
$assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($distributionFiles['rml_mods/UnityPackageImporter.dll']).Version
$versionStr = "$($assemblyVersion.Major).$($assemblyVersion.Minor).$($assemblyVersion.Build)"
if (-not $ReleaseTag) { $ReleaseTag = "v$versionStr" }
if ($ReleaseTag -cnotin @($versionStr, "v$versionStr")) { throw 'Release tag must match the built assembly version.' }

dotnet run --project (Join-Path $repositoryRoot 'Tests/Importer.RegressionTests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed; no release package was prepared.' }
dotnet run --project (Join-Path $repositoryRoot 'Tests/Importer.RegressionTests.csproj') -c Release --no-build -- --ui-bundle $distributionFiles['rml_mods/UnityPackageImporter.dll'] (Join-Path $repositoryRoot 'UnityPackageImporter/UI/Templates')
if ($LASTEXITCODE -ne 0) { throw 'UI bundle audit failed; no release package was prepared.' }
Push-Location -LiteralPath $repositoryRoot
try {
    python Tests/test_resolute_distribution.py
    if ($LASTEXITCODE -ne 0) { throw 'Resolute distribution tests failed; no release package was prepared.' }
}
finally { Pop-Location }

if (-not [System.IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $repositoryRoot $OutDir }
$distPath = (New-Item -ItemType Directory -Force -Path $OutDir).FullName
$packageNonce = [guid]::NewGuid().ToString('N')
$stagingDir = Join-Path $distPath "package-staging-$versionStr-$packageNonce"
$temporaryZip = Join-Path $distPath "package-$packageNonce.pending.zip"
$zipName = "UnityPackageImporter-v$versionStr.zip"
$zipPath = Join-Path $distPath $zipName

function Assert-DistributionChild([string]$Candidate) {
    $absolute = [System.IO.Path]::GetFullPath($Candidate)
    $prefix = [System.IO.Path]::GetFullPath($distPath).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $absolute.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Package path escapes the requested distribution directory: $absolute"
    }
}

Assert-DistributionChild $stagingDir
Assert-DistributionChild $temporaryZip
Assert-DistributionChild $zipPath
try {
    foreach ($entryName in $distributionFiles.Keys) {
        $destination = Join-Path $stagingDir $entryName
        $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination)
        Copy-Item -LiteralPath $distributionFiles[$entryName] -Destination $destination
    }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stagingDir, $temporaryZip)

    # Audit the actual archive before replacing the previous candidate. This
    # allowlist excludes engine assemblies, debug symbols and private sources.
    $archive = [System.IO.Compression.ZipFile]::OpenRead($temporaryZip)
    try {
        if ($archive.Entries.Count -ne $distributionFiles.Count) { throw 'Unexpected distribution entry count.' }
        $seenEntries = @{}
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if (-not $distributionFiles.Contains($name) -or $seenEntries.ContainsKey($name)) {
                throw "Unexpected or duplicate distribution entry: $name"
            }
            $seenEntries[$name] = $true
            $stream = $entry.Open()
            try { $actualHash = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash }
            finally { $stream.Dispose() }
            if ($actualHash -ne (Get-FileHash -LiteralPath $distributionFiles[$name] -Algorithm SHA256).Hash) {
                throw "Packaged file does not match its source: $name"
            }
        }
    }
    finally { $archive.Dispose() }
    Move-Item -LiteralPath $temporaryZip -Destination $zipPath -Force
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    $releaseAssets = Join-Path $distPath "release-assets/$versionStr"
    Assert-DistributionChild $releaseAssets
    $null = New-Item -ItemType Directory -Force -Path $releaseAssets
    foreach ($source in $distributionFiles.Values) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $releaseAssets (Split-Path -Leaf $source)) -Force
    }
    Copy-Item -LiteralPath $zipPath -Destination (Join-Path $releaseAssets $zipName) -Force
    Copy-Item -LiteralPath $zipPath -Destination (Join-Path $releaseAssets 'UnityPackageImporter-testing-build.zip') -Force
    $assetNames = @($distributionFiles.Values | ForEach-Object { Split-Path -Leaf $_ }) + @($zipName, 'UnityPackageImporter-testing-build.zip')
    $checksums = foreach ($assetName in $assetNames) {
        $assetHash = (Get-FileHash -LiteralPath (Join-Path $releaseAssets $assetName) -Algorithm SHA256).Hash.ToLowerInvariant()
        "$assetHash  $assetName"
    }
    $checksums | Set-Content -LiteralPath (Join-Path $releaseAssets 'SHA256SUMS.txt') -Encoding UTF8
    "$hash  $zipName" | Set-Content -LiteralPath (Join-Path $distPath 'SHA256SUMS.txt') -Encoding UTF8
    python (Join-Path $repositoryRoot 'scripts/Prepare-ResoluteManifest.py') --artifacts $releaseAssets --output (Join-Path $distPath "resolute/$versionStr") --version $versionStr --release-tag $ReleaseTag
    if ($LASTEXITCODE -ne 0) { throw 'Resolute submission preparation failed.' }
}
finally {
    # Only remove this invocation's unique scratch paths, after checking their
    # resolved absolute locations remain inside the explicit output directory.
    Assert-DistributionChild $stagingDir
    if (Test-Path -LiteralPath $stagingDir) { Remove-Item -LiteralPath $stagingDir -Recurse -Force }
    Assert-DistributionChild $temporaryZip
    if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip -Force }
}

Write-Host "Release package generated successfully:"
Write-Host "  Package: $zipPath"
Write-Host "  SHA256:  $hash"
Write-Host "  Release assets: $releaseAssets"
Write-Host "  Resolute submission: $(Join-Path $distPath "resolute/$versionStr/manifest/Artbyo3")"
Write-Host 'Local preparation only. Publish the exact assets and verify the URLs before submitting the manifest.'
