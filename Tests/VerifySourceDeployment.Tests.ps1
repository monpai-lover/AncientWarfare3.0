param()

$ErrorActionPreference = 'Stop'
$verifier = Join-Path $PSScriptRoot 'VerifySourceDeployment.ps1'

if (-not (Test-Path -LiteralPath $verifier -PathType Leaf)) {
    throw "Source deployment verifier is missing: $verifier"
}

& cmd /c exit 7
if ($LASTEXITCODE -ne 7) {
    throw 'Failed to seed the stale LASTEXITCODE fixture.'
}

& $verifier -SelfTest
