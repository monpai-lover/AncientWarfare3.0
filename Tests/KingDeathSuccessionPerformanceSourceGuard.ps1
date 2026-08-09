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
$preparation = Read-Source `
    'Code/core/lineage/SuccessionPreparationService.cs'
$dispute = Read-Source 'Code/core/lineage/SuccessionDisputeService.cs'

foreach ($forbidden in @('BuildSnapshot(',
        'TryPublishForNativeSuccession(', 'TryGetPublishedCandidate(',
        'TryOverridePublishedCandidate(', 'SuccessionPreparationSnapshot',
        'KingSuccessionPreparationState')) {
    if (($death + $mandate + $heir + $preparation).Contains($forbidden)) {
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
