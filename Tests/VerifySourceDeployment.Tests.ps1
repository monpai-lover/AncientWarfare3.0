param()

$ErrorActionPreference = 'Stop'
$verifier = Join-Path $PSScriptRoot 'VerifySourceDeployment.ps1'

if (-not (Test-Path -LiteralPath $verifier -PathType Leaf)) {
    throw "Source deployment verifier is missing: $verifier"
}

& $verifier -SelfTest
if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
    throw "Source deployment verifier self-test failed with exit code $LASTEXITCODE."
}
