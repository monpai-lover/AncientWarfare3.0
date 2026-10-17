[CmdletBinding()]
param([string]$LogDirectory = (Join-Path $PSScriptRoot 'TestResults/full-checks'))

$ErrorActionPreference = 'Stop'
$shellName = if ($PSVersionTable.PSEdition -eq 'Core') {
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { 'pwsh.exe' } else { 'pwsh' }
} else { 'powershell.exe' }
$shell = Join-Path $PSHOME $shellName
Get-Command dotnet -ErrorAction Stop | Out-Null
$null = New-Item -ItemType Directory -Path $LogDirectory -Force
$LogDirectory = (Resolve-Path -LiteralPath $LogDirectory).Path
$results = [System.Collections.Generic.List[object]]::new()

function Run-Check([string]$Name, [string]$Executable, [string[]]$Arguments) {
    $log = Join-Path $LogDirectory ($Name + '.log')
    $started = [DateTime]::UtcNow
    $exitCode = 1
    try {
        # Continue collecting all checks when a child reports a failure.
        $ErrorActionPreference = 'Continue'
        & $Executable @Arguments > $log 2>&1
        $exitCode = $LASTEXITCODE
    } catch {
        $_ | Out-String | Add-Content -LiteralPath $log
    } finally {
        $ErrorActionPreference = 'Stop'
    }
    $results.Add([pscustomobject]@{
        name = $Name; exit_code = $exitCode; log = $log
        started_utc = $started.ToString('o'); finished_utc = [DateTime]::UtcNow.ToString('o')
        executable = $Executable; arguments = $Arguments
    })
    $status = if ($exitCode -eq 0) { 'PASS' } else { 'FAIL' }
    Write-Output "$status $Name (exit $exitCode)"
    if ($exitCode -ne 0) { Get-Content -LiteralPath $log -Tail 16 }
}

Push-Location $PSScriptRoot
try {
    Run-Check 'source-guards' $shell @('-NoLogo', '-NoProfile', '-File',
        (Join-Path $PSScriptRoot 'run_all_guards.ps1'))
    $projects = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Tests') `
        -Recurse -File -Filter '*.csproj' |
        Where-Object { $_.FullName -notmatch '[/\\](bin|obj)[/\\]' } |
        Sort-Object FullName)
    if ($projects.Count -eq 0) { throw 'No executable test projects found.' }
    foreach ($project in $projects) {
        Run-Check $project.BaseName 'dotnet' @('run', '--project',
            $project.FullName, '--configuration', 'Release')
        if ($results[$results.Count - 1].exit_code -ne 0) {
            # Never execute a stale DLL after a failed build/run prerequisite.
            Write-Output "SKIP additional cases for $($project.BaseName): prerequisite failed"
            continue
        }
        if ($project.BaseName -eq 'AncientWarfare3.Rules.Tests') {
            Run-Check 'rules-all-suites' 'dotnet' @('run', '--project',
                $project.FullName, '--configuration', 'Release', '--no-build',
                '--', '--all-suites')
        }
        if ($project.BaseName -eq 'ArmyRtsAdversarialSimulation') {
            Run-Check 'rts-32-seeds' 'dotnet' @('run', '--project',
                $project.FullName, '--configuration', 'Release', '--no-build',
                '--', '--all', '--seeds', '32', '--ticks', '10000')
            foreach ($scenario in @('synthetic-mobilization',
                    'large-step-equivalence', 'battle-10x20', 'oracle')) {
                Run-Check "rts-$scenario" 'dotnet' @('run', '--project',
                    $project.FullName, '--configuration', 'Release', '--no-build',
                    '--', '--scenario', $scenario, '--seed', '17')
            }
        }
    }
} finally {
    Pop-Location
    ConvertTo-Json -InputObject @($results.ToArray()) -Depth 5 |
        Set-Content -LiteralPath (Join-Path $LogDirectory 'results.json') -Encoding UTF8
}
$failed = @($results | Where-Object { $_.exit_code -ne 0 }).Count
Write-Output "CHECKS=$($results.Count) PASS=$($results.Count - $failed) FAIL=$failed"
Write-Output "Logs: $LogDirectory"
if ($failed -gt 0) { exit 1 }
exit 0
