param(
    [string]$Configuration = 'Release',
    [string]$OutDir = 'dist'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repositoryRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Building project in $Configuration mode..."
dotnet build (Join-Path $repositoryRoot 'UnityPackageImporter/UnityPackageImporter.csproj') -c $Configuration
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
    $checksumFile = Join-Path $distPath 'SHA256SUMS.txt'
    "$hash  $zipName" | Set-Content -LiteralPath $checksumFile -Encoding UTF8
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
