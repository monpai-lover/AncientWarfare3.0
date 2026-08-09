$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
function Read-Source([string]$relativePath) {
    $path = Join-Path $projectRoot $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "required succession source is missing: $relativePath"
    }
    return Get-Content -Raw -Encoding UTF8 $path
}

$death = Read-Source 'Code/patch/AW_ActorDeathPatch.cs'
$mandate = Read-Source 'Code/patch/AW_MandateSuccessionPatch.cs'
$heir = Read-Source 'Code/patch/AW_HeirPatch.cs'
$persistence = Read-Source `
    'Code/core/lineage/SuccessionDisputePersistenceService.cs'
$dispute = Read-Source 'Code/core/lineage/SuccessionDisputeService.cs'
$authority = Read-Source 'Code/core/performance/AWAuthorityCycleService.cs'

if (Test-Path -LiteralPath (Join-Path $projectRoot `
        'Code/core/lineage/SuccessionPreparationService.cs')) {
    throw 'legacy succession preparation service file remains'
}

foreach ($forbidden in @('BuildSnapshot(',
        'TryPublishForNativeSuccession(', 'TryGetPublishedCandidate(',
        'TryOverridePublishedCandidate(', 'SuccessionPreparationSnapshot',
        'KingSuccessionPreparationState')) {
    if (($death + $mandate + $heir).Contains($forbidden)) {
        throw "legacy succession snapshot path remains: $forbidden"
    }
}

if (-not $mandate.Contains(
        'AuthoritativeSuccessionService.EnsureRegisteredCandidate(')) {
    throw 'dead-king handling must validate the registered heir once'
}
if (-not $heir.Contains('HeirService.PeekRegisteredHeir(pKingdom)')) {
    throw 'native royal-clan selection must consume the registered heir'
}
if ($death.Contains('SuccessionPreparationService.CaptureDeath')) {
    throw 'Actor.die must not capture a succession snapshot'
}
if (-not $heir.Contains(
        'SuccessionDisputePersistenceService.EnqueueInstalledSuccession(')) {
    throw 'successful vanilla installation must enqueue dispute persistence'
}
if (-not $heir.Contains('ReigningRoyalLineageIndex.OnKingInstalled(')) {
    throw 'successful vanilla installation must update the reigning index'
}
if ($authority.Contains('SuccessionPreparationService') -or
    -not $authority.Contains(
        'SuccessionDisputePersistenceService.ProcessAuthorityCycle()')) {
    throw 'authority cycle still owns candidate snapshot work'
}

$capture = [regex]::Match($persistence,
    'internal static void EnqueueInstalledSuccession\(Kingdom pKingdom,(.*?)internal static void ProcessAuthorityCycle\(\)',
    [Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $capture.Success) {
    throw 'installed-succession scalar capture method is missing'
}
foreach ($forbidden in @('BuildPreparationFacts', 'ResolveFactionSupport',
        'SQLite', 'HistoricalWriteService', 'getCities',
        'World.world.units')) {
    if ($capture.Value.Contains($forbidden)) {
        throw "setKing dispute capture contains deferred work: $forbidden"
    }
}

foreach ($forbidden in @('PrepareSuccessionBeforeKingDeath',
        'SuccessionDisputeService.Prepare', 'SQLiteCommand',
        'BeginTransaction', 'LineageQuery', 'World.world.units')) {
    if ($death.Contains($forbidden)) {
        throw "Actor.die contains forbidden king-death work: $forbidden"
    }
}
if ($dispute.Contains('public static void Prepare(')) {
    throw 'legacy synchronous succession dispute preparation remains callable'
}

Write-Host 'King death succession performance source guard passed.'
