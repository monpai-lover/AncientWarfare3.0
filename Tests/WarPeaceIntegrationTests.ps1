$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot

function Read-Source([string]$relativePath) {
    $path = Join-Path $repo $relativePath
    if (-not [IO.File]::Exists($path)) {
        throw "Missing source file: $relativePath"
    }
    return [IO.File]::ReadAllText($path)
}

$warPatch = Read-Source 'Code/patch/AW_WarPatch.cs'
if (-not $warPatch.Contains('WarScoreService.StartWar(__result)')) {
    throw 'war start must initialize the persisted war-score snapshot'
}
if (-not $warPatch.Contains('WarScoreService.EndWar(pWar, pWinner)')) {
    throw 'war end must finalize the persisted war-score snapshot'
}

$occupationPatch = Read-Source `
    'Code/patch/AW_CityOccupationAccelerationPatch.cs'
if (-not $occupationPatch.Contains(
        'WarScoreService.TryFreezeCityOccupation(')) {
    throw 'city capture completion must freeze occupation for peace settlement'
}
$scoreBridge = Read-Source `
    'Code/core/lineage/WarScoreRuntimeBridge.cs'
$scoreService = Read-Source 'Code/core/lineage/WarScoreService.cs'
if (-not $scoreBridge.Contains('if (facts.MatchesActiveWarGoal)') -or
    -not $scoreBridge.Contains('ClearGoalControlForCity(')) {
    throw 'verified war-goal occupation must feed the goal score component'
}
if ($scoreBridge.Contains('SafeIsTotalWar')) {
    throw 'total war must use the same reversible frozen occupation path'
}
if (-not $scoreBridge.Contains('ClearDepartedParticipantControls(')) {
    throw 'a participant leaving a war must release its frozen controls'
}
if (-not $scoreBridge.Contains('ClearCaptureStateAfterWar(')) {
    throw 'war end must clear vanilla city capture state after score finalization'
}
if (-not $scoreBridge.Contains(
        'ReadAllOccupiedCitiesForWarCleanup(war.data.id)') -or
    $scoreBridge.Contains(
        'ReadOccupiedCitiesForWar(war.data.id, 128)')) {
    throw 'war end cleanup must enumerate every frozen city without truncation'
}
if (-not $scoreBridge.Contains('ScheduleDepartedParticipantCleanup(') -or
    -not $scoreBridge.Contains(
        'DeferredRuntimeWorkService.EnqueueCoalesced(')) {
    throw 'participant departure cleanup must continue in bounded deferred pages'
}
$baselineService = Read-Source `
    'Code/core/lineage/WarParticipantCityBaselineService.cs'
$mobilizationBaselineService = Read-Source `
    'Code/core/lineage/WarParticipantMobilizationBaselineService.cs'
$scorePersistence = Read-Source `
    'Code/core/lineage/WarScorePersistence.cs'
if ($mobilizationBaselineService -notmatch
        'WartimeMilitaryPotentialService\s*\.\s*CountPotentialWarriors\s*\(' -or
    $mobilizationBaselineService -notmatch
        'WarParticipantMobilizationBaselineRules\s*\.\s*PotentialKey\s*\(' -or
    -not $mobilizationBaselineService.Contains('pWar.getAttackers()') -or
    -not $mobilizationBaselineService.Contains('pWar.getDefenders()')) {
    throw 'each war participant must register one full-mobilization baseline'
}
if (([regex]::Matches($scoreBridge,
        'WarParticipantMobilizationBaselineService\s*\.\s*' +
        'RegisterExistingParticipants\s*\(war\)')).Count -lt 2 -or
    -not $scoreBridge.Contains('baselines.Attackers') -or
    -not $scoreBridge.Contains('baselines.Defenders')) {
    throw 'war start and annual calibration must reconcile mobilization baselines'
}
if (-not $warPatch.Contains(
        'WarScoreService.RegisterParticipantMobilization(pWar, pKingdom)')) {
    throw 'a mid-war joiner must register mobilization before it can suffer casualties'
}
if (-not $scoreBridge.Contains(
        'public static void RegisterParticipantMobilization(') -or
    ([regex]::Matches($scoreBridge,
        'RepairMobilizationBaselinesIfMissing\s*\(')).Count -lt 3 -or
    -not $scoreBridge.Contains(
        'snapshot.AttackerMobilizationBaseline <= 0') -or
    -not $scoreBridge.Contains(
        'snapshot.DefenderMobilizationBaseline <= 0')) {
    throw 'legacy active wars must repair zero mobilization before reads and casualties'
}
if (-not $scoreService.Contains(
        'pSnapshot.AttackerMobilizationBaseline') -or
    -not $scoreService.Contains(
        'pSnapshot.DefenderMobilizationBaseline')) {
    throw 'war exhaustion must use the persisted side mobilization baselines'
}
foreach ($column in @('ATTACKER_MOBILIZATION_BASELINE',
        'DEFENDER_MOBILIZATION_BASELINE')) {
    if (-not $scorePersistence.Contains(
            'EnsureColumn(SnapshotTable, "' + $column + '"')) {
        throw "war-score migration must add $column"
    }
}
$chroniclePatch = Read-Source 'Code/patch/AW_ChroniclePatch.cs'
if (-not $chroniclePatch.Contains(
        'WarParticipantCityBaselineService.OnCityOwnerChanged(')) {
    throw 'permanent City.setKingdom completion must notify remaining-territory state'
}
if (-not $baselineService.Contains(
        'WarRemainingTerritoryOrchestration.ApplyPermanentTransfer(') -or
    -not $baselineService.Contains('SnapshotWars(pOldOwner)') -or
    -not $baselineService.Contains('SnapshotWars(pNewOwner)') -or
    $baselineService.Contains('World.world.wars')) {
    throw 'remaining-territory changes must inspect only old/new owner active wars'
}
if (-not $baselineService.Contains('SetRemainingCityCount(') -or
    -not $baselineService.Contains(
        'ScheduleParticipantCityControlRevaluation(')) {
    throw 'permanent ownership changes must persist counts and schedule revaluation'
}
if (-not $scoreBridge.Contains(
        'ApplyToEverySharedActiveWar(') -or
    -not $scoreBridge.Contains('SnapshotWars(pOccupier)') -or
    $scoreBridge.Contains('FindSharedActiveWar(')) {
    throw 'frozen occupation must update every shared active war'
}
if ($scoreBridge -match
        'TryGetFrozenCityControl\(\s*(?:pCity|city)\.id') {
    throw 'runtime frozen-control reads must include an exact war id'
}
if (-not $scoreBridge.Contains(
        'ReadOccupiedCitiesByHomeKingdom(') -or
    -not $scoreBridge.Contains('ProcessRevaluationPage(') -or
    -not $scoreBridge.Contains(
        'DeferredRuntimeWorkService.EnqueueCoalesced(')) {
    throw 'affected home controls must be revalued in bounded deferred pages'
}
if (-not $scoreBridge.Contains(
        'ScheduleActiveParticipantControlRevaluation(war)') -or
    -not $scoreBridge.Contains(
        'foreach (Kingdom kingdom in pWar.getAttackers())') -or
    -not $scoreBridge.Contains(
        'foreach (Kingdom kingdom in pWar.getDefenders())')) {
    throw 'annual calibration must repair active participant occupation baselines'
}
if (-not $chroniclePatch.Contains(
        'ShouldRebaseOwnerChange(')) {
    throw 'load and replica city callbacks must share the tested rebase gate'
}
$indexes = Read-Source 'Code/core/db/LineageArchiveIndexRules.cs'
if (-not $indexes.Contains('idx_WarGoal_war_open_city')) {
    throw 'active city-goal lookup must have a matching compound index'
}
foreach ($indexName in @(
        'idx_DiplomacyProposal_responder_status_created',
        'idx_DiplomacyProposal_pending_due',
        'idx_DiplomacyProposal_processing_due')) {
    if (-not $indexes.Contains('DiplomacyActionIndexRules') -or
        -not (Read-Source `
            'Code/core/db/DiplomacyActionIndexRules.cs').Contains(
                $indexName)) {
        throw "main archive schema must install diplomacy index: $indexName"
    }
}
$recalculateStart = $scoreService.IndexOf(
    'private RawScoreTotals CalculateRawTotals(',
    [StringComparison]::Ordinal)
$recalculateEnd = $scoreService.IndexOf(
    'internal bool TryGetFrozenCityControl(', [StringComparison]::Ordinal)
if ($recalculateStart -lt 0 -or $recalculateEnd -le $recalculateStart -or
    $scoreService.Substring($recalculateStart,
        $recalculateEnd - $recalculateStart).Contains('foreach')) {
    throw 'control score updates must be O(1), not rescan every war control'
}

$proposal = Read-Source `
    'Code/core/lineage/DiplomacyProposalService.cs'
$settlementFactsStart = $proposal.IndexOf(
    'private static WarSettlementAiFacts BuildWarSettlementFacts(')
$settlementFactsEnd = $proposal.IndexOf(
    'private static void EnsureWarSettlementBaseline(',
    $settlementFactsStart)
$settlementFacts = if ($settlementFactsStart -ge 0 -and
    $settlementFactsEnd -gt $settlementFactsStart) {
    $proposal.Substring($settlementFactsStart,
        $settlementFactsEnd - $settlementFactsStart)
} else { '' }
foreach ($token in @('AttackerMobilizationBaseline',
        'DefenderMobilizationBaseline', 'AttackerExhaustion',
        'DefenderExhaustion', 'requesterWarExhaustion:',
        'opponentWarExhaustion:')) {
    if (-not $settlementFacts.Contains($token)) {
        throw "war settlement facts must consume snapshot token: $token"
    }
}
$registerBaselineStart = $proposal.IndexOf(
    'public static void RegisterWarSettlementBaseline(')
$registerBaselineEnd = $proposal.IndexOf(
    'private static bool EvaluateAndRespond(', $registerBaselineStart)
$registerBaseline = if ($registerBaselineStart -ge 0 -and
    $registerBaselineEnd -gt $registerBaselineStart) {
    $proposal.Substring($registerBaselineStart,
        $registerBaselineEnd - $registerBaselineStart)
} else { '' }
if (-not $registerBaseline.Contains(
        'WarParticipantMobilizationBaselineService') -or
    $registerBaseline.Contains('countAttackersWarriors()') -or
    $registerBaseline.Contains('countDefendersWarriors()')) {
    throw 'settlement baselines must use full wartime mobilization potential'
}
$rtsScheduling = Read-Source `
    'Code/core/performance/ArmyRtsSchedulingService.cs'
$courtPeace = Read-Source 'Code/core/court/CourtPeaceService.cs'
if ($courtPeace.Contains('World.world.wars.endWar(') -or
    -not $courtPeace.Contains(
        'DiplomacyProposalService.TryScheduleWarPeace(')) {
    throw 'court peace must schedule a treaty proposal instead of ending the war directly'
}
if (-not $proposal.Contains(
        'public static bool TryScheduleWarPeace(Kingdom pRequester)') -or
    -not $proposal.Contains('ProposalRuntime') -or
    -not $proposal.Contains('GetOrAddRecoveryCursor')) {
    throw 'war peace scheduling must expose one bounded annual public entry'
}
$extinction = Read-Source `
    'Code/core/lineage/DiplomacyExtinctionService.cs'
if (-not $extinction.Contains(
        'DiplomacyProposalService.OnKingdomDestroyed(pKingdom.id)') -or
    $extinction.Contains('DiplomacyProposalService.ClearRuntime()')) {
    throw 'kingdom extinction must clear only the fallen realm proposal state'
}
$destroyStart = $proposal.IndexOf(
    'public static void OnKingdomDestroyed(long pKingdomId)')
$destroyEnd = $proposal.IndexOf(
    'public static void OnKingdomYear(', $destroyStart)
$destroyBlock = if ($destroyStart -ge 0 -and $destroyEnd -gt $destroyStart) {
    $proposal.Substring($destroyStart, $destroyEnd - $destroyStart)
} else { '' }
if (-not $destroyBlock.Contains(
        'ProposalRuntime.RemoveKingdom(pKingdomId)') -or
    $destroyBlock.Contains('.Clear(')) {
    throw 'fallen-realm cleanup must preserve survivor cursors and annual dedupe'
}
$annualStart = $proposal.IndexOf(
    'public static void OnKingdomYear(Kingdom pKingdom)')
$annualEnd = $proposal.IndexOf(
    'private static void CalibrateOwnedWarScores(', $annualStart)
$annualBlock = if ($annualStart -ge 0 -and $annualEnd -gt $annualStart) {
    $proposal.Substring($annualStart, $annualEnd - $annualStart)
} else { '' }
$annualPeaceIndex = $annualBlock.IndexOf('TryScheduleWarPeace(')
$annualCooldownIndex = $annualBlock.IndexOf(
    'GeneralAiProposalCooldownReady(')
if ($annualPeaceIndex -lt 0 -or $annualCooldownIndex -lt 0 -or
    $annualPeaceIndex -gt $annualCooldownIndex) {
    throw 'sync war peace evaluation must run before the ordinary eight-year cooldown'
}
$prepareStart = $proposal.IndexOf(
    'private static bool TryPrepareAnnualProposal(')
$prepareEnd = $proposal.IndexOf(
    'private static bool GeneralAiProposalCooldownReady(', $prepareStart)
$prepareBlock = if ($prepareStart -ge 0 -and $prepareEnd -gt $prepareStart) {
    $proposal.Substring($prepareStart, $prepareEnd - $prepareStart)
} else { '' }
if (-not $prepareBlock.Contains(
        'ProposalRuntime.GetOrRunAnnualPreparation(') -or
    -not $prepareBlock.Contains('RunAnnualDiplomacyMaintenance(')) {
    throw 'sync, async and shadow must share kingdom-year idempotent maintenance'
}
$maintenanceStart = $proposal.IndexOf(
    'private static bool RunAnnualDiplomacyMaintenance(')
$maintenanceEnd = $proposal.IndexOf(
    'private static bool GeneralAiProposalCooldownReady(',
    $maintenanceStart)
$maintenanceBlock = if ($maintenanceStart -ge 0 -and
    $maintenanceEnd -gt $maintenanceStart) {
    $proposal.Substring($maintenanceStart,
        $maintenanceEnd - $maintenanceStart)
} else { '' }
if (-not $maintenanceBlock.Contains(
        'WarRecoveryCursor(pKingdom).Take(1)') -or
    $maintenanceBlock.Contains('WarSettlementCursor(pKingdom).Take(1)')) {
    throw 'settlement recovery must not advance the annual four-war peace cursor'
}
$asyncPrepareStart = $proposal.IndexOf(
    'internal static bool TryPrepareAsyncProposalYear(')
$asyncPrepareEnd = if ($asyncPrepareStart -ge 0) {
    $proposal.IndexOf('internal static bool TryBeginAsyncProposalYear(',
        $asyncPrepareStart)
} else { -1 }
$asyncPrepareBlock = if ($asyncPrepareStart -ge 0 -and
    $asyncPrepareEnd -gt $asyncPrepareStart) {
    $proposal.Substring($asyncPrepareStart,
        $asyncPrepareEnd - $asyncPrepareStart)
} else { '' }
$asyncPeaceIndex = $asyncPrepareBlock.IndexOf('TryScheduleWarPeace(')
$asyncCooldownIndex = $asyncPrepareBlock.IndexOf(
    'GeneralAiProposalCooldownReady(')
if ($asyncPeaceIndex -lt 0 -or $asyncCooldownIndex -lt 0 -or
    $asyncPeaceIndex -gt $asyncCooldownIndex) {
    throw 'async authority must run the war peace selector before ordinary cooldown'
}
$asyncStrategy = Read-Source `
    'Code/core/lineage/AsyncKingdomStrategyService.cs'
$scheduleStart = $asyncStrategy.IndexOf(
    'public static void ScheduleDiplomacy(')
$scheduleEnd = $asyncStrategy.IndexOf(
    'private static void CompleteWar(', $scheduleStart)
$scheduleBlock = if ($scheduleStart -ge 0 -and
    $scheduleEnd -gt $scheduleStart) {
    $asyncStrategy.Substring($scheduleStart,
        $scheduleEnd - $scheduleStart)
} else { '' }
$preflightIndex = $scheduleBlock.IndexOf(
    'TryPrepareAsyncProposalYear(')
$captureIndex = $scheduleBlock.IndexOf('TryCaptureAsyncProposal(')
if ($preflightIndex -lt 0 -or $captureIndex -lt 0 -or
    $preflightIndex -gt $captureIndex) {
    throw 'async diplomacy must run annual maintenance and peace preflight before capture'
}
$selectorStart = $proposal.IndexOf(
    'private static PreparedWarSettlement SelectBoundedWarSettlement(')
$selectorEnd = $proposal.IndexOf(
    'private static BoundedRoundRobinCursor<War> WarSettlementCursor(',
    $selectorStart)
$selectorBlock = if ($selectorStart -ge 0 -and
    $selectorEnd -gt $selectorStart) {
    $proposal.Substring($selectorStart, $selectorEnd - $selectorStart)
} else { '' }
foreach ($needle in @(
        'MaximumWarSettlementAssessments',
        'IsProtectedWar(war)',
        'HasPendingPair(pRequester.id, opponent.id)',
        'WarScoreService.TryGetSnapshot(war, pRequester,',
        'ResolvePositionFromSignedWarScore(snapshot.Score)',
        'SelectBestWarSettlementIndex(candidates)')) {
    if (-not $selectorBlock.Contains($needle)) {
        throw "bounded war peace selector is missing: $needle"
    }
}
if (-not $proposal.Contains(
        'public static bool IsProtectedWar(War pWar)') -or
    $courtPeace.Contains('private static bool IsProtectedWar(')) {
    throw 'court, sync and async scheduling must share one protected-war runtime entry'
}
$protectedIndex = $selectorBlock.IndexOf('IsProtectedWar(war)')
$scoreIndex = $selectorBlock.IndexOf(
    'WarScoreService.TryGetSnapshot(war, pRequester,')
if ($protectedIndex -lt 0 -or $scoreIndex -lt 0 -or
    $protectedIndex -gt $scoreIndex) {
    throw 'protected wars must be filtered before ordinary settlement scoring'
}
if ($selectorBlock -notmatch
        'SelectWarSettlement\(facts,\s*position,' -or
    $selectorBlock -notmatch
        'IsReadyToAcceptPeace\(facts,\s*position\)' -or
    $selectorBlock -notmatch
        'SettlementUrgency\(\s*facts,\s*decision,\s*snapshot\.Score\)') {
    throw 'war peace decision and urgency must consume authoritative position and score'
}
if ($selectorBlock.Contains('ResolvePosition(facts)')) {
    throw 'war peace selector must not infer position from relative military advantage'
}
$evaluationStart = $proposal.IndexOf(
    'private static WarSettlementEvaluation BuildWarSettlementEvaluation(')
$evaluationEnd = $proposal.IndexOf(
    'private static Kingdom GetAnySuzerain(', $evaluationStart)
$evaluationBlock = if ($evaluationStart -ge 0 -and
    $evaluationEnd -gt $evaluationStart) {
    $proposal.Substring($evaluationStart,
        $evaluationEnd - $evaluationStart)
} else { '' }
if (-not $evaluationBlock.Contains(
        'WarScoreService.TryGetSnapshot(pWar, pRequester,') -or
    -not $evaluationBlock.Contains(
        'ResolvePositionFromSignedWarScore(snapshot.Score)') -or
    $evaluationBlock.Contains('ResolvePosition(attackerFacts)')) {
    throw 'settlement availability must use authoritative requester signed war score'
}
if (-not $proposal.Contains('WarScoreService.CalibrateYear(')) {
    throw 'annual diplomacy work must calibrate each war score once per year'
}
if (-not $proposal.Contains(
        'WarPeaceSettlementService.Instance.ProcessReparations(')) {
    throw 'annual diplomacy work must process active war reparations'
}
if (-not $proposal.Contains(
        '.AcceptAndExecuteOrResume(')) {
    throw 'outer diplomacy must use the unified peace accept/resume entry'
}
if (-not $proposal.Contains(
        'WarPeaceSettlementService.Instance.RecoverOneForKingdom(')) {
    throw 'annual kingdom work must trigger bounded indexed peace recovery'
}
if (-not $maintenanceBlock.Contains('WarRecoveryCursor(pKingdom)') -or
    -not $maintenanceBlock.Contains('.Take(1)')) {
    throw 'annual peace recovery must rotate exactly one reserved active war'
}
if (-not $proposal.Contains(
        'WarPeaceSettlementService.Instance.EvaluateAi(')) {
    throw 'AI peace responses must use current war score and selected terms'
}
if ($proposal.Contains('AllowPredictedRejection') -or
    $proposal.Contains('pAllowPredictedRejection')) {
    throw 'AI war peace must never bypass a predicted rejection'
}
$createSelectedStart = $proposal.IndexOf(
    'private static bool TryCreateSelected(')
$createSelectedEnd = $proposal.IndexOf(
    'private static bool TryPrepareDefaultPeaceSettlement(',
    $createSelectedStart)
$createSelectedBlock = if ($createSelectedStart -ge 0 -and
    $createSelectedEnd -gt $createSelectedStart) {
    $proposal.Substring($createSelectedStart,
        $createSelectedEnd - $createSelectedStart)
} else { '' }
$preparedAcceptanceIndex = $createSelectedBlock.IndexOf(
    'WarPeaceSettlementService.Instance.EvaluateAi(')
$proposalInsertIndex = $createSelectedBlock.IndexOf(
    'long proposalId = TableIdAllocator.Next(')
if ($preparedAcceptanceIndex -lt 0 -or $proposalInsertIndex -lt 0 -or
    $preparedAcceptanceIndex -gt $proposalInsertIndex -or
    -not $createSelectedBlock.Contains('SettlementResolve(pResponder)')) {
    throw 'AI war peace must evaluate the persisted terms before queuing its letter'
}
if ($selectorBlock.Contains('ExpectedAccepted(assessment)') -or
    $selectorBlock.Contains('receiverExpectedAccepted')) {
    throw 'war peace candidate selection must defer recipient acceptance to the prepared terms'
}
$rejectionStart = $proposal.IndexOf(
    'private static bool HasRecentAiRejection(')
$rejectionEnd = $proposal.IndexOf(
    'private static bool IsSubject(', $rejectionStart)
$rejectionBlock = if ($rejectionStart -ge 0 -and
    $rejectionEnd -gt $rejectionStart) {
    $proposal.Substring($rejectionStart, $rejectionEnd - $rejectionStart)
} else { '' }
if (-not $rejectionBlock.Contains(
        'IsAiRejectionCooldownActive(') -or
    -not $rejectionBlock.Contains('pType)')) {
    throw 'AI rejection lookup must apply proposal-type-specific cooldowns'
}
if (-not $proposal.Contains('private static bool EvaluateAndRespond(') -or
    -not $proposal.Contains('"no_longer_available"')) {
    throw 'invalid due diplomacy proposals must close or back off instead of retrying every frame'
}
$processOneStart = $proposal.IndexOf(
    'private static bool ProcessOneDueProposal(')
$processOneEnd = if ($processOneStart -ge 0) {
    $proposal.IndexOf('public static void ClearRuntime()', $processOneStart)
} else { -1 }
$processOneBlock = if ($processOneStart -ge 0 -and
    $processOneEnd -gt $processOneStart) {
    $proposal.Substring($processOneStart,
        $processOneEnd - $processOneStart)
} else { '' }
foreach ($needle in @(
        'try',
        'EvaluateAndRespond(pProposalId)',
        'catch (Exception exception)',
        'DeferFailedResponse(pProposalId, pNow)')) {
    if (-not $processOneBlock.Contains($needle)) {
        throw "due response execution must catch and defer failures: $needle"
    }
}
$deferStart = $proposal.IndexOf(
    'private static bool DeferFailedResponse(')
$deferEnd = if ($deferStart -ge 0) {
    $proposal.IndexOf('public static void ClearRuntime()', $deferStart)
} else { -1 }
$deferBlock = if ($deferStart -ge 0 -and $deferEnd -gt $deferStart) {
    $proposal.Substring($deferStart, $deferEnd - $deferStart)
} else { '' }
foreach ($needle in @(
        'RESPONSE_DUE_TIME=@next',
        "RESPONSE_REASON='response_retry'",
        "STATUS='pending'")) {
    if (-not $deferBlock.Contains($needle)) {
        throw "failed response row must receive bounded retry metadata: $needle"
    }
}
if ($deferBlock -notmatch
        'DiplomacyProposalRules\s*\.NextResponseRuntimeTime\(pNow\)') {
    throw 'failed response backoff must use the bounded runtime-time rule'
}
if (-not $processOneBlock.Contains(
        'DiplomacyProposalRules.ShouldRetryFailedResponse(')) {
    throw 'terminal and processing diplomacy rows must not be retried as pending'
}
if (-not $proposal.Contains(
        'ProcessOneDueProposal(incoming.ProposalId, now)') -or
    -not $proposal.Contains(
        'ProcessOneDueProposal(proposalId, now)')) {
    throw 'frame and annual response paths must share exception-safe execution'
}
$dueQueryStart = $proposal.IndexOf(
    'private static long FindDuePendingProposal(double pNow)')
$dueQueryEnd = $proposal.IndexOf(
    'private sealed class PreparedAiProposal',
    $dueQueryStart)
$dueQueryBlock = if ($dueQueryStart -ge 0 -and
    $dueQueryEnd -gt $dueQueryStart) {
    $proposal.Substring($dueQueryStart, $dueQueryEnd - $dueQueryStart)
} else { '' }
if (-not $dueQueryBlock.Contains("STATUS='pending'") -or
    -not $dueQueryBlock.Contains('RESPONSE_DUE_TIME<=@now')) {
    throw 'pending diplomacy proposals must not be evaluated before their reply is due'
}
$respondStart = $proposal.IndexOf(
    'public static bool Respond(long pProposalId, bool pAccept,')
$respondEnd = $proposal.IndexOf(
    'public static bool HasPendingPair(', $respondStart)
$respondBlock = if ($respondStart -ge 0 -and
    $respondEnd -gt $respondStart) {
    $proposal.Substring($respondStart, $respondEnd - $respondStart)
} else { '' }
$rejectStart = $respondBlock.IndexOf('if (!pAccept)')
$rejectEnd = $respondBlock.IndexOf('Kingdom requester =', $rejectStart)
$rejectBlock = if ($rejectStart -ge 0 -and $rejectEnd -gt $rejectStart) {
    $respondBlock.Substring($rejectStart, $rejectEnd - $rejectStart)
} else { '' }
if (-not $rejectBlock.Contains(
        'WarPeaceSettlementService.Instance.Respond(') -or
    -not $rejectBlock.Contains('accept: false') -or
    $rejectBlock.Contains('WarPeaceSettlementService.Instance.Cancel(')) {
    throw 'a rejected peace letter must reject only its pending inner settlement'
}
$failedExecutionStart = $proposal.IndexOf('if (!Execute(proposal,')
$failedExecutionEnd = $proposal.IndexOf('bool accepted = CloseReserved(',
    $failedExecutionStart)
$failedExecutionBlock = if ($failedExecutionStart -ge 0 -and
    $failedExecutionEnd -ge 0) {
    $proposal.Substring($failedExecutionStart,
        $failedExecutionEnd - $failedExecutionStart)
} else { '' }
$cancelIndex = $failedExecutionBlock.IndexOf(
    'WarPeaceSettlementService.Instance.Cancel(')
$closeIndex = $failedExecutionBlock.IndexOf('CloseReserved(proposal,')
if ($cancelIndex -lt 0 -or $closeIndex -lt 0 -or
    $cancelIndex -gt $closeIndex -or
    -not $failedExecutionBlock.Contains(
        'WarPeaceSettlementStatus.Executing') -or
    -not $failedExecutionBlock.Contains('_nextProcessingPollTime')) {
    throw 'failed outer diplomacy execution must cancel an accepted inner peace settlement'
}
$recoveryStart = $proposal.IndexOf(
    'private static bool RecoverProcessingProposal(')
$recoveryEnd = $proposal.IndexOf(
    'private static bool EffectAlreadyApplied(', $recoveryStart)
$recoveryBlock = if ($recoveryStart -ge 0 -and $recoveryEnd -ge 0) {
    $proposal.Substring($recoveryStart, $recoveryEnd - $recoveryStart)
} else { '' }
if (-not $recoveryBlock.Contains(
        'WarPeaceSettlementService.Instance.Cancel(') -or
    -not $recoveryBlock.Contains(
        'WarPeaceSettlementStatus.Executing')) {
    throw 'bounded diplomacy recovery must preserve an executing inner settlement'
}
if (-not $recoveryBlock.Contains(
        '!DiplomacyProposalRules.IsPeaceProposal(proposal.Type)')) {
    throw 'peace recovery must not treat a missing war as completed before term recovery'
}
$legacyBlock = @'
                        World.world.wars.endWar(peaceWar, peaceWinner);
'@
if ($proposal.Contains($legacyBlock)) {
    throw 'peace proposals must not bypass settlement terms with raw endWar'
}

$territory = Read-Source `
    'Code/core/lineage/WarTerritoryService.cs'
if (-not $territory.Contains('TryHasExecutedCoalitionSettlement(') -or
    -not $territory.Contains('ResolveNegotiatedGoalRecord(')) {
    throw 'legacy war-goal resolution must not apply settlement terms twice'
}
if (-not $territory.Contains(
        'WarPeaceSettlementService.Instance.HasActionableSettlement(')) {
    throw 'city-goal resolution must not re-enter while settlement terms are applying'
}
if (-not $territory.Contains('TryReadExecutedCoalitionTerms(') -or
    -not $territory.Contains('NegotiatedGoalMatchesExecutedTerm(')) {
    throw 'negotiated war goals must be resolved from actual executed terms'
}

$conversation = Read-Source `
    'Code/ui/windows/DiplomacyConversationWindow.cs'
if (-not $conversation.Contains('WarPeaceNegotiationController.Open(')) {
    throw 'peace, surrender and enforce actions must open negotiation UI'
}
$conversationService = Read-Source `
    'Code/core/lineage/DiplomacyConversationService.cs'
if (-not $conversationService.Contains(
        'WarPeaceSettlementService.Instance.ReadTerms(')) {
    throw 'peace proposal letters must list their persisted terms'
}
if (-not $conversationService.Contains(
        'DiplomacyConversationRules.IsAutomaticWarSettlementTruce(') -or
    -not $conversationService.Contains(
        'WarPeaceSettlementService.Instance.ReadExecutedTerms(') -or
    -not $conversationService.Contains('pProposal.TreatyUntilYear')) {
    throw 'automatic post-war truce bubbles must show the treaty duration and executed settlement terms'
}
if (-not $conversationService.Contains(
        '!DiplomacyConversationRules.IsAutomaticWarSettlementTruce(')) {
    throw 'automatic post-war truce records must not create a fake diplomatic reply bubble'
}

$commandModels = Read-Source `
    'Code/api/multiplayer/AW3MultiplayerCatalogModels.cs'
$commandHandler = Read-Source `
    'Code/core/multiplayer/commands/AW3DiplomacyCommandHandler.cs'
if (-not $commandModels.Contains('CreateWarPeaceProposal(') -or
    -not $commandModels.Contains('public string Payload') -or
    -not $commandHandler.Contains('WarPeaceSettlementDraft') -or
    -not $commandHandler.Contains('WarPeaceDraftCodec.TryDeserialize')) {
    throw 'war-peace drafts must travel through the authoritative command path'
}

$settlementStore = Read-Source `
    'Code/core/lineage/WarPeaceSettlementStore.cs'
if (-not $settlementStore.Contains('REQUESTER_KINGDOM_ID=@kingdom') -or
    -not $settlementStore.Contains('RESPONDER_KINGDOM_ID=@kingdom') -or
    -not $settlementStore.Contains('LIMIT @limit') -or
    -not $settlementStore.Contains('AND STATUS IN ') -or
    -not $settlementStore.Contains(
        "('executing','terms_applied')") -or
    -not $settlementStore.Contains(
        "WHERE WAR_ID=@war AND SCOPE_KIND='coalition' AND ") -or
    -not $settlementStore.Contains("STATUS='executed' ")) {
    throw 'peace recovery candidates must use a bounded kingdom index query'
}
$settlementProposalTable = Read-Source `
    'Code/core/db/WarPeaceSettlementProposalTableItem.cs'
if (-not $settlementProposalTable.Contains('recovery_attempts')) {
    throw 'peace recovery attempt ordering must persist on the proposal row'
}
if (-not $settlementStore.Contains('TryMarkRecoveryAttempt(') -or
    -not $settlementStore.Contains(
        'SET RECOVERY_ATTEMPTS=CASE WHEN RECOVERY_ATTEMPTS<') -or
    -not $settlementStore.Contains('ORDER BY RECOVERY_ATTEMPTS ASC,')) {
    throw 'peace recovery must atomically age attempted rows before paging'
}
if (-not $indexes.Contains(
        'REQUESTER_KINGDOM_ID, RECOVERY_ATTEMPTS, STATUS, PROPOSAL_ID') -or
    -not $indexes.Contains(
        'RESPONDER_KINGDOM_ID, RECOVERY_ATTEMPTS, STATUS, PROPOSAL_ID')) {
    throw 'peace recovery attempt ordering must have requester/responder indexes'
}
$settlementRuntime = Read-Source `
    'Code/core/lineage/WarPeaceSettlementRuntime.cs'
$acceptanceRuntime = Read-Source `
    'Code/core/lineage/WarPeaceSettlementAcceptanceRuntime.cs'
$settlementModels = Read-Source `
    'Code/core/lineage/WarPeaceSettlementModels.cs'
$outcomeRules = Read-Source `
    'Code/core/lineage/WarPeaceSettlementOutcomeRules.cs'
$settlementService = Read-Source `
    'Code/core/lineage/WarPeaceSettlementService.cs'
if (-not $proposal.Contains(
        'proposal.Type == DiplomacyProposalType.Surrender')) {
    throw 'peace response must identify the explicit surrender path'
}
if (-not $acceptanceRuntime.Contains(
        'WarPeaceDefaultOfferRules.IsCompleteSurrenderOffer(') -or
    -not $acceptanceRuntime.Contains('BuildDefaultDraft(') -or
    -not $acceptanceRuntime.Contains(
        'WarPeaceSettlementValidationRules.TryMaterialize(')) {
    throw 'surrender acceptance must compare against a live canonical full bundle'
}
if (-not $acceptanceRuntime.Contains('TryResolveRecipientSide(') -or
    -not $acceptanceRuntime.Contains('maximumDraft.Participants.Add(')) {
    throw 'AI acceptance must classify allied recipients and preserve coalition sides'
}
if (-not $settlementModels.Contains('TryResolveRecipientSide(') -or
    -not $outcomeRules.Contains(
        'IReadOnlyList<WarPeaceSettlementParticipantSnapshot>')) {
    throw 'persisted demand value and treaty outcome must use participant sides'
}
if ($settlementRuntime -notmatch
    '(?s)_proposal\.Terms,\s*_proposal\.Participants') {
    throw 'final treaty winner must receive authoritative participant sides'
}
if (-not $settlementRuntime.Contains(
        'TryResolveFrozenOccupationRecipient(') -or
    -not $settlementRuntime.Contains(
        'CanExecuteFrozenOccupationCede(') -or
    -not $settlementRuntime.Contains(
        'ownerId == fromId,') -or
    -not $settlementRuntime.Contains(
        'ownerId == toId,') -or
    -not $settlementRuntime.Contains(
        'ownerId == controllerId, basis')) {
    throw 'initial cede validation must authorize the controller-side recipient and recorded controller'
}
if (-not $settlementRuntime.Contains(
        'facts.SourceKingdomCityCount = CountLiveCities(from)')) {
    throw 'peace materialization must capture the live source city count'
}
$liveValidateStart = $settlementRuntime.IndexOf(
    'public bool TryValidate(WarPeaceSettlementProposal proposal,')
$liveValidateEnd = $settlementRuntime.IndexOf(
    'public IWarPeaceSettlementExecution BeginExecution(',
    $liveValidateStart)
$liveValidateBlock = if ($liveValidateStart -ge 0 -and
    $liveValidateEnd -gt $liveValidateStart) {
    $settlementRuntime.Substring($liveValidateStart,
        $liveValidateEnd - $liveValidateStart)
} else { '' }
if (-not $liveValidateBlock.Contains(
        'WarPeaceTreatySurvivalLedger') -or
    -not $liveValidateBlock.Contains('survival.Validate(out reason)')) {
    throw 'accepted peace must revalidate full-annexation survival conflicts'
}
if (-not $settlementRuntime.Contains(
        'TryResolveFrozenOccupationRecipient(_proposal.WarId,') -or
    -not $settlementRuntime.Contains(
        'ownerId == controllerId, basis')) {
    throw 'city cession must recheck the frozen controller immediately before transfer'
}
if (-not $settlementRuntime.Contains(
        'HasExecutionTerritorialBasis(term.FrozenOccupation,') -or
    -not $settlementRuntime.Contains('term.CoreOrClaimBasis,')) {
    throw 'executing city cessions must retain accepted territorial basis after war cleanup'
}
$inspectStart = $settlementRuntime.IndexOf(
    'public WarPeaceTermApplicationState InspectTermApplication(')
$inspectEnd = $settlementRuntime.IndexOf(
    'public WarPeaceTermApplicationState InspectResourceEndpoint(',
    $inspectStart)
$inspectBlock = if ($inspectStart -ge 0 -and
    $inspectEnd -gt $inspectStart) {
    $settlementRuntime.Substring($inspectStart,
        $inspectEnd - $inspectStart)
} else { '' }
if (-not $inspectBlock.Contains(
        'TryResolveFrozenOccupationRecipient(proposal.WarId,') -or
    -not $inspectBlock.Contains(
        'city.kingdom?.id == controllerId')) {
    throw 'recovery inspection must recognize the recorded allied controller as pending'
}
if (-not $settlementService.Contains('TryRecoverPendingCede(')) {
    throw 'executing pending cede must recognize an already transferred city'
}
if ($settlementRuntime.Contains('if (original == recipient) return true;')) {
    throw 'TryCedeCity must not retain its unreachable recipient-owner branch'
}

$coalitionTasks = Read-Source `
    'Code/core/lineage/CoalitionWarTaskService.cs'
$militaryFacts = Read-Source `
    'Code/core/lineage/WarMilitaryFactsService.cs'
$armyController = Read-Source `
    'Code/core/lineage/ArmyRtsControllerService.cs'
$authorityCycle = Read-Source `
    'Code/core/performance/AWAuthorityCycleService.cs'
$restorePipeline = Read-Source `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs'
foreach ($source in @($coalitionTasks, $militaryFacts)) {
    if ($source.Contains('.endWar(') -or
        $source.Contains('WarPeaceSettlementService')) {
        throw 'military facts and coalition tasks cannot close wars or execute treaties'
    }
}
foreach ($needle in @(
        'CoalitionWarTaskService.OnWarStarted(__result);',
        'CoalitionWarTaskService.OnWarEnded(pWar);',
        'CoalitionWarTaskService.OnWarParticipantChanged(')) {
    if (-not $warPatch.Contains($needle)) {
        throw "coalition task lifecycle is missing war hook: $needle"
    }
}
if (-not $armyController.Contains(
        'CoalitionWarTaskService.OnArmyInvalidated(pArmyId);')) {
    throw 'Army invalidation must release its coalition task claim'
}
if ($coalitionTasks -notmatch
        'CoalitionWarTaskRules\s*\.\s*MaximumTasksPerWar' -or
    $coalitionTasks -notmatch
        'CoalitionWarTaskRules\s*\.\s*MaximumTargetsInspectedPerSide') {
    throw 'coalition task publication must enforce bounded war and target work'
}
if (-not $coalitionTasks.Contains('ArmyFieldIndexService') -or
    -not $coalitionTasks.Contains('.getKingdom()')) {
    throw 'coalition claims must resolve indexed Armies and verify native ownership'
}
foreach ($needle in @(
        'WarIdsByTarget.Remove(pCity.id);',
        'TargetInvalidationQueue.Enqueue(new TargetInvalidationWork(',
        'Ledger.ReleaseTarget(warId, work.TargetCityId)')) {
    if (-not $coalitionTasks.Contains($needle)) {
        throw "target invalidation must drain only detached reverse-indexed wars: $needle"
    }
}
if ($coalitionTasks.Contains('Ledger.ReleaseTarget(pCity.id)')) {
    throw 'target invalidation cannot release an unscoped city across all wars'
}
$participantChangeStart = $coalitionTasks.IndexOf(
    'public static void OnWarParticipantChanged(')
$participantChangeEnd = $coalitionTasks.IndexOf(
    'public static void OnArmyInvalidated(', $participantChangeStart)
$participantChange = if ($participantChangeStart -ge 0 -and
    $participantChangeEnd -gt $participantChangeStart) {
    $coalitionTasks.Substring($participantChangeStart,
        $participantChangeEnd - $participantChangeStart)
} else { '' }
if (-not $participantChange.Contains('RequestRefresh(pWar.data.id)') -or
    $participantChange.Contains('RefreshWar(')) {
    throw 'coalition participant changes must restart the resumable refresh instead of rebuilding synchronously'
}
if (-not $proposal.Contains('WarMilitaryFactsService.Build(') -or
    -not $proposal.Contains('requesterMilitary.AvailableFieldArmies') -or
    -not $proposal.Contains('opponentMilitary.AvailableFieldArmies')) {
    throw 'war settlement facts must consume both sides military projections'
}
if ($militaryFacts -notmatch
        'ArmyFieldIndexService\s*\.\s*CreateSnapshotCursor\(' -or
    $militaryFacts -notmatch
        'ArmyEstablishmentRules\s*\.\s*MaximumFieldArmies') {
    throw 'military facts must inspect only the bounded field-Army index'
}
if (-not $rtsScheduling.Contains(
        'CoalitionWarTaskService.ProcessFrame') -or
    -not $authorityCycle.Contains(
        'ArmyRtsSchedulingService.ProcessAw3Authority(')) {
    throw 'coalition task expiry must run through the authoritative RTS scheduling stage'
}
if (-not $restorePipeline.Contains(
        'CoalitionWarTaskService.RebuildRuntime')) {
    throw 'load restore must rebuild active coalition tasks'
}

Write-Output 'War peace integration tests passed.'
