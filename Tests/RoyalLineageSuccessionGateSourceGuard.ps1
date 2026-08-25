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
$birth = Get-Content -Raw -Encoding UTF8 (Join-Path $projectRoot `
    'Code/patch/AW_BirthPatch.cs')
$death = Get-Content -Raw -Encoding UTF8 (Join-Path $projectRoot `
    'Code/patch/AW_ActorDeathPatch.cs')
$heir = Get-Content -Raw -Encoding UTF8 (Join-Path $projectRoot `
    'Code/core/lineage/HeirService.cs')
$inheritance = Get-Content -Raw -Encoding UTF8 (Join-Path $projectRoot `
    'Code/core/lineage/InheritanceLawService.cs')
$courtDirection = Get-Content -Raw -Encoding UTF8 (Join-Path $projectRoot `
    'Code/core/court/CourtDirectionService.cs')
$chronicle = Get-Content -Raw -Encoding UTF8 (Join-Path $projectRoot `
    'Code/patch/AW_ChroniclePatch.cs')

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
if (-not ($authority.Contains(
        'ReigningRoyalLineageIndex.ProcessAuthorityCycle()') -or
    $authority.Contains(
        'ReigningRoyalLineageIndex.ProcessAuthorityCycle)'))) {
    throw 'world load must incrementally rebuild the reigning-lineage index'
}

if ($birth.Contains('SuccessionPreparationService')) {
    throw 'ordinary births must not invalidate a succession snapshot'
}
if ($death.Contains('SuccessionPreparationService.MarkDirty')) {
    throw 'ordinary deaths must not invalidate a kingdom succession snapshot'
}
if (-not $death.Contains('ReigningRoyalLineageIndex.OnKingDying(')) {
    throw 'king death must remove only that kingdom from the reigning index'
}

$eventMethod = [regex]::Match($heir,
    'public static void MarkSuccessionDirtyForActor\(Actor pActor\)(.*?)public static void MarkSelectionDirty',
    [Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $eventMethod.Success) {
    throw 'filtered actor succession event method is missing'
}
foreach ($required in @('LineageKeys.LINEAGE_ID',
        'ReigningRoyalLineageIndex.IsRoyalLineageOf(',
        'RoyalSuccessionEventRules.ShouldMarkSelectionDirty(')) {
    if (-not $eventMethod.Value.Contains($required)) {
        throw "actor succession event gate is missing: $required"
    }
}
foreach ($forbidden in @('SQLite', 'LineageQuery', 'World.world.units')) {
    if ($eventMethod.Value.Contains($forbidden)) {
        throw "actor succession event gate contains forbidden lookup: $forbidden"
    }
}

foreach ($source in @($inheritance, $courtDirection, $chronicle)) {
    if ($source.Contains('SuccessionPreparationService.MarkDirty')) {
        throw 'policy, court, and chronicle hooks must not invalidate a succession snapshot'
    }
}

Write-Host 'Royal lineage succession gate source guard passed.'
