$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Read-Source([string] $RelativePath) {
    $path = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing required source: $RelativePath"
    }
    return Get-Content -LiteralPath $path -Raw -Encoding UTF8
}

$service = Read-Source `
    'Code/core/lineage/WesternLineageParentEdgeService.cs'
$birthArchive = Read-Source `
    'Code/core/lineage/LineageBirthArchiveService.cs'
$birthModels = Read-Source `
    'Code/core/lineage/LineageBirthArchiveModels.cs'
$birthEnvelope = Read-Source `
    'Code/core/lineage/LineageBirthArchiveAsyncWrite.cs'
$archiveWriter = Read-Source `
    'Code/core/lineage/LineageArchiveWriter.cs'
$lineage = Read-Source 'Code/core/lineage/LineageService.cs'
$birth = Read-Source 'Code/patch/AW_BirthPatch.cs'
$naming = Read-Source `
    'Code/core/naming/AWCultureNamingTraditionService.cs'
$babyName = Read-Source 'Code/patch/AW_BabyNamePatch.cs'
$death = Read-Source 'Code/patch/AW_ActorDeathPatch.cs'
$edgeTable = Read-Source 'Code/core/db/FamilyEdgeTableItem.cs'
$unitTab = Read-Source 'Code/patch/AW_UnitTabPatch.cs'
$migration = Read-Source `
    'Code/core/lineage/WesternLineageMigrationService.cs'

if ($service -notmatch
        '(?s)if\s*\(\s*!pUseLightweightEdges\s*\).*' +
        'LineageBirthArchiveStatus\.NotEligible' -or
    $service -notmatch
        'LineageBirthArchiveService\.TryRecord\s*\(') {
    throw 'The western birth service must require the precomputed lightweight ownership decision.'
}
if ($service -match '\.ResolveForActor\s*\(' -or
    $service -match '\.Ensure\s*\(') {
    throw 'The lightweight writer must not resolve or mutate naming state.'
}

foreach ($forbidden in @(
    'ArchiveTraceableActor', 'LineageArchiveWriter',
    'EnsureLineageForNoble', 'InsertShiBranch',
    'CreateFamily', 'portrait')) {
    if ($service -match [regex]::Escape($forbidden)) {
        throw "Western commoner edge recording must not use $forbidden."
    }
}

foreach ($scan in @(
    'World.world.units', 'getSimpleList', 'getUnits(', 'MapBox.Update',
    'updateAge', 'updateYear', 'foreach')) {
    if ($service -match [regex]::Escape($scan)) {
        throw "Western parent-edge recording contains a forbidden world/annual scan marker: $scan"
    }
}

foreach ($runtimeState in @(
    'Queue<', 'ConcurrentQueue', 'Dictionary<', 'HashSet<', 'List<',
    'Task.Run', 'async ')) {
    if ($service -match [regex]::Escape($runtimeState)) {
        throw "Synchronous western edge recording must not retain runtime state: $runtimeState"
    }
}

$hookIndex = $birth.IndexOf(
    'public static void ApplyParentsMeta_Postfix',
    [System.StringComparison]::Ordinal)
$gateIndex = $birth.IndexOf(
    'if (AW3MultiplayerReplicaScope.IsApplying) return;',
    $hookIndex, [System.StringComparison]::Ordinal)
$decisionIndex = $birth.IndexOf(
    'LineageService.ResolveBirthAdmissionDecision',
    $hookIndex, [System.StringComparison]::Ordinal)
$serviceIndex = $birth.IndexOf(
    'WesternLineageParentEdgeService.RecordBirth',
    $hookIndex, [System.StringComparison]::Ordinal)
$fullPathIndex = $birth.IndexOf(
    'LineageService.OnActorBornWithParents',
    $hookIndex, [System.StringComparison]::Ordinal)
if ($hookIndex -lt 0 -or $gateIndex -lt $hookIndex -or
    $decisionIndex -lt $gateIndex -or $serviceIndex -lt $decisionIndex -or
    $fullPathIndex -lt $serviceIndex) {
    throw 'BabyHelper.applyParentsMeta must decide unique edge ownership before either birth writer runs.'
}
if ([regex]::Matches($birth,
        'LineageService\.ResolveBirthAdmissionDecision\s*\(').Count -ne 1 -or
    $birth -notmatch
        '(?s)if\s*\(decision\.UseLightweightEdges\).*WesternLineageParentEdgeService\.RecordBirth' -or
    $birth -notmatch
        '(?s)OnActorBornWithParents\s*\(\s*pBaby,\s*pParent1,\s*pParent2,\s*decision\.UseFullPath\s*\)' -or
    $birth -notmatch
        'parentEdgesOwned\s*=\s*decision\.UseFullPath\s*\|\|\s*decision\.UseLightweightEdges' -or
    $birth -notmatch
        '(?s)OnMixedAncestryBorn\s*\(\s*pBaby,\s*pParent1,\s*pParent2,\s*parentEdgesOwned\s*\)') {
    throw 'The birth hook must use one decision for lightweight, full, and mixed parent-edge ownership.'
}
foreach ($warning in @(
    'Western lineage admission decision failed:',
    'Western lightweight parent-edge recording failed:',
    'Lineage birth processing failed:',
    'Birth chronicle processing failed:',
    'Mixed ancestry birth processing failed:')) {
    if ($birth -notmatch [regex]::Escape($warning)) {
        throw "Birth processing must isolate and warn after failure: $warning"
    }
}
if ([regex]::Matches($birth,
        'ChronicleEvents\.OnHadChild\s*\(').Count -ne 1 -or
    $birth -notmatch
        'try\s*\{\s*ChronicleEvents\.OnHadChild\s*\(') {
    throw 'The birth hook must dispatch the chronicle event exactly once behind its exception boundary.'
}

if ($lineage -notmatch
    'WesternLineageEligibilityRules\.UsesAwLineageSystem\s*\(' -or
    $lineage -notmatch
    'lineageId\s*>=\s*0') {
    throw 'UsesAwLineageSystem must be profile-aware and require a stable lineage id.'
}
$usesAwStart = $lineage.IndexOf(
    'public static bool UsesAwLineageSystem',
    [System.StringComparison]::Ordinal)
$usesAwEnd = $lineage.IndexOf(
    'private static bool CanUseXiaizedLineageGovernment',
    $usesAwStart, [System.StringComparison]::Ordinal)
if ($usesAwStart -lt 0 -or $usesAwEnd -lt $usesAwStart) {
    throw 'UsesAwLineageSystem method body could not be located.'
}
$usesAwBody = $lineage.Substring($usesAwStart,
    $usesAwEnd - $usesAwStart)
$stableAssignmentIndex = $usesAwBody.IndexOf(
    'bool hasStableLineageId = lineageId >= 0L;',
    [System.StringComparison]::Ordinal)
$stableReturnIndex = $usesAwBody.IndexOf(
    'if (!hasStableLineageId) return false;',
    [System.StringComparison]::Ordinal)
$nativeProfileIndex = $usesAwBody.IndexOf(
    'if (IsNativeXiaCultureActor(pActor))',
    [System.StringComparison]::Ordinal)
$readOnlyProfileIndex = $usesAwBody.IndexOf(
    '.ResolveForActorReadOnly(pActor)',
    [System.StringComparison]::Ordinal)
if ($stableAssignmentIndex -lt 0 -or
    $stableReturnIndex -lt $stableAssignmentIndex -or
    $nativeProfileIndex -lt $stableReturnIndex -or
    $readOnlyProfileIndex -lt $stableReturnIndex) {
    throw 'UsesAwLineageSystem must reject actors without stable lineage before native or culture-profile resolution.'
}
$decisionStart = $lineage.IndexOf(
    'internal static WesternLineageBirthAdmissionDecision ResolveBirthAdmissionDecision',
    [System.StringComparison]::Ordinal)
$admissionStart = $lineage.IndexOf(
    'internal static bool RequiresFullArchiveAdmission',
    [System.StringComparison]::Ordinal)
if ($decisionStart -lt 0 -or $admissionStart -lt $decisionStart) {
    throw 'The runtime birth-admission decision method could not be located.'
}
$decisionBody = $lineage.Substring($decisionStart,
    $admissionStart - $decisionStart)
if ([regex]::Matches($decisionBody,
        'ResolveForActorReadOnly\s*\(').Count -ne 1 -or
    $decisionBody -notmatch
        'WesternLineageEligibilityRules\.ResolveBirthAdmission\s*\(' -or
    $decisionBody -match '\.ResolveForActor\s*\(' -or
    $decisionBody -match '\.Ensure\s*\(') {
    throw 'The runtime birth gate must resolve the effective profile once without mutating naming state.'
}
if ($lineage -notmatch
        'AWCultureNamingTraditionService\s*\.ResolveForActorReadOnly\(pActor\)\.Profile') {
    throw 'Lineage query paths must use read-only naming resolution.'
}
if ($lineage -match
        'AWCultureNamingTraditionService\s*\.ResolveForActor\s*\(') {
    throw 'Lineage query and birth paths must not use the mutating naming resolver.'
}

$readOnlyStart = $naming.IndexOf(
    'internal static AWCultureNamingTradition ResolveForActorReadOnly',
    [System.StringComparison]::Ordinal)
$persistStart = $naming.IndexOf(
    'private static AWCultureNamingTradition Persist',
    $readOnlyStart, [System.StringComparison]::Ordinal)
if ($readOnlyStart -lt 0 -or $persistStart -lt $readOnlyStart) {
    throw 'The read-only actor naming resolver could not be located.'
}
$readOnlyBody = $naming.Substring($readOnlyStart,
    $persistStart - $readOnlyStart)
foreach ($mutation in @('.set(', 'removeString(', 'Persist(', 'Ensure(')) {
    if ($readOnlyBody -match [regex]::Escape($mutation)) {
        throw "Read-only naming resolution must not call $mutation"
    }
}
if ($babyName -notmatch
        '(?s)!LineageService\.UsesAwLineageSystem\(__result\).*' +
        '!LineageService\.HasTraceableArchive\(__result\)\) return;' -or
    $babyName -notmatch
        'LineageService\.RefreshArchivedIdentity\s*\(\s*__result\s*\)' -or
    $babyName -match 'LineageBirthArchiveService\.TryRecord' -or
    $babyName -match 'FamilyTreeProjectionChange\.FamilyStructure') {
    throw 'Post-birth naming must refresh identity only and must not record birth or advance family structure.'
}
if ($unitTab -notmatch
    'showFamily\s*=\s*hasActor\s*&&\s*LineageService\.HasTraceableFamily\(actor\)') {
    throw 'Lightweight edges alone must not expose the family-tree button.'
}
$fullBirthStart = $lineage.IndexOf(
    'public static void OnActorBornWithParents',
    [System.StringComparison]::Ordinal)
$mixedBirthStart = $lineage.IndexOf(
    'public static void OnMixedAncestryBorn',
    $fullBirthStart, [System.StringComparison]::Ordinal)
$archiveReadStart = $lineage.IndexOf(
    'public static bool HasTraceableArchive',
    $mixedBirthStart, [System.StringComparison]::Ordinal)
if ($fullBirthStart -lt 0 -or $mixedBirthStart -lt $fullBirthStart -or
    $archiveReadStart -lt $mixedBirthStart) {
    throw 'Birth edge ownership methods could not be located.'
}
$fullBirthBody = $lineage.Substring($fullBirthStart,
    $mixedBirthStart - $fullBirthStart)
$mixedBirthBody = $lineage.Substring($mixedBirthStart,
    $archiveReadStart - $mixedBirthStart)
if ($fullBirthBody -notmatch
        'bool\s+pUseFullPath' -or
    $fullBirthBody -notmatch
        'if\s*\(\s*!pUseFullPath\s*\)\s*return;' -or
    $fullBirthBody -match 'ShouldUseLineageBirth\s*\(' -or
    $fullBirthBody -match 'WesternLineageParentEdgeService\.IsEligible' -or
    $fullBirthBody -notmatch
        '(?s)InheritFromParents\s*\(.*ApplyDisplayName\s*\(\s*pBaby\s*\).*' +
        'LineageBirthArchiveService\.TryRecord\s*\(\s*pBaby,\s*pParent1,\s*pParent2\s*\)' -or
    $fullBirthBody -match 'RecordFamilyEdges\s*\(' -or
    $fullBirthBody -match 'ArchiveActor\s*\(\s*pBaby') {
    throw 'The pre-admitted full path must route its final snapshot and both parent edges through the atomic birth service.'
}
if ($mixedBirthBody -notmatch 'bool\s+pParentEdgesOwned' -or
    $mixedBirthBody -notmatch
        '(?s)if\s*\(\s*!pParentEdgesOwned\s*\)\s*' +
        'LineageBirthArchiveService\.TryRecord\s*\(\s*pBaby,\s*pParent1,\s*pParent2\s*\)' -or
    $mixedBirthBody -match 'ShouldUseLineageBirth\s*\(' -or
    $mixedBirthBody -match 'WesternLineageParentEdgeService\.IsEligible' -or
    $mixedBirthBody -match 'RecordFamilyEdges\s*\(' -or
    $mixedBirthBody -match 'ArchiveTraceableActor\s*\(\s*pBaby') {
    throw 'Mixed ancestry may own the atomic child archive only when neither lightweight nor full birth owns it.'
}

$plainBirthStart = $lineage.IndexOf(
    'public static void OnActorBorn(Actor pActor)',
    [System.StringComparison]::Ordinal)
$plainBirthBody = $lineage.Substring($plainBirthStart,
    $fullBirthStart - $plainBirthStart)
if ($plainBirthStart -lt 0 -or
    $plainBirthBody -notmatch 'EnsureGivenName\s*\(' -or
    $plainBirthBody -notmatch 'ApplyDisplayName\s*\(' -or
    $plainBirthBody -match 'ArchiveActor\s*\(') {
    throw 'OnActorBorn must remain name initialization without pre-parent archive persistence.'
}

foreach ($retired in @(
    'RecordFamilyEdges', 'RecordLightweightParentEdges',
    'UpsertFamilyEdge')) {
    if ($lineage -match [regex]::Escape($retired)) {
        throw "LineageService must not retain direct edge-only birth persistence: $retired"
    }
}

if ($archiveWriter -notmatch
        'internal static ActorArchiveTableItem CaptureRelationshipSnapshot\s*\(' -or
    $archiveWriter -notmatch
        '(?s)private static bool Upsert\s*\(.*CaptureRelationshipSnapshot\s*\(' -or
    $archiveWriter -notmatch
        'internal static bool RefreshIdentity\s*\(') {
    throw 'LineageArchiveWriter must share snapshot capture and expose an identity-only refresh path.'
}
if ($archiveWriter -notmatch
        '(?s)!LineageService\.UsesAwLineageSystem\(pActor\).*' +
        '!LineageService\.HasOriginalClan\(pActor\).*' +
        '\(!pTraceOnly\s*\|\|\s*!traceableSpecies\).*' +
        '\(!pIdentityOnlyProjection\s*\|\|\s*previous\s*==\s*null\)') {
    throw 'Only identity-only refresh may bypass standalone archive admission for an existing row.'
}
$refreshIdentityStart = $archiveWriter.IndexOf(
    'internal static bool RefreshIdentity',
    [System.StringComparison]::Ordinal)
$captureCoreStart = $archiveWriter.IndexOf(
    'private static ActorArchiveTableItem CaptureRelationshipSnapshot',
    $refreshIdentityStart, [System.StringComparison]::Ordinal)
$identityProjectionStart = $archiveWriter.IndexOf(
    'private static FamilyTreeProjectionChange ResolveIdentityProjectionChange',
    [System.StringComparison]::Ordinal)
$fullProjectionStart = $archiveWriter.IndexOf(
    'private static FamilyTreeProjectionChange ResolveProjectionChange',
    [System.StringComparison]::Ordinal)
if ($refreshIdentityStart -lt 0 -or $captureCoreStart -lt 0 -or
    $identityProjectionStart -lt 0 -or
    $fullProjectionStart -lt $identityProjectionStart) {
    throw 'The bounded identity-only archive projection path could not be located.'
}
$refreshIdentityBody = $archiveWriter.Substring($refreshIdentityStart,
    $captureCoreStart - $refreshIdentityStart)
$identityProjectionBody = $archiveWriter.Substring($identityProjectionStart,
    $fullProjectionStart - $identityProjectionStart)
if ($refreshIdentityBody -notmatch
        'pIdentityOnlyProjection:\s*true' -or
    $identityProjectionBody -notmatch
        'pPrevious\.sex\s*!=\s*pCurrent\.sex' -or
    $identityProjectionBody -match
        'FamilyTreeProjectionChange\.FamilyStructure') {
    throw 'Baby-name identity refresh must be structurally unable to advance FamilyStructure.'
}

if ($birthModels -notmatch 'enum LineageBirthArchiveStatus' -or
    $birthModels -notmatch '(?s)NotEligible.*Queued.*Committed.*Failed' -or
    $birthModels -notmatch
        'readonly struct LineageBirthArchiveResult' -or
    $birthModels -notmatch
        '(?s)Accepted\s*=>\s*Status\s*==\s*LineageBirthArchiveStatus\.Queued\s*\|\|\s*' +
        'Status\s*==\s*LineageBirthArchiveStatus\.Committed') {
    throw 'Birth callers must receive a structured four-state result whose Accepted flag excludes NotEligible and Failed.'
}
if ($birthEnvelope -notmatch '"lineage-birth:v1:child:"' -or
    $birthEnvelope -notmatch 'HistoricalWriteKind\.State') {
    throw 'Each child birth must use one versioned State envelope key.'
}
if ($birthArchive -notmatch
        'internal static LineageBirthArchiveResult TryRecord\s*\(' -or
    $birthArchive -notmatch
        'LineageArchiveWriter\.\s*CaptureRelationshipSnapshot\s*\(' -or
    $birthArchive -notmatch
        'HistoricalWriteService\.TryEnqueueCustom\s*\(' -or
    $birthArchive -notmatch 'FlushForSynchronousFallback\s*\(' -or
    $birthArchive -notmatch
        '(?s)BeginTransaction\s*\(\).*LineageBirthArchivePersistence\.Execute\s*\(.*transaction\.Commit\s*\(' -or
    $birthArchive -notmatch 'transaction\.Rollback\s*\(') {
    throw 'The birth archive service must enqueue captured state and use one transactional synchronous fallback.'
}
$enqueueIndex = $birthArchive.IndexOf(
    'HistoricalWriteService.TryEnqueueCustom',
    [System.StringComparison]::Ordinal)
$actorPublishIndex = $birthArchive.IndexOf(
    'ActorArchivePendingStore.Publish',
    [System.StringComparison]::Ordinal)
$projectionPublishIndex = $birthArchive.IndexOf(
    'FamilyTreeProjectionPendingStore.Publish',
    [System.StringComparison]::Ordinal)
if ($enqueueIndex -lt 0 -or $actorPublishIndex -lt $enqueueIndex -or
    $projectionPublishIndex -lt $enqueueIndex) {
    throw 'Birth pending state must be published only after queue acceptance.'
}
if ($birthArchive -notmatch
        '(?s)OnCommitted\s*\(.*ActorArchivePendingStore\.Complete.*' +
        'FamilyTreeProjectionPendingStore\.TryComplete.*' +
        'FamilyTreeProjectionRevision\.Advance\s*\(\s*committedChange\s*\)' -or
    $birthArchive -notmatch
        '(?s)OnFailed\s*\(.*ActorArchivePendingStore\.Complete.*' +
        'FamilyTreeProjectionPendingStore\.TryComplete' -or
    [regex]::Matches($birthArchive,
        'FamilyTreeProjectionRevision\.Advance\s*\(').Count -ne 2 -or
    [regex]::Matches($birthArchive,
        'FamilyTreeProjectionChange\.FamilyStructure').Count -ne 2) {
    throw 'Birth completion must clear pending state and advance FamilyStructure exactly once after async or synchronous commit.'
}
if ($birthArchive -match
        'HistoricalContentRevision\.AdvanceAfterSuccessfulSynchronousWrite' -or
    $birthArchive -match 'HistoricalContentRevision\.Advance\s*\(') {
    throw 'Synchronous birth fallback must let its single FamilyStructure advance publish the one historical revision.'
}
$notEligibleEnd = $birthArchive.IndexOf(
    'try', [System.StringComparison]::Ordinal)
$notEligibleBody = $birthArchive.Substring(0, $notEligibleEnd)
if ($birthArchive -notmatch
        '(?s)LineageBirthArchiveStatus\.Failed.*LogFailedRateLimited' -or
    $notEligibleBody -match 'LogFailedRateLimited') {
    throw 'Only failed birth archive results may use the centralized rate-limited warning.'
}

$syncLiveStart = $lineage.IndexOf(
    'private static bool TrySyncLiveChildFromParent',
    [System.StringComparison]::Ordinal)
$syncArchivedStart = $lineage.IndexOf(
    'private static bool TrySyncArchivedChildFromParent',
    $syncLiveStart, [System.StringComparison]::Ordinal)
if ($syncLiveStart -lt 0 -or $syncArchivedStart -lt $syncLiveStart) {
    throw 'The bounded live-child sync path could not be located.'
}
$syncLiveBody = $lineage.Substring($syncLiveStart,
    $syncArchivedStart - $syncLiveStart)
if ($syncLiveBody -match 'LineageBirthArchiveService\.TryRecord' -or
    $syncLiveBody -match 'RecordFamilyEdges' -or
    $syncLiveBody -notmatch
        'ArchiveActor\s*\(\s*pChild,\s*pAlive:\s*true,\s*pFinalizeProjection:\s*!pDeferProjection\s*\)') {
    throw 'Live-child lineage sync must honor defer through archive projection finalization and must not reuse birth persistence.'
}

$admissionEnd = $lineage.IndexOf('private static void RecordBirthEvent',
    $admissionStart, [System.StringComparison]::Ordinal)
$admissionBody = $lineage.Substring($admissionStart,
    $admissionEnd - $admissionStart)
if ($admissionBody -notmatch
    '(?s)try\s*\{\s*nobleTrait\s*=\s*pActor\.hasTrait\(LineageKeys\.TRAIT_GUIZU\);\s*\}\s*catch') {
    throw 'Full-archive admission must safely read the noble trait.'
}
if ($edgeTable -notmatch
    '\[TableItemDef\(pIsPrimary:\s*true\)\]\s*public long edge_id') {
    throw 'FamilyEdge must retain its O(1) EDGE_ID primary-key upsert path.'
}

if ($death -match '(?is)DELETE\s+FROM\s+FamilyEdge' -or
    $lineage -match '(?is)DELETE\s+FROM\s+FamilyEdge') {
    throw 'Death handling must preserve lightweight parent edges.'
}

if ($migration -notmatch
        '(?s)WesternLineageAdmissionService\.TryEnsure\s*\(.*ruler.*LineageService\.SyncExistingChildrenAfterLineageChange\s*\(\s*pActor') {
    throw 'Old-save migration must backfill a reigning ruler''s pre-accession children after lineage admission.'
}

Write-Output 'Western lineage parent-edge source guard passed.'
