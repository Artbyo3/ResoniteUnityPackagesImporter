param([Parameter(Mandatory = $true)][string]$ManifestPath)
$ErrorActionPreference = 'Stop'

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$processes = @(Get-Process -ErrorAction Stop | Where-Object {
    $_.ProcessName -in @('Resonite', 'ResoniteHeadless', 'Renderite.Host', 'Renderite.Renderer')
})
if ($processes.Count -gt 0) {
    @{ status = 'waiting'; processes = @($processes | Select-Object Id, ProcessName) } | ConvertTo-Json -Depth 4
    exit 0
}

$pending = @()
foreach ($file in $manifest.files) {
    if ((Get-FileHash -LiteralPath $file.source -Algorithm SHA256).Hash -ne $file.newHash) {
        throw "Prepared build changed: $($file.source)"
    }
    $installedHash = (Get-FileHash -LiteralPath $file.target -Algorithm SHA256).Hash
    if ($installedHash -eq $file.newHash) { continue }
    if ($installedHash -ne $file.oldHash) { throw "Installed file changed since preparation: $($file.target)" }
    if ((Get-FileHash -LiteralPath $file.backup -Algorithm SHA256).Hash -ne $file.oldHash) {
        throw "Original backup did not verify: $($file.backup)"
    }
    $pending += $file
}

# Check exclusive access to every destination before replacing anything.
foreach ($file in $pending) {
    $handle = [System.IO.File]::Open($file.target, 'Open', 'ReadWrite', 'None')
    $handle.Dispose()
}
$changed = @()
try {
    foreach ($file in $pending) {
        if (@(Get-Process | Where-Object { $_.ProcessName -in @('Resonite', 'ResoniteHeadless', 'Renderite.Host', 'Renderite.Renderer') }).Count -gt 0) {
            throw 'Resonite restarted before installation finished. Original files will be restored where possible.'
        }
        $temporary = $file.target + '.pending-' + [guid]::NewGuid().ToString('N')
        try {
            Copy-Item -LiteralPath $file.source -Destination $temporary
            if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $file.newHash) {
                throw "Staged copy did not verify: $temporary"
            }
            # PowerShell can marshal $null as an empty string for this overload.
            # Use an explicit backup path instead, alongside the original backup.
            [System.IO.File]::Replace($temporary, $file.target, ($file.backup + '.replace'))
            $changed += $file
        }
        finally {
            if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
        }
    }
    foreach ($file in $manifest.files) {
        if ((Get-FileHash -LiteralPath $file.target -Algorithm SHA256).Hash -ne $file.newHash) {
            throw "Installed file did not verify: $($file.target)"
        }
    }
}
catch {
    $installError = $_
    foreach ($file in $changed) {
        try { Copy-Item -LiteralPath $file.backup -Destination $file.target -Force }
        catch { Write-Warning "Could not restore $($file.target). Backup: $($file.backup). Error: $_" }
    }
    throw $installError
}

$result = @{ status = 'installed'; installedAt = (Get-Date).ToString('o'); files = $manifest.files }
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $ManifestPath) 'installed.json')
$result | ConvertTo-Json -Depth 5
