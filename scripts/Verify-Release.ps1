$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Invoke-VerificationStep([string]$Name, [string[]]$Arguments) {
    try {
        & dotnet @Arguments | Out-Host
        $passed = $LASTEXITCODE -eq 0
    }
    catch {
        Write-Host $_
        $passed = $false
    }
    if ($passed) { Write-Host "PASS $Name" }
    else { Write-Host "FAIL $Name" }
    return $passed
}

Push-Location -LiteralPath $repositoryRoot
try {
    $buildPassed = Invoke-VerificationStep 'Release build' @(
        'build', 'UnityPackageImporter/UnityPackageImporter.csproj', '-c', 'Release', '--no-incremental')
    $testsPassed = Invoke-VerificationStep 'Regression tests' @(
        'run', '--project', 'Tests/Importer.RegressionTests.csproj', '-c', 'Release')
    $auditPassed = $false
    if ($buildPassed) {
        $auditPassed = Invoke-VerificationStep 'UI bundle audit' @(
            'run', '--project', 'Tests/Importer.RegressionTests.csproj', '-c', 'Release', '--no-build', '--',
            '--ui-bundle', 'UnityPackageImporter/bin/Release/UnityPackageImporter.dll', 'UnityPackageImporter/UI/Templates')
    }
    else {
        # A stale DLL must never stand in for this invocation's failed build.
        Write-Host 'FAIL UI bundle audit (Release build failed; audit not run)'
    }
}
finally { Pop-Location }

if ($buildPassed -and $testsPassed -and $auditPassed) { exit 0 }
exit 1
