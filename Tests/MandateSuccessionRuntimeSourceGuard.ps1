$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$chronicle = Get-Content -Raw (Join-Path $root 'Code/core/lineage/ChronicleEvents.cs')
$mandate = Get-Content -Raw (Join-Path $root 'Code/core/lineage/MandateService.cs')
$reigns = Get-Content -Raw (Join-Path $root 'Code/core/lineage/ReignRecordWriter.cs')
$reignAccession = Get-Content -Raw (Join-Path $root `
    'Code/core/lineage/ReignAccessionPersistence.cs')
$declarationPersistence = Get-Content -Raw (Join-Path $root `
    'Code/core/lineage/MandateDeclarationPersistence.cs')
$projectionOutbox = Get-Content -Raw (Join-Path $root `
    'Code/core/lineage/MandateProjectionOutboxPersistence.cs')
$facts = Get-Content -Raw (Join-Path $root 'Code/core/lineage/RulerTitleFactService.cs')
$posthumous = Get-Content -Raw (Join-Path $root 'Code/core/lineage/PosthumousTitleService.cs')
$history = Get-Content -Raw (Join-Path $root 'Code/core/lineage/HistoryWriter.cs')
$dynastyWriter = Get-Content -Raw (Join-Path $root `
    'Code/core/lineage/DynastyRecordWriter.cs')
$dynastyRules = Get-Content -Raw (Join-Path $root `
    'Code/core/lineage/DynastyTransitionRules.cs')
$restore = Get-Content -Raw (Join-Path $root `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs')
$indexes = Get-Content -Raw (Join-Path $root `
    'Code/core/db/LineageArchiveIndexRules.cs')
$kingdomHistoryTable = Get-Content -Raw (Join-Path $root `
    'Code/core/db/KingdomHistoryTableItem.cs')
$personHistoryTable = Get-Content -Raw (Join-Path $root `
    'Code/core/db/PersonBiographyTableItem.cs')
$mandateEventTable = Get-Content -Raw (Join-Path $root `
    'Code/core/db/MandateEventTableItem.cs')
$mandateCoreTable = Get-Content -Raw (Join-Path $root `
    'Code/core/db/MandateCoreCityTableItem.cs')

function WithoutLineComments([string]$content) {
    return [regex]::Replace($content, '(?m)//.*$', '')
}

$chronicleCode = WithoutLineComments $chronicle

function Require([string]$content, [string]$needle, [string]$message) {
    if (-not $content.Contains($needle)) { throw $message }
}

function RequireOrder([string]$content, [string]$first, [string]$second,
    [string]$message) {
    $firstIndex = $content.IndexOf($first)
    $secondIndex = $content.IndexOf($second)
    if ($firstIndex -lt 0 -or $secondIndex -le $firstIndex) {
        throw $message
    }
}

function RequireMethodGate([string]$content, [string]$signature,
    [string]$nextSignature, [string]$firstMutation,
    [string]$message) {
    $start = $content.IndexOf($signature)
    $end = $content.IndexOf($nextSignature, $start + $signature.Length)
    if ($start -lt 0 -or $end -le $start) { throw $message }
    $method = $content.Substring($start, $end - $start)
    RequireOrder $method 'MandateAuthorityMutationRules.CanMutate' `
        $firstMutation $message
}

Require $chronicleCode 'MandateService.OnRulerSucceeded(' `
    'normal accession does not settle the active Mandate ruler projection'
Require $chronicle 'isActiveMandate: MandateService.IsMandateKingdom(pKingdom)' `
    'active Mandate accession can still project a branch state name'
Require $mandate 'public static bool OnRulerSucceeded' `
    'MandateService exposes no synchronous succession settlement boundary'
Require $mandate 'MandateSuccessionPersistence.TryRefreshRuler' `
    'Mandate succession has no checked persistent ruler refresh'
RequireOrder $mandate 'MandateSuccessionPersistence.TryRefreshRuler' `
    'KingdomTitleService.SetTitle(pKingdom, KingdomTitle.Emperor)' `
    'Mandate runtime projection occurs before persistence succeeds'
Require $mandate 'MandateSuccessionRules.ShouldRefreshRulerProjection' `
    'Mandate succession does not validate the installed live ruler'
Require $mandate 'MandateSuccessionRules.ShouldTransferRulerTrait' `
    'Mandate succession does not distinguish a real ruler transfer'
Require $mandate 'previousRuler.removeTrait(TRAIT_TIANMING)' `
    'the former emperor retains the Mandate trait after succession'
Require $mandate 'pNewKing.addTrait(TRAIT_TIANMING)' `
    'the installed emperor does not receive the Mandate trait'
Require $mandate 'KingdomTitleService.SetTitle(pKingdom, KingdomTitle.Emperor)' `
    'an active Mandate realm can remain downgraded after succession'
Require $mandate 'FamilyTreeProjectionChange.RankOrMandate' `
    'Mandate succession does not invalidate its display projection'
Require $mandate 'MandateDeclarationPersistence.TryCommit' `
    'Mandate establishment does not use its atomic persistence boundary'
Require $mandate 'DrainMandateProjection(pKingdom, pending)' `
    'Mandate declaration does not drain its projection outbox after commit'
Require $declarationPersistence 'pDb.BeginTransaction()' `
    'Mandate declaration persistence does not start a transaction'
Require $declarationPersistence 'ReignMandateProjectionPersistence.TryProject' `
    'Mandate declaration transaction omits the reign projection'
Require $declarationPersistence 'transaction.Rollback()' `
    'Mandate declaration cannot roll back a failed reign projection'
Require $declarationPersistence 'MandateProjectionOutboxPersistence.TryEnqueue' `
    'Mandate declaration does not enqueue projection work in its transaction'
Require $declarationPersistence `
    'CoreCitySnapshots = pRequest.CoreCitySnapshots' `
    'Mandate declaration transaction omits its legal-core child snapshot'
Require $projectionOutbox 'MandateProjectionCoreSnapshot' `
    'Mandate projection outbox has no legal-core child table'
Require $projectionOutbox 'TryMigrateLegacyCoreSnapshots' `
    'legacy current-period outbox cannot be snapshotted exactly once'
Require $projectionOutbox 'DELETE FROM " + CoreSnapshotTable' `
    'completed Mandate projection does not clean up its core snapshots'
Require $history 'public static bool TryRecordKingdom' `
    'Mandate kingdom history publication is not observable'
Require $history 'public static bool TryRecordPerson' `
    'Mandate person history publication is not observable'
Require $mandate 'private static bool RecordEvent' `
    'Mandate event publication is not observable'
Require $history 'TryApplyIdempotentRecord' `
    'Mandate history does not commit by stable projection key'
Require $history 'TryRecordKingdomSnapshot' `
    'Mandate kingdom history cannot replay from an immutable snapshot'
Require $history 'TryRecordPersonSnapshot' `
    'Mandate person history cannot replay from an immutable snapshot'
Require $mandate 'HistoryWriter.TryRecordKingdomSnapshot' `
    'Mandate kingdom history still depends on a live kingdom'
Require $mandate 'HistoryWriter.TryRecordPersonSnapshot' `
    'Mandate person history still depends on a live declaration ruler'
Require $mandate 'RecordSnapshotEvent(' `
    'Mandate events still depend on live actors or the current report'
Require $mandate 'NewYearPrefix = HistoryWriter.BuildYearPrefix(now,' `
    'Mandate declaration does not snapshot its year prefix'
Require $mandate 'PreviousYearPrefix = HistoryWriter.BuildYearPrefix(now,' `
    'Mandate replacement does not snapshot the previous realm year prefix'
Require $mandate 'PreviousKingdomColor = replacingActiveMandate' `
    'Mandate replacement does not capture the previous realm color'
Require $mandate 'previousReport.kingdom_color' `
    'previous realm color still depends on a live or surviving kingdom'
Require $mandate 'pPending.OperationKey + ":new_mandate_event"' `
    'Mandate start event has no stable projection key'
Require $mandate 'pPending.OperationKey + ":old_mandate_event"' `
    'Mandate end event has no stable projection key'
Require $mandate 'pPending.OperationKey + ":new_kingdom_history"' `
    'Mandate kingdom history has no stable projection key'
Require $mandate 'pPending.OperationKey + ":new_person_history"' `
    'Mandate person history has no stable projection key'
Require $mandate 'pPending.OperationKey + ":legal_cores:"' `
    'Mandate legal cores have no stable per-city projection key'
Require $kingdomHistoryTable 'projection_key' `
    'KingdomHistory cannot persist a stable Mandate projection key'
Require $personHistoryTable 'projection_key' `
    'PersonBiography cannot persist a stable Mandate projection key'
Require $mandateEventTable 'projection_key' `
    'MandateEvent cannot persist a stable Mandate projection key'
Require $mandateCoreTable 'projection_key' `
    'MandateCoreCity cannot persist a stable Mandate projection key'
Require $indexes 'uq_KingdomHistory_projection' `
    'KingdomHistory projection keys are not unique'
Require $indexes 'uq_PersonBiography_projection' `
    'PersonBiography projection keys are not unique'
Require $indexes 'uq_MandateEvent_projection' `
    'MandateEvent projection keys are not unique'
Require $indexes 'uq_MandateCoreCity_projection' `
    'Mandate legal-core projection keys are not unique'
if ($indexes.Contains('uq_MandateCoreCity_period_city_active')) {
    throw 'legacy duplicate active legal cores can block archive migration'
}
Require $declarationPersistence 'ReignMandateProjectionPersistence.TryProject' `
    'Mandate reign projection does not use the checked identity update'
Require $declarationPersistence 'pRequest.RulerActorId' `
    'Mandate reign projection is not bound to the installed ruler'
Require $facts 'RulerTitleFactRules.ResolveSavedHighestTitle' `
    'posthumous facts still derive rank from downgraded live state'
Require $posthumous 'RulerTitleFactRules.ResolveSavedHighestTitle' `
    'posthumous context can overwrite the saved imperial rank'

$sameKingStart = $chronicleCode.IndexOf('if (lastKingId == pNewKing.data.id)')
$sameKingEnd = $chronicleCode.IndexOf('RecordPreviousKingLostThrone',
    $sameKingStart)
if ($sameKingStart -lt 0 -or $sameKingEnd -le $sameKingStart -or
    -not $chronicleCode.Substring($sameKingStart,
        $sameKingEnd - $sameKingStart).Contains(
            'ReignRecordWriter.TryTransitionReign(')) {
    throw 'same-ruler retry does not rerun the atomic reign transition'
}

$openReign = $chronicleCode.LastIndexOf(
    'ReignRecordWriter.TryTransitionReign(')
$installedKing = $chronicleCode.LastIndexOf(
    'pKingdom.king?.data?.id == pNewKing.data.id')
$normalMandate = $chronicleCode.LastIndexOf(
    'MandateService.OnRulerSucceeded(')
$lastKingProjection = $chronicleCode.LastIndexOf(
    'ReignRecordWriter.ProjectCurrentReignStart(')
if ($openReign -lt 0 -or $installedKing -le $openReign -or
    $normalMandate -le $installedKing -or
    $lastKingProjection -le $normalMandate) {
    throw 'normal accession does not ensure its open reign and installed king before Mandate commit'
}

Require $reignAccession 'pDb.BeginTransaction()' `
    'ruler accession does not transactionally close and open reigns'
RequireOrder $reignAccession 'CloseOld(' 'InsertNew(' `
    'ruler accession does not close the old reign before opening the new reign'
Require $reignAccession 'command.ExecuteNonQuery() != 1' `
    'ruler accession does not observe failed close or insert writes'
Require $declarationPersistence 'ExpectedPreviousActive' `
    'Mandate replacement does not compare the expected old active holder'
RequireOrder $declarationPersistence 'EndPreviousPeriod(' 'InsertPeriod(' `
    'Mandate replacement does not end the old period inside the new transaction'
Require $mandate 'MandateProjectionOutboxPersistence.TryDrain' `
    'same-holder declaration retry cannot drain its durable projection outbox'
Require $mandate 'public static int ResumePendingProjections' `
    'pending Mandate projections have no automatic bounded resume entrypoint'
Require $mandate 'TryResumePendingBatch' `
    'automatic Mandate recovery does not use the stable bounded batch'
Require $mandate 'MandateProjectionResumeRules.ResolveDisposition' `
    'automatic Mandate recovery cannot distinguish current and stale work'
Require $mandate 'ResolveRuntimeActorId' `
    'current Mandate recovery remains bound to the dead declaration ruler'
Require $mandate 'if (pKing?.data == null) return false;' `
    'current Mandate runtime work completes without an installed king'
Require $mandate 'MandateProjectionResumeRules.ShouldPublishEffect' `
    'stale Mandate recovery can mutate a newer active period'
Require $mandate 'AW3MultiplayerReplicaScope.IsReplicaSession' `
    'replica sessions can mutate the Mandate projection outbox'
RequireMethodGate $mandate 'private static bool TryDeclareMandateCore' `
    'private static void PublishDeclaredMandate' 'ReadReport()' `
    'fresh Mandate declaration reads or mutates state before its authority gate'
RequireMethodGate $mandate `
    'public static bool TryForceGrantMandateForZhuluAge' `
    'private static bool TryDeclareMandateCore' 'IsMandateKingdom' `
    'forced Mandate grant reads cached state before its authority gate'
RequireMethodGate $mandate 'public static bool TryGrantMandateByPlayer' `
    'public static bool CanDeclareMandate' 'ZhuluWarService' `
    'player Mandate grant reads world state before its authority gate'
RequireMethodGate $mandate 'public static bool OnRulerSucceeded' `
    'private static void CommitRulerProjection' 'ReadReport()' `
    'Mandate succession reads or mutates state before its authority gate'
RequireMethodGate $mandate 'public static void ClearMandate' `
    'public static void CollapseMandate' 'ReadReport()' `
    'Mandate clear reads or mutates state before its authority gate'
RequireMethodGate $mandate 'private static void ChangeMandate' `
    'private static MandateReport ReadReportFromDb' 'ReadReport()' `
    'direct Mandate decline reads or mutates state before its authority gate'
RequireMethodGate $mandate 'private static void UpsertState' `
    'private static void PublishRuntimeMarkerProjection' 'DB.CheckKeyExist' `
    'direct Mandate state update writes before its authority gate'
RequireMethodGate $mandate 'public static void OnKingdomYear' `
    'public static bool TryDeclareMandate' 'TryResumePendingProjectionYear' `
    'replica annual Mandate work starts before its authority gate'
RequireMethodGate $mandate 'public static bool ApplySacrificeOutcome' `
    'public static bool HasMandateProtection' 'ReadReport()' `
    'replica sacrifice outcome reads or mutates before its authority gate'
RequireMethodGate $mandate 'public static void OnKingdomCoreCreated' `
    'public static float GetCoreControlRatioFor' 'ReadReport()' `
    'replica legal-core callback reads or mutates before its authority gate'
RequireMethodGate $mandate 'public static void CollapseMandate' `
    'public static void OnWarStarted' 'MandatePhaseService.ForceChaos' `
    'replica collapse publishes history before its authority gate'
RequireMethodGate $mandate 'public static void OnWarStarted' `
    'public static void OnWarEnded' 'MandateBorderDefenseService' `
    'replica war start mutates Mandate services before its authority gate'
RequireMethodGate $mandate 'public static void OnWarEnded' `
    'public static void OnKingdomDestroyed' 'GetWarType' `
    'replica war end processing starts before its authority gate'
RequireMethodGate $mandate 'public static void OnKingdomDestroyed' `
    'private static void TryDeclareMandateAfterVictory' 'ReadReport()' `
    'replica destruction processing reads or mutates before its authority gate'
RequireMethodGate $mandate `
    'public static void NormalizeMapMarkerAfterRebelSettlement' `
    'public static ColorAsset GetDynastyMapColor' 'pKingdom.data.get' `
    'replica marker normalization mutates before its authority gate'
RequireMethodGate $mandate 'public static void RecordMandateEvent' `
    'public static void MarkDirty' 'RecordEvent' `
    'replica public Mandate event write starts before its authority gate'
Require $restore 'new AW3RestoreStage("mandate_projection_resume"' `
    'load restore does not automatically resume pending Mandate projections'
RequireOrder $restore 'new AW3RestoreStage("mandate_projection"' `
    'new AW3RestoreStage("mandate_projection_resume"' `
    'load restore resumes Mandate work before marker projection is rebuilt'
Require $mandate '_lastProjectionResumeYear = int.MinValue' `
    'annual Mandate projection resume has no shared world-wide year gate'
Require $mandate 'TryResumePendingProjectionYear();' `
    'kingdom annual work never lazily resumes pending Mandate projections'
Require $mandate 'MandateProjectionResumeRules.ShouldStartAnnualCycle' `
    'annual Mandate projection retry can run once per kingdom'

$directDrainStart = $mandate.IndexOf(
    'private static bool DrainMandateProjection')
$directDrainEnd = $mandate.IndexOf(
    'private static bool PublishMandateProjectionEffect', $directDrainStart)
if ($directDrainStart -lt 0 -or $directDrainEnd -le $directDrainStart) {
    throw 'direct Mandate outbox drain method cannot be inspected'
}
$directDrain = $mandate.Substring($directDrainStart,
    $directDrainEnd - $directDrainStart)
RequireOrder $directDrain 'CanMutateOutbox' 'TryDrain' `
    'same-holder replica retry mutates outbox state before authority gate'
Require $dynastyWriter `
    'public static DynastyTransitionStatus TryOnKingChanged' `
    'dynasty persistence does not distinguish no-change from failure'
Require $dynastyWriter 'close.ExecuteNonQuery() != 1' `
    'dynasty close reports success without checking the affected row'
Require $chronicleCode 'DynastyTransitionRules.TryResolve(' `
    'Chronicle does not stop accession after a dynasty persistence failure'
Require $reigns 'TryProjectCurrentReignDynasty' `
    'new reigns cannot be attributed to a dynasty created after the reign gate'
Require $reigns 'TryReadCurrentReignDynasty' `
    'dynasty retry cannot observe whether the open reign has converged'
Require $chronicleCode 'ReignRecordWriter.TryProjectCurrentReignDynasty(' `
    'Chronicle leaves a dynasty-changing reign attributed to the old dynasty'
Require $chronicleCode 'ResolveReignProjection(' `
    'Chronicle still decides reign projection from transition status alone'
Require $dynastyWriter 'TryReadCurrentDynastyState' `
    'state-name retry cannot observe the durable active dynasty marker'
Require $chronicleCode 'StateNameRules.ShouldRetryDynasticStateName(' `
    'state-name retry still depends only on transient created status'
RequireOrder $chronicleCode 'StateNameService.ProjectExistingStateName(' `
    'DynastyRecordWriter.UpdateCurrentStateName(' `
    'dynasty marker advances before runtime state-name projection succeeds'
if ($chronicleCode.Contains('ShouldProjectStateNameAsCreatedDynasty(')) {
    throw 'Chronicle still uses transient created status as completion state'
}
if ($dynastyRules.Contains('ShouldProjectCreatedDynasty(')) {
    throw 'status-only dynasty projection rule can strand a partial retry'
}
if ($chronicleCode.Contains('DynastyRecordWriter.OnKingChanged(')) {
    throw 'Chronicle still treats dynasty no-change and failure as the same boolean'
}

$transition = $chronicleCode.LastIndexOf(
    'ReignRecordWriter.TryTransitionReign(')
$dynasty = $chronicleCode.LastIndexOf(
    'DynastyRecordWriter.TryOnKingChanged(')
$currentDynasty = $chronicleCode.LastIndexOf(
    'DynastyRecordWriter.TryReadCurrentDynastyState(')
$readReignDynasty = $chronicleCode.LastIndexOf(
    'ReignRecordWriter.TryReadCurrentReignDynasty(')
$resolveDynasty = $chronicleCode.LastIndexOf(
    'DynastyTransitionRules.ResolveReignProjection(')
$reignDynasty = $chronicleCode.LastIndexOf(
    'ReignRecordWriter.TryProjectCurrentReignDynasty(')
$stateNamePending = $chronicleCode.LastIndexOf(
    'StateNameRules.ShouldRetryDynasticStateName(')
$stateName = $chronicleCode.LastIndexOf(
    'ProjectDynasticStateNameForRuler(')
if ($transition -lt 0 -or $dynasty -le $transition -or
    $currentDynasty -le $dynasty -or
    $readReignDynasty -le $currentDynasty -or
    $resolveDynasty -le $readReignDynasty -or
    $reignDynasty -le $resolveDynasty -or
    $stateNamePending -le $reignDynasty -or
    $stateName -le $stateNamePending) {
    throw 'dynasty or state-name publication occurs before the reign DB gate'
}

if ($reigns.Contains('ProjectMandateContext(')) {
    throw 'legacy unchecked mandate-context projection entrypoint remains callable'
}

$declarationTry = $declarationPersistence.IndexOf('try')
$declarationBegin = $declarationPersistence.IndexOf('pDb.BeginTransaction()')
if ($declarationTry -lt 0 -or $declarationBegin -le $declarationTry) {
    throw 'Mandate declaration begins its transaction outside the protected try block'
}

$declareStart = $mandate.IndexOf('private static bool TryDeclareMandateCore')
$declareEnd = $mandate.IndexOf('private static void PublishDeclaredMandate',
    $declareStart)
if ($declareStart -lt 0 -or $declareEnd -le $declareStart -or
    $mandate.Substring($declareStart, $declareEnd - $declareStart).Contains(
        'ClearMandate(')) {
    throw 'Mandate replacement clears the old holder before the new transaction commits'
}
$declare = $mandate.Substring($declareStart, $declareEnd - $declareStart)
RequireOrder $declare 'CaptureLegalCoreSnapshots(' `
    'MandateDeclarationPersistence.TryCommit' `
    'Mandate declaration does not capture legal-core cities before its transaction'
Require $declare 'CoreSnapshotSource = "declaration"' `
    'fresh Mandate declaration does not mark its immutable core snapshot'

$createCoresStart = $mandate.IndexOf('private static bool CreateLegalCores')
$createCoresEnd = $mandate.IndexOf(
    'private static bool EnsurePendingCoreSnapshots',
    $createCoresStart)
if ($createCoresStart -lt 0 -or $createCoresEnd -le $createCoresStart) {
    throw 'Mandate legal-core replay method cannot be inspected'
}
$createCores = $mandate.Substring($createCoresStart,
    $createCoresEnd - $createCoresStart)
if ($createCores.Contains('getCities()')) {
    throw 'Mandate legal-core replay still reads changing live cities'
}
Require $mandate 'TryMigrateLegacyCoreSnapshots(' `
    'current legacy Mandate outbox never receives a stable core snapshot'

Write-Output 'Mandate succession runtime source guard passed.'
