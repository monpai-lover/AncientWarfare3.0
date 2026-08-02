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
        'if\s*\(\s*!pUseLightweightEdges\s*\)\s*return false;' -or
    $service -notmatch
        'LineageService\.RecordLightweightParentEdges\s*\(') {
    throw 'The western birth service must require the precomputed lightweight ownership decision.'
}
if ($service -match '\.ResolveForActor\s*\(' -or
    $service -match '\.Ensure\s*\(') {
    throw 'The lightweight writer must not resolve or mutate naming state.'
}

foreach ($forbidden in @(
    'ArchiveActor', 'ArchiveTraceableActor', 'LineageArchiveWriter',
    'ActorArchive', 'EnsureLineageForNoble', 'InsertShiBranch',
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
    'internal static bool RecordLightweightParentEdges\s*\(') {
    throw 'LineageService must expose only a minimal internal lightweight edge API.'
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
        '!LineageService\.UsesAwLineageSystem\(__result\)\) return;' -or
    $babyName -notmatch 'LineageService\.ArchiveActor\(__result') {
    throw 'Post-birth archive work must stay behind the stable lineage-id gate.'
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
        '(?s)InheritFromParents\s*\(.*RecordFamilyEdges\s*\(\s*pBaby,\s*pParent1,\s*pParent2\s*\)') {
    throw 'The pre-admitted full path must record final-lineage edges only after inheritance.'
}
if ($mixedBirthBody -notmatch 'bool\s+pParentEdgesOwned' -or
    $mixedBirthBody -notmatch
        'if\s*\(\s*!pParentEdgesOwned\s*\)\s*RecordFamilyEdges' -or
    $mixedBirthBody -match 'ShouldUseLineageBirth\s*\(' -or
    $mixedBirthBody -match 'WesternLineageParentEdgeService\.IsEligible') {
    throw 'Mixed ancestry may write edges only when neither lightweight nor full birth owns them.'
}

$recordStart = $lineage.IndexOf(
    'private static bool RecordFamilyEdges',
    [System.StringComparison]::Ordinal)
$upsertStart = $lineage.IndexOf(
    'private static bool UpsertFamilyEdge',
    $recordStart, [System.StringComparison]::Ordinal)
if ($recordStart -lt 0 -or $upsertStart -lt $recordStart) {
    throw 'The bounded FamilyEdge writer could not be found.'
}
$recordBody = $lineage.Substring($recordStart,
    $upsertStart - $recordStart)
if ([regex]::Matches($recordBody,
        'UpsertFamilyEdge\s*\(').Count -ne 2 -or
    $recordBody -notmatch 'BeginTransaction\s*\(' -or
    [regex]::Matches($recordBody,
        'AdvanceAfterSuccessfulSynchronousWrite\s*\(').Count -ne 1 -or
    $recordBody -notmatch 'transaction\.Commit' -or
    $recordBody -notmatch 'transaction\.Rollback\s*\(') {
    throw 'Both parent slots must share one transaction, rollback together, and advance history once after commit.'
}
if ($lineage -notmatch 'using System\.Data\.SQLite;' -or
    $lineage -notmatch
        '(?s)private static bool UpsertFamilyEdge\s*\(\s*SQLiteConnection.*SQLiteTransaction.*new SQLiteCommand.*Transaction\s*=\s*pTransaction.*UPDATE.*INSERT') {
    throw 'FamilyEdge upserts must use direct SQLite commands in the shared transaction.'
}
if ([regex]::Matches($recordBody,
        'UpsertFamilyEdge\s*\(').Count -ne 2) {
    throw 'Each birth must perform at most the two parent-slot upserts.'
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
