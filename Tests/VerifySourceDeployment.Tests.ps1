param()

$ErrorActionPreference = 'Stop'
$verifier = Join-Path $PSScriptRoot 'VerifySourceDeployment.ps1'

if (-not (Test-Path -LiteralPath $verifier -PathType Leaf)) {
    throw "Source deployment verifier is missing: $verifier"
}

$shellName = if ($PSVersionTable.PSEdition -eq 'Core') {
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { 'pwsh.exe' } else { 'pwsh' }
} else { 'powershell.exe' }
& (Join-Path $PSHOME $shellName) -NoProfile -Command 'exit 7'
if ($LASTEXITCODE -ne 7) {
    throw 'Failed to seed the stale LASTEXITCODE fixture.'
}

& $verifier -SelfTest
# Do not inherit the native child process exit code after a successful self-test.
exit 0
