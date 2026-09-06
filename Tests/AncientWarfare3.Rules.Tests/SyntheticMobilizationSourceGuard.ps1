$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Read-Source([string] $relativePath) {
    return Get-Content -Raw -Encoding UTF8 (Join-Path $root $relativePath)
}

function Require-Text([string] $text, [string] $needle,
    [string] $message) {
    if (-not $text.Contains($needle)) { throw $message }
}

function Reject-Text([string] $text, [string] $needle,
    [string] $message) {
    if ($text.Contains($needle)) { throw $message }
}

function Section([string] $text, [string] $start, [string] $end) {
    $startIndex = $text.IndexOf($start, [StringComparison]::Ordinal)
    if ($startIndex -lt 0) { return '' }
    $endIndex = $text.IndexOf($end, $startIndex + $start.Length,
        [StringComparison]::Ordinal)
    if ($endIndex -lt 0) { return $text.Substring($startIndex) }
    return $text.Substring($startIndex, $endIndex - $startIndex)
}

$ledger = Read-Source 'Code/core/lineage/SyntheticMobilizationLedgerService.cs'
$reserve = Read-Source 'Code/core/lineage/CityReservePoolService.cs'
$temporary = Read-Source 'Code/core/lineage/TemporaryLevyService.cs'
$standing = Read-Source 'Code/patch/AW_StandingArmyPatch.cs'
$scheduler = Read-Source 'Code/core/performance/ArmyRtsSchedulingService.cs'
$runner = Read-Source 'Code/core/performance/AWCooperativeSimulationRunner.cs'
$director = Read-Source 'Code/core/lineage/KingdomWarDirectorService.cs'
$cityPatch = Read-Source 'Code/patch/AW_CityReservePoolPatch.cs'
$syntheticLevy = Read-Source 'Code/core/lineage/SyntheticLevyService.cs'
$reproduction = Read-Source 'Code/patch/AW_DynasticReproductionPatch.cs'
$birth = Read-Source 'Code/patch/AW_BirthPatch.cs'
$babyName = Read-Source 'Code/patch/AW_BabyNamePatch.cs'
$civilianBirth = Read-Source 'Code/patch/AW_CivilianInstantBirthPatch.cs'
$pregnancy = Read-Source 'Code/patch/AW_NobleHeirPregnancyPatch.cs'
$figure = Read-Source 'Code/patch/AW_FigurePatch.cs'
$lovers = Read-Source 'Code/patch/AW_LoversPatch.cs'
$death = Read-Source 'Code/patch/AW_ActorDeathPatch.cs'
$familyExpansion = Read-Source 'Code/core/lineage/FamilyExpansionService.cs'
$dynasticReproduction = Read-Source 'Code/core/lineage/DynasticReproductionService.cs'
$restorePipeline = Read-Source `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs'

Require-Text $ledger 'city.getPopulationPeople()' `
    'Synthetic quota must use the city population snapshot.'
Require-Text $ledger 'TryReserveReplacement' `
    'Initial mobilization and replacements must share the city-war ledger.'
Require-Text $ledger 'LiveSyntheticForCity' `
    'Compatibility estimates must read synthetic counts in O(1).'
Require-Text $ledger 'LoadActorReconciliationBatchLimit' `
    'Load reconciliation must process synthetic actors in bounded slices.'
Require-Text $ledger 'LoadRecordReconciliationBatchLimit' `
    'Load reconciliation must process ledger records in bounded slices.'
Require-Text $ledger 'LoadReconciliationPhase.ResetRecords' `
    'Record reset must finish before actor membership is rebuilt.'
Require-Text $ledger 'FinishLoadActorReconciliation();' `
    'Every load-reconciliation completion path must clear all runtime cursors.'
Require-Text $ledger 'SyntheticLevyService.ReconcileLoadedActor(' `
    'Load reconciliation must rebuild actor membership from persisted metadata.'
Require-Text $ledger 'OrphanSyntheticActorIds' `
    'Unrecoverable synthetic actors need bounded post-scan cleanup.'
Reject-Text $syntheticLevy 'ClearParentage(actor);' `
    'Synthetic soldiers must not overwrite vanilla parent fields.'
Reject-Text $syntheticLevy 'actor.data.parent_id_1 = -1L;' `
    'Synthetic parent slot one belongs to vanilla.'
Reject-Text $syntheticLevy 'actor.data.parent_id_2 = -1L;' `
    'Synthetic parent slot two belongs to vanilla.'
$loadReconcile = Section $syntheticLevy `
    'internal static void ReconcileLoadedActor(Actor actor)' `
    'internal static void PromoteToPermanentCommand(Actor actor)'
Reject-Text $loadReconcile 'actor.lover' `
    'Loaded synthetic soldiers must not rewrite vanilla lover state.'
Reject-Text $loadReconcile 'finishStatusEffect("pregnant")' `
    'Loaded synthetic soldiers must not rewrite vanilla pregnancy state.'
Require-Text $reproduction 'SyntheticLevyService.IsSynthetic(pParent1)' `
    'Synthetic parents must be detected before custom reproduction changes.'
Require-Text $reproduction 'return false;' `
    'Synthetic two-parent reproduction must be rejected at the vanilla entry point.'
Require-Text $reproduction 'pParent2 == null' `
    'Single-parent miracle paths must retain the vanilla return contract.'
Require-Text $birth 'SyntheticLevyService.IsSynthetic(pParent1)' `
    'Birth history hooks must skip synthetic parents.'
Require-Text $babyName 'SyntheticLevyService.IsSynthetic(pParent1)' `
    'Baby naming hooks must skip synthetic parents.'
Require-Text $civilianBirth 'SyntheticLevyService.IsSynthetic(mother)) return false;' `
    'Synthetic civilian pregnancy must be rejected before vanilla pregnancy.'
Require-Text $pregnancy 'SyntheticLevyService.IsSynthetic(pActor)) return false;' `
    'Synthetic pregnancy delivery must be rejected before vanilla delivery.'
Require-Text $figure 'SyntheticLevyService.IsSynthetic(__result)' `
    'Historical figure birth hooks must skip synthetic actors.'
Require-Text $lovers 'SyntheticLevyService.IsSynthetic(__instance)' `
    'Synthetic lover hooks must not write custom relationship data.'
Require-Text $lovers 'BecomeLoversWith_Prefix' `
    'Synthetic lover formation must be blocked before vanilla execution.'
Require-Text $death 'if (!SyntheticLevyService.IsSynthetic(__instance))' `
    'Synthetic deaths must not update reproduction parent indexes.'
Require-Text $babyName 'SyntheticLevyService.IsSynthetic(__result)' `
    'Synthetic creation must not receive custom birth affiliation data.'
Require-Text $familyExpansion 'SyntheticLevyService.IsSynthetic(pActor)' `
    'Synthetic actors must bypass custom family expansion weighting.'
Require-Text $dynasticReproduction 'SyntheticLevyService.IsSynthetic(pActor)' `
    'Synthetic actors must bypass custom dynastic reproduction rules.'
Require-Text $ledger 'DemobilizationBatchLimit' `
    'Post-war generated-actor removal must remain bounded.'
Require-Text $ledger 'OnCityKingdomChanged(City pCity,' `
    'City transfer must stop future city mobilization.'
Require-Text $cityPatch `
    'SyntheticMobilizationLedgerService.OnCityKingdomChanged(' `
    'City transfer must notify the unified mobilization ledger.'
$cityTransfer = Section $ledger 'OnCityKingdomChanged(City pCity,' `
    'internal static void ProcessAuthorityCycle()'
Reject-Text $cityTransfer `
    'record.Phase = SyntheticMobilizationPhase.Demobilizing;' `
    'City transfer alone cannot demobilize an active participant.'
Reject-Text $cityTransfer 'foreach (' `
    'City transfer events must enqueue indexed bounded work.'
Reject-Text $cityTransfer 'Records' `
    'City transfer callbacks cannot inspect ledger records directly.'
Require-Text $cityTransfer 'EnqueueCityRecordWork(pCity.id);' `
    'City transfer callbacks must only enqueue indexed record work.'
$warStart = Section $ledger 'internal static void OnWarStarted(War pWar)' `
    'internal static void OnKingdomJoinedWar('
Reject-Text $warStart 'getAttackers()' `
    'War-start events must not synchronously enumerate attackers.'
Reject-Text $warStart 'getDefenders()' `
    'War-start events must not synchronously enumerate defenders.'
$warJoin = Section $ledger 'internal static void OnKingdomJoinedWar(' `
    'internal static void OnKingdomLeftWar('
Reject-Text $warJoin 'EnqueueKingdom(' `
    'Participant joins must enqueue a bounded city-cursor job.'
Require-Text $warJoin 'EnqueueParticipant(' `
    'Participant joins must persist a bounded city cursor.'
$markDemobilizing = Section $ledger `
    'private static void MarkDemobilizing(long pWarId,' `
    'private static void EnqueueCityRecordWork('
Reject-Text $markDemobilizing 'foreach (' `
    'War demobilization events must enqueue indexed bounded work.'
Reject-Text $markDemobilizing 'Records' `
    'War demobilization callbacks cannot inspect ledger records directly.'
Require-Text $ledger 'RecordKeysByWar' `
    'War lifecycle work requires a maintained war-record index.'
Require-Text $ledger 'RecordKeysByCity' `
    'City transfer work requires a maintained city-record index.'
Require-Text $ledger 'ProcessPendingParticipantWork();' `
    'Authority cycles must advance bounded participant city cursors.'
Require-Text $ledger 'ProcessWarEnrollmentScan();' `
    'Load and topology recovery must re-enroll active wars in bounded slices.'
Require-Text $ledger 'RequestWarEnrollmentScan();' `
    'City membership changes must request bounded active-war re-enrollment.'
Require-Text $ledger 'ExpectedCityCount' `
    'Participant cursors must detect mutable city-list generations.'
Require-Text $ledger 'RestartRequested' `
    'Repeated enrollment requests must restart only after current progress.'
Require-Text $ledger 'ProcessPendingWarRecordWork();' `
    'Authority cycles must advance bounded war-record batches.'
Require-Text $ledger 'ProcessPendingCityRecordWork();' `
    'Authority cycles must advance bounded city-record batches.'
Require-Text $ledger 'EndExclusive' `
    'Lifecycle jobs must snapshot their event-time record boundary.'
Require-Text $ledger 'LifecycleRecordBatchLimit' `
    'Lifecycle record work must use a fixed small batch limit.'
Require-Text $ledger 'PendingParticipantWorkKeys.Clear();' `
    'Runtime reset must clear participant cursor deduplication state.'
Require-Text $ledger 'PendingWarRecordWorkKeys.Clear();' `
    'Runtime reset must clear war lifecycle deduplication state.'
Require-Text $ledger 'PendingCityRecordWorkKeys.Clear();' `
    'Runtime reset must clear city lifecycle deduplication state.'
Require-Text $ledger 'RecordKeysByWar.Clear();' `
    'Runtime reset must clear the war-record index.'
Require-Text $ledger 'RecordKeysByCity.Clear();' `
    'Runtime reset must clear the city-record index.'
Reject-Text $ledger 'Records[key] =' `
    'All record creation paths must use the shared indexed store.'
Reject-Text $restorePipeline `
    'throw new InvalidOperationException(snapshotError)' `
    'A damaged mobilization sidecar must fall back to actor reconciliation.'
Reject-Text $reserve 'TryTakeNextActorId(pool.ActorIds' `
    'Real resident IDs cannot feed AW3 temporary mobilization.'
Reject-Text $reserve 'WarReserveCapacity' `
    'CityReservePoolService cannot remain a parallel wartime ledger.'
Reject-Text $reserve 'TryReserveWarManpower(' `
    'Replacement ownership belongs only to the synthetic ledger.'
Reject-Text $temporary 'SyntheticLevyService.Promote(' `
    'Generated wartime actors cannot become permanent residents.'
Reject-Text $temporary 'ResumeActiveRecruitmentPlans();' `
    'Legacy real-actor recruitment plans cannot resume after load.'
Reject-Text $standing 'TryToMakeWarrior_Prefix' `
    'AW3 cannot globally suppress vanilla recruitment.'
Require-Text $scheduler 'SharedGate' `
    'Native and AW3 RTS entries must share one exact-once gate.'
Require-Text $runner 'Aw3RtsLogicalPulse' `
    'Every large-step internal pass must expose an RTS pulse.'
Require-Text $director 'TryAssignFirstOrderMission(' `
    'War participants must receive bounded first-order missions.'

Write-Host 'Synthetic mobilization source guard passed.'
