$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$storePath = Join-Path $root `
    'Code\core\lineage\MilitaryGovernorateStore.cs'
$pipelinePath = Join-Path $root `
    'Code\core\multiplayer\AW3RuntimeRestorePipeline.cs'
$indexPath = Join-Path $root `
    'Code\core\db\LineageArchiveIndexRules.cs'
$modelsPath = Join-Path $root `
    'Code\api\multiplayer\AW3MultiplayerStrategicStateModels.cs'
$coordinatorPath = Join-Path $root `
    'Code\core\multiplayer\AW3MultiplayerStrategicStateCoordinator.cs'
$tablePath = Join-Path $root `
    'Code\core\db\MilitaryGovernorateStateTableItem.cs'
$archivePath = Join-Path $root `
    'Code\core\db\LineageArchiveManager.cs'
$vassalPath = Join-Path $root `
    'Code\core\lineage\VassalService.cs'

foreach ($path in @($storePath, $pipelinePath, $indexPath, $modelsPath,
        $coordinatorPath, $tablePath, $archivePath, $vassalPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing military governorate integration source: $path"
    }
}

$store = Get-Content -Raw -LiteralPath $storePath
$pipeline = Get-Content -Raw -LiteralPath $pipelinePath
$indexes = Get-Content -Raw -LiteralPath $indexPath
$models = Get-Content -Raw -LiteralPath $modelsPath
$coordinator = Get-Content -Raw -LiteralPath $coordinatorPath
$table = Get-Content -Raw -LiteralPath $tablePath
$archive = Get-Content -Raw -LiteralPath $archivePath
$vassal = Get-Content -Raw -LiteralPath $vassalPath

foreach ($token in @(
    'private const int RuntimeRestoreBatchLimit',
    'private const int RuntimeRestoreRepairBudget',
    'public static List<MilitaryGovernorateSnapshot> ReadActiveBatch(',
    'WHERE ACTIVE=1 AND STATE_ID>@after',
    'ORDER BY STATE_ID LIMIT @limit',
    'public static void EnqueueRuntimeRestore()',
    'DeferredRuntimeWorkService.EnqueueCoalesced(',
    'public static void ApplyAuthoritativeProjection(',
    'missing_subject_kingdom',
    'missing_suzerain_kingdom',
    'missing_seat_city',
    'missing_governor_actor',
    'missing_successor_actor',
    'missing_vassal_relation'
)) {
    if (-not $store.Contains($token)) {
        throw "Missing bounded governorate restore token: $token"
    }
}

foreach ($token in @(
    'new AW3RestoreStage("military_governorates"',
    'MilitaryGovernorateStore.EnqueueRuntimeRestore'
)) {
    if (-not $pipeline.Contains($token)) {
        throw "Governorate restore is not event-driven: $token"
    }
}

foreach ($token in @(
    'idx_MilitaryGovernorateState_active_state',
    '"ACTIVE, STATE_ID"',
    'idx_MilitaryGovernorateState_subject_active',
    'idx_MilitaryGovernorateState_suzerain_active',
    'idx_MilitaryGovernorateState_relation_active'
)) {
    if (-not $indexes.Contains($token)) {
        throw "Missing governorate hot-path index: $token"
    }
}

foreach ($token in @(
    'public sealed class AW3MultiplayerMilitaryGovernorateProjection',
    'public long StateId { get; }',
    'public long RelationId { get; }',
    'public long SubjectKingdomId { get; }',
    'public long SuzerainKingdomId { get; }',
    'public long SeatCityId { get; }',
    'public long GovernorActorId { get; }',
    'public long SuccessorActorId { get; }',
    'public string CommandName { get; }',
    'public int SuccessionState { get; }',
    'public bool ReplacementAllowed { get; }',
    'public bool Active { get; }',
    'public IReadOnlyList<AW3MultiplayerMilitaryGovernorateProjection> MilitaryGovernorates { get; }'
)) {
    if (-not $models.Contains($token)) {
        throw "Missing authoritative governorate snapshot field: $token"
    }
}

foreach ($token in @(
    'CaptureMilitaryGovernorates()',
    'ApplyMilitaryGovernorate(',
    'CompleteMilitaryGovernorateSnapshot(',
    'pSnapshot.MilitaryGovernorates'
)) {
    if (-not $coordinator.Contains($token)) {
        throw "Missing governorate multiplayer integration: $token"
    }
}

$restoreStart = $store.IndexOf(
    'public static List<MilitaryGovernorateSnapshot> ReadActiveBatch(',
    [StringComparison]::Ordinal)
$restoreEnd = $store.IndexOf(
    'private static bool UpdateId(', [StringComparison]::Ordinal)
if ($restoreStart -lt 0 -or $restoreEnd -le $restoreStart) {
    throw 'Cannot isolate governorate restore implementation.'
}
$restore = $store.Substring($restoreStart, $restoreEnd - $restoreStart)
if ($restore.Contains('EXPEDITIONARY_ARMY_ID') -or
    $restore.Contains('ExpeditionaryArmyId')) {
    throw 'Governorate restore must ignore expeditionary army compatibility data.'
}
if ($restore -match 'World\.world\.units\s*\.' +
        '\s*(units_only_alive|GetEnumerator|ToList|ToArray)' -or
    $restore -match 'foreach\s*\([^)]*World\.world\.units') {
    throw 'Governorate restore contains a global unit scan.'
}
if ($restore -match 'foreach\s*\([^)]*World\.world\.kingdoms' -or
    $restore -match 'foreach\s*\([^)]*World\.world\.cities') {
    throw 'Governorate restore contains a global kingdom or city scan.'
}

foreach ($token in @(
    'TryEndWithRelation(pSnapshot.StateId,',
    'TryEndStateWithActiveMilitaryRelations(',
    'relation.RelationId != pSnapshot.RelationId',
    'VassalService.ClearInvalidMilitaryGovernorateProjection(',
    'WHERE VASSAL_ID=@subject',
    'AND ACTIVE=1 AND END_TIME<0 AND SUBJECT_KIND=@kind',
    'bool stateEnded = false;',
    'stateEnded = true;',
    'if (!stateEnded && !End(pSnapshot.StateId, pReason))'
)) {
    if (-not $restore.Contains($token)) {
        throw "Missing atomic governorate relation repair: $token"
    }
}

$vassalCleanupStart = $vassal.IndexOf(
    'internal static void ClearInvalidMilitaryGovernorateProjection(',
    [StringComparison]::Ordinal)
$vassalCleanupEnd = $vassal.IndexOf(
    'private static List<ActiveVassalRelationIdentity>',
    [StringComparison]::Ordinal)
if ($vassalCleanupStart -lt 0 -or
    $vassalCleanupEnd -le $vassalCleanupStart) {
    throw 'Cannot isolate invalid governorate relation projection cleanup.'
}
$vassalCleanup = $vassal.Substring($vassalCleanupStart,
    $vassalCleanupEnd - $vassalCleanupStart)
if (-not $vassalCleanup.Contains(
        'World.world?.kingdoms?.get(suzerainId)') -or
    $vassalCleanup.Contains('FindKingdom(') -or
    $vassalCleanup -match 'foreach\s*\([^)]*World\.world\.kingdoms') {
    throw 'Invalid governorate cleanup does not use direct kingdom ID lookup.'
}

$applyStart = $restore.IndexOf(
    'public static void ApplyAuthoritativeProjection(',
    [StringComparison]::Ordinal)
$retainStart = $restore.IndexOf(
    'public static void RetainAuthoritativeProjections(',
    [StringComparison]::Ordinal)
$processStart = $restore.IndexOf(
    'private static void ProcessRuntimeRestore()',
    [StringComparison]::Ordinal)
if ($applyStart -lt 0 -or $retainStart -le $applyStart -or
    $processStart -le $retainStart) {
    throw 'Cannot isolate governorate replica projection cleanup.'
}
$applyProjection = $restore.Substring($applyStart,
    $retainStart - $applyStart)
$retainProjection = $restore.Substring($retainStart,
    $processStart - $retainStart)
foreach ($token in @(
    'bool trackedReplica = ReplicaSubjectIds.Remove(pSubject.id);',
    'if (trackedReplica)',
    'ClearReplicaVassalProjection(pSubject);'
)) {
    if (-not $applyProjection.Contains($token)) {
        throw "Inactive replica cleanup is not ownership-scoped: $token"
    }
}
foreach ($token in @(
    'foreach (long subjectId in ReplicaSubjectIds)',
    'ClearReplicaVassalProjection(subject);',
    'ReplicaSubjectIds.Remove(stale[index]);'
)) {
    if (-not $retainProjection.Contains($token)) {
        throw "Stale replica cleanup does not clear tracked vassal keys: $token"
    }
}
foreach ($token in @(
    'LineageKeys.VASSAL_RELATION_ID, -1L',
    'LineageKeys.VASSAL_SUZERAIN_ID, -1L'
)) {
    if (-not $restore.Contains($token)) {
        throw "Replica cleanup leaves an authoritative vassal key: $token"
    }
}

$worldStoreStart = $coordinator.IndexOf(
    'internal sealed class AW3MultiplayerStrategicWorldStore',
    [StringComparison]::Ordinal)
$captureStart = $coordinator.IndexOf('CaptureMilitaryGovernorates()',
    $worldStoreStart, [StringComparison]::Ordinal)
$captureEnd = $coordinator.IndexOf(
    'public bool HasArmy(', [StringComparison]::Ordinal)
if ($captureStart -lt 0 -or $captureEnd -le $captureStart) {
    throw 'Cannot isolate governorate multiplayer capture.'
}
$capture = $coordinator.Substring($captureStart, $captureEnd - $captureStart)
if ($capture.Contains('World.world.units') -or
    $capture.Contains('EXPEDITIONARY_ARMY_ID') -or
    $capture.Contains('ExpeditionaryArmyId')) {
    throw 'Governorate multiplayer capture scans units or exports expeditionary army data.'
}

$hotLoopFiles = @(
    'Code\patch\AW_AgePatch.cs',
    'Code\patch\AW_ActorRacePerformancePatch.cs',
    'Code\patch\AW_KingdomPolicyPatch.cs',
    'Code\core\lineage\ActorAgeWorkService.cs',
    'Code\core\performance\AWAuthorityCycleService.cs'
)
foreach ($relative in $hotLoopFiles) {
    $path = Join-Path $root $relative
    if ((Test-Path -LiteralPath $path) -and
        (Get-Content -Raw -LiteralPath $path).Contains(
            'MilitaryGovernorateStore.ReadActiveBatch(')) {
        throw "Governorate restore was added to an actor-age/update hot loop: $relative"
    }
}

if (-not $table.Contains('[TableItemDef(pDefaultValue: "0")]') -or
    -not $table.Contains('public int replacement_allowed = 0;') -or
    -not $store.Contains('!pReader.IsDBNull(11)') -or
    -not $archive.Contains('_db.AddMissingColumns(tableName, upgradeColumns);')) {
    throw 'Legacy REPLACEMENT_ALLOWED migration/NULL compatibility is not guarded.'
}

Write-Output 'Military governorate performance source guard passed.'
