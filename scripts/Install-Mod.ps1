param([string]$GamePath = $env:GAMEPATH)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $GamePath) { $GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Resonite' }
if (-not (Test-Path -LiteralPath (Join-Path $GamePath 'Libraries/ResoniteModLoader.dll') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $GamePath 'rml_libs/0Harmony.dll') -PathType Leaf)) {
    throw 'Install ResoniteModLoader and Harmony in the selected GamePath before installing this mod.'
}
$files = [ordered]@{
    'rml_mods/UnityPackageImporter.dll' = (Join-Path $repositoryRoot 'UnityPackageImporter/bin/Release/UnityPackageImporter.dll')
    'rml_libs/YamlDotNet.dll' = (Join-Path $repositoryRoot 'UnityPackageImporter/bin/Release/YamlDotNet.dll')
    'UnityPackageImporter.LICENSE' = (Join-Path $repositoryRoot 'LICENSE')
    'UnityPackageImporter.THIRD_PARTY_NOTICES.md' = (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md')
}
foreach ($source in $files.Values) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing distribution file: $source. Build Release first." }
}
$processes = @(Get-Process | Where-Object {
    $_.ProcessName -in @('Resonite', 'ResoniteHeadless', 'Renderite.Host', 'Renderite.Renderer')
})
if ($processes.Count -gt 0) {
    Write-Warning 'Resonite or its renderer is running. Close it completely before installation.'
    exit 1
}
foreach ($relativePath in $files.Keys) {
    $source = $files[$relativePath]
    $target = Join-Path $GamePath $relativePath
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target)
    Copy-Item -LiteralPath $source -Destination $target -Force
    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($targetHash -ne $sourceHash) { throw "Installed file failed verification: $relativePath" }
    Write-Host "Installed $relativePath (SHA256: $targetHash)"
}
Write-Host 'Ready for live test. Fully restart Resonite and confirm imports and settings.'
