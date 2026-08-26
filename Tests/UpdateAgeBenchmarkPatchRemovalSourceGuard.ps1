$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$patchPath = Join-Path $repoRoot 'Code/patch/AW_UpdateAgeBenchmarkPatch.cs'
if (Test-Path -LiteralPath $patchPath) {
    throw 'The redundant Actor/City/Kingdom updateAge benchmark patch must be removed.'
}

$benchmarkPath = Join-Path $repoRoot 'Code/core/policy/UpdateAgeBenchmark.cs'
$annualPath = Join-Path $repoRoot 'Code/core/policy/KingdomAnnualWorkService.cs'
$benchmark = [System.IO.File]::ReadAllText($benchmarkPath)
$annual = [System.IO.File]::ReadAllText($annualPath)
if (-not $benchmark.Contains('internal static class UpdateAgeBenchmark')) {
    throw 'AW3 stage-level age benchmarking must remain available.'
}
if (-not $annual.Contains('UpdateAgeBenchmark.Flush()')) {
    throw 'Annual work must continue flushing AW3 stage-level measurements.'
}

Write-Host 'Update-age benchmark patch removal source guard passed.'
