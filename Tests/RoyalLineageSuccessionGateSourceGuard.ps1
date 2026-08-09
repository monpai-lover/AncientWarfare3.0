$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$indexPath = Join-Path $projectRoot `
    'Code/core/lineage/ReigningRoyalLineageIndex.cs'
if (-not (Test-Path -LiteralPath $indexPath)) {
    throw 'reigning royal-lineage runtime index is missing'
}

$index = Get-Content -Raw -Encoding UTF8 $indexPath
$authority = Get-Content -Raw -Encoding UTF8 (Join-Path $projectRoot `
    'Code/core/performance/AWAuthorityCycleService.cs')

foreach ($required in @('LineageKeys.LINEAGE_ID', 'State.Register(',
        'State.RemoveKingdom(', 'ProcessAuthorityCycle()', 'Reset()')) {
    if (-not $index.Contains($required)) {
        throw "missing royal-lineage index behavior: $required"
    }
}

foreach ($forbidden in @('LineageKeys.SHI_ID', 'SQLite', 'LineageQuery',
        'World.world.units')) {
    if ($index.Contains($forbidden)) {
        throw "royal-lineage event index contains forbidden lookup: $forbidden"
    }
}

if (-not $authority.Contains('ReigningRoyalLineageIndex.Reset()')) {
    throw 'world reset must clear the reigning-lineage index'
}
if (-not $authority.Contains(
        'ReigningRoyalLineageIndex.ProcessAuthorityCycle()')) {
    throw 'world load must incrementally rebuild the reigning-lineage index'
}

Write-Host 'Royal lineage succession gate source guard passed.'
