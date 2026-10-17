[CmdletBinding()]
param(
    [string]$TestsDirectory = (Join-Path $PSScriptRoot 'Tests'),
    [string[]]$CheckNames = @()
)

$ErrorActionPreference = 'Stop'
$TestsDirectory = (Resolve-Path -LiteralPath $TestsDirectory).Path
$shellName = if ($PSVersionTable.PSEdition -eq 'Core') {
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { 'pwsh.exe' } else { 'pwsh' }
} else { 'powershell.exe' }
$shell = Join-Path $PSHOME $shellName
$guards = @(Get-ChildItem -LiteralPath $TestsDirectory -Recurse -File -Filter '*.ps1' |
    Where-Object { $_.Name -ne 'VerifySourceDeployment.ps1' } |
    Sort-Object FullName)
$missingNames = @()
if ($CheckNames.Count -gt 0) {
    # Historical subset runners select names without changing their lists.
    # Only discovered files can run; missing selections remain visible failures.
    $selectedNames = @($CheckNames | ForEach-Object { $_ -replace '\.ps1$', '' } |
        Sort-Object -Unique)
    $guards = @($guards | Where-Object { $_.BaseName -in $selectedNames })
    $missingNames = @($selectedNames | Where-Object { $_ -notin $guards.BaseName })
    foreach ($missing in $missingNames) { Write-Output "MISSING $missing" }
} elseif ($guards.Count -eq 0) {
    throw "No checks found in $TestsDirectory"
}
$failures = @()
foreach ($guard in $guards) {
    # Isolate script globals, exit statements and Add-Type definitions.
    $name = $guard.FullName.Substring($TestsDirectory.TrimEnd('/', '\').Length + 1)
    # Some guards use repository-relative paths and do not set an error policy.
    # Encode the wrapper to preserve literal paths, including spaces/quotes.
    $quotedRoot = $PSScriptRoot.Replace("'", "''")
    $quotedGuard = $guard.FullName.Replace("'", "''")
    $command = @"
`$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath '$quotedRoot'
`$global:LASTEXITCODE = 0
& '$quotedGuard'
if (-not `$?) { exit 1 }
exit 0
"@
    $encodedCommand = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($command))
    $output = & $shell -NoLogo -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand 2>&1
    $code = $LASTEXITCODE
    if ($code -eq 0) {
        Write-Output "PASS $name"
    } else {
        $failures += $name
        Write-Output "FAIL $name (exit $code)"
        $output | ForEach-Object { Write-Output "  $_" }
    }
}
Write-Output "TOTAL=$($guards.Count + $missingNames.Count) PASS=$($guards.Count - $failures.Count) FAIL=$($failures.Count) MISSING=$($missingNames.Count)"
if ($failures.Count -gt 0 -or $missingNames.Count -gt 0) { exit 1 }
exit 0
