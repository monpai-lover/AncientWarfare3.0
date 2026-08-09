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
$identity = Read-Source 'Code/core/lineage/AccessionIdentityService.cs'
$code = (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Code') `
    -Recurse -Filter '*.cs' | ForEach-Object {
        Get-Content -Raw -Encoding UTF8 $_.FullName
    }) -join "`n"

foreach ($forbidden in @('SuccessionPreparationService',
        'SuccessionPreparationSnapshot',
        'TryPublishForNativeSuccession', 'TryGetPublishedCandidate',
        'TryOverridePublishedCandidate',
        'KingSuccessionPreparationState')) {
    if ($code.Contains($forbidden)) {
        throw "legacy succession symbol remains in production: $forbidden"
    }
}

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
        'AccessionIdentityService.CompleteInstalledKing(')) {
    throw 'immediate accession must use the shared completion path'
}
if (-not $heir.Contains('ReigningRoyalLineageIndex.OnKingInstalled(')) {
    throw 'successful vanilla installation must update the reigning index immediately'
}
$commitFailure = [regex]::Match($heir,
    'if \(!AccessionIdentityService\.Commit\(__instance, king\)\)(.*?)AccessionIdentityService\.ClearDeferredInstalledKing',
    [Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $commitFailure.Success -or
    -not $commitFailure.Value.Contains(
        'ReigningRoyalLineageIndex.OnKingInstalled(') -or
    -not $commitFailure.Value.Contains(
        'AccessionIdentityService.DeferInstalledKing(')) {
    throw 'post-setKing identity commit failure must retain the installed accession'
}
$deferredCompletion = [regex]::Match($identity,
    'private static bool CompleteDeferredInstallation\((.*?)public static bool FinalizeDeferredFounding',
    [Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $deferredCompletion.Success -or
    -not $deferredCompletion.Value.Contains('CompleteInstalledKing(')) {
    throw 'deferred identity repair must replay the shared accession completion'
}
$sharedCompletion = [regex]::Match($identity,
    'internal static bool CompleteInstalledKing\((.*?)public static bool FinalizeDeferredFounding',
    [Text.RegularExpressions.RegexOptions]::Singleline)
foreach ($required in @('LineageService.OnKingFoundBranch(',
        'SuccessionDisputePersistenceService.EnqueueInstalledSuccession(',
        'InheritanceLawService.EstablishHereditaryBranchAfterAccession(',
        'ReigningRoyalLineageIndex.OnKingInstalled(',
        'AW3MultiplayerSuccessionFacade.NotifyKingInstalled(')) {
    if (-not $sharedCompletion.Success -or
        -not $sharedCompletion.Value.Contains($required)) {
        throw "shared accession completion is missing: $required"
    }
}
if (-not $identity.Contains(
        'private sealed class AccessionCompletionProgress') -or
    -not $identity.Contains(
        'internal static bool CompleteInstalledKing(') -or
    -not $identity.Contains('CompletionProgressByKingdom')) {
    throw 'installed accession completion must be resumable and idempotent'
}
if (-not $identity.Contains('DeferredInstallationOrder') -or
    -not $identity.Contains('_deferredInstallationCursor') -or
    $identity.Contains('foreach (long id in DeferredInstallations.Keys)')) {
    throw 'deferred accession scheduling must be bounded and starvation-free'
}
if (-not $heir.Contains('pIdentityCommitted: true')) {
    throw 'failed accession completion must not repeat committed identity work'
}
$exhaustion = [regex]::Match($identity,
    'if \(pending\.Attempts >= DeferredInstallationMaxAttempts\)(.*?)\n\s*\}',
    [Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $exhaustion.Success -or
    $exhaustion.Value.Contains('DeferredInstallations.Remove(') -or
    -not $exhaustion.Value.Contains(
        'DeferredInstallationExhaustedRetryDelay')) {
    throw 'exhausted identity repair must remain queued at a bounded low frequency'
}
$enqueueIndex = $sharedCompletion.Value.IndexOf(
    'SuccessionDisputePersistenceService.EnqueueInstalledSuccession(',
    [StringComparison]::Ordinal)
$branchIndex = $sharedCompletion.Value.IndexOf(
    'InheritanceLawService.EstablishHereditaryBranchAfterAccession(',
    [StringComparison]::Ordinal)
if ($enqueueIndex -lt 0 -or $branchIndex -lt 0 -or
    $enqueueIndex -gt $branchIndex) {
    throw 'accession law must be captured before hereditary branch reset'
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
if (-not $capture.Value.Contains(
        'InheritanceLawService.GetEffectiveLaw(pKingdom)') -or
    -not $capture.Value.Contains('AccessionLaw = pAccessionLaw')) {
    throw 'installed succession must capture its accession law as a scalar'
}
if (-not $persistence.Contains('internal Actor Predecessor;') -or
    -not $persistence.Contains('Predecessor = pPredecessor')) {
    throw 'deferred dispute must retain the installed predecessor reference'
}
$process = [regex]::Match($persistence,
    'internal static void ProcessAuthorityCycle\(\)(.*?)internal static void Reset\(\)',
    [Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $process.Success -or
    $process.Value.Contains('PendingBuilds.Remove(ids[0])') -or
    -not $process.Value.Contains('BuildQueue.MarkDirty(context.KingdomId)')) {
    throw 'deferred dispute context can be removed before transient resolution succeeds'
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
