$ErrorActionPreference = 'Stop'
$runner = Join-Path (Split-Path -Parent $PSScriptRoot) 'run_all_guards.ps1'
$shellName = if ($PSVersionTable.PSEdition -eq 'Core') {
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { 'pwsh.exe' } else { 'pwsh' }
} else { 'powershell.exe' }
$shell = Join-Path $PSHOME $shellName
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('aw3-guards-' + [guid]::NewGuid().ToString('N'))
try {
    $nested = New-Item -ItemType Directory -Path (Join-Path $fixture 'nested') -Force
    Set-Content -LiteralPath (Join-Path $fixture 'Pass.ps1') -Value 'Write-Output "fixture pass"; exit 0'
    Set-Content -LiteralPath (Join-Path $nested 'Failure.ps1') -Value 'Write-Output "fixture failure"; exit 7'
    $result = & $shell -NoProfile -File $runner -TestsDirectory $fixture 2>&1
    if ($LASTEXITCODE -ne 1 -or ($result -join "`n") -notmatch 'TOTAL=2 PASS=1 FAIL=1') {
        throw 'Runner must discover nested checks and return failure for their nonzero exit.'
    }
    $result = & $shell -NoProfile -File $runner -TestsDirectory $fixture -CheckNames Pass 2>&1
    if ($LASTEXITCODE -ne 0 -or ($result -join "`n") -notmatch 'TOTAL=1 PASS=1 FAIL=0 MISSING=0' -or
        ($result -join "`n") -match 'FAIL nested') {
        throw 'A selected run must not execute unselected checks.'
    }
    $result = & $shell -NoProfile -File $runner -TestsDirectory $fixture -CheckNames Failure 2>&1
    if ($LASTEXITCODE -ne 1 -or ($result -join "`n") -notmatch 'TOTAL=1 PASS=0 FAIL=1 MISSING=0') {
        throw 'A selected failing check must propagate failure.'
    }
    $result = & $shell -NoProfile -File $runner -TestsDirectory $fixture -CheckNames MissingCheck 2>&1
    if ($LASTEXITCODE -ne 1 -or ($result -join "`n") -notmatch 'MISSING MissingCheck' -or
        ($result -join "`n") -notmatch 'TOTAL=1 PASS=0 FAIL=0 MISSING=1') {
        throw 'A missing selected check must be counted and fail the run.'
    }
    Set-Content -LiteralPath (Join-Path $nested 'Failure.ps1') -Value 'exit 0'
    $result = & $shell -NoProfile -File $runner -TestsDirectory $fixture 2>&1
    if ($LASTEXITCODE -ne 0 -or ($result -join "`n") -notmatch 'TOTAL=2 PASS=2 FAIL=0') {
        throw 'Runner must return success only after every check succeeds.'
    }
    Set-Content -LiteralPath (Join-Path $nested 'Failure.ps1') -Value 'throw "fixture exception"'
    $result = & $shell -NoProfile -File $runner -TestsDirectory $fixture 2>&1
    if ($LASTEXITCODE -ne 1) { throw 'Runner must propagate terminating check exceptions.' }
    Set-Content -LiteralPath (Join-Path $nested 'Failure.ps1') -Value 'Write-Error "fixture non-terminating error"; Write-Output "continued"'
    $result = & $shell -NoProfile -File $runner -TestsDirectory $fixture 2>&1
    if ($LASTEXITCODE -ne 1 -or ($result -join "`n") -notmatch 'TOTAL=2 PASS=1 FAIL=1') {
        throw 'Runner must fail checks that emit an unhandled PowerShell error.'
    }
    $quotedShell = $shell.Replace("'", "''")
    Set-Content -LiteralPath (Join-Path $nested 'Failure.ps1') -Value @"
& '$quotedShell' -NoProfile -Command 'exit 7'
if (`$LASTEXITCODE -ne 7) { throw 'expected fixture exit 7' }
Write-Output 'handled expected child failure'
"@
    $result = & $shell -NoProfile -File $runner -TestsDirectory $fixture 2>&1
    if ($LASTEXITCODE -ne 0 -or ($result -join "`n") -notmatch 'TOTAL=2 PASS=2 FAIL=0') {
        throw 'Runner must not confuse a handled fixture exit with the check result.'
    }
    Set-Content -LiteralPath (Join-Path $nested 'Failure.ps1') -Value 'if (-not (Test-Path "Code/core")) { throw "wrong repository cwd" }'
    Push-Location -LiteralPath $fixture
    try {
        $result = & $shell -NoProfile -File $runner -TestsDirectory $fixture 2>&1
        if ($LASTEXITCODE -ne 0 -or ($result -join "`n") -notmatch 'TOTAL=2 PASS=2 FAIL=0') {
            throw 'Runner must execute repository-relative guards from the repository root.'
        }
    } finally {
        Pop-Location
    }
    $legacyRoot = Join-Path $fixture 'legacy'
    $legacyTests = New-Item -ItemType Directory -Path (Join-Path $legacyRoot 'Tests') -Force
    Copy-Item -LiteralPath $runner -Destination (Join-Path $legacyRoot 'run_all_guards.ps1')
    foreach ($legacy in @(
        @('run_my_guards.ps1', 'ArmyCaptainCareerSourceGuardTests', 21),
        @('run_relevant_guards.ps1', 'ActorRuntimePerformanceSourceGuardTests', 55))) {
        $helper = Join-Path $legacyRoot $legacy[0]
        Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $runner) $legacy[0]) -Destination $helper
        $failingCheck = Join-Path $legacyTests ($legacy[1] + '.ps1')
        Set-Content -LiteralPath $failingCheck -Value 'exit 7'
        $result = & $shell -NoProfile -File $helper 2>&1
        $expected = "TOTAL=$($legacy[2]) PASS=0 FAIL=1 MISSING=$($legacy[2] - 1)"
        if ($LASTEXITCODE -ne 1 -or -not ($result -join "`n").Contains($expected)) {
            throw "$($legacy[0]) must preserve its selection and fail for failed or missing checks."
        }
        Remove-Item -LiteralPath $failingCheck
    }
    Write-Output 'Full guard runner regression tests passed.'
} finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
exit 0
