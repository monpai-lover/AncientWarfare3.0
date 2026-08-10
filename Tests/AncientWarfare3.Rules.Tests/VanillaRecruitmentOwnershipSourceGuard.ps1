$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Read-Source([string] $relativePath) {
    return [IO.File]::ReadAllText((Join-Path $repo $relativePath))
}

$authority = Read-Source 'Code/core/performance/AWAuthorityCycleService.cs'
$annual = Read-Source 'Code/core/policy/KingdomAnnualWorkService.cs'
$warPatch = Read-Source 'Code/patch/AW_WarPatch.cs'
$warNotice = Read-Source 'Code/core/lineage/WarNoticeService.cs'
$reserve = Read-Source 'Code/core/lineage/CityReservePoolService.cs'
$replenishment = Read-Source `
    'Code/core/lineage/ArmyReplenishmentOperationService.cs'
$synthetic = Read-Source 'Code/core/lineage/SyntheticLevyService.cs'
$actorDeath = Read-Source 'Code/patch/AW_ActorDeathPatch.cs'
$armySafety = Read-Source 'Code/patch/AW_ArmySafetyPatch.cs'
$enlist = Read-Source 'Code/patch/AW_EnlistPatch.cs'
$reservePatch = Read-Source 'Code/patch/AW_CityReservePoolPatch.cs'
$slaveryPatch = Read-Source 'Code/patch/AW_SlaveryPatch.cs'
$courtTransition = Read-Source `
    'Code/core/court/CourtOfficerMilitaryTransitionService.cs'
$accession = Read-Source 'Code/core/lineage/AccessionIdentityService.cs'
$slaveService = Read-Source 'Code/core/lineage/SlaveService.cs'
$armyService = Read-Source 'Code/core/lineage/AWArmyService.cs'
$warDirector = Read-Source 'Code/core/lineage/KingdomWarDirectorService.cs'
$retirement = Read-Source 'Code/patch/AW_RetirementPatch.cs'
$standingPatch = Read-Source 'Code/patch/AW_StandingArmyPatch.cs'
$garrison = Read-Source 'Code/core/lineage/WartimeGarrisonService.cs'
$slaveVanguard = Read-Source `
    'Code/core/lineage/TemporarySlaveVanguardService.cs'
$restore = Read-Source 'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs'
$temporary = Read-Source 'Code/core/lineage/TemporaryLevyService.cs'
$benchmarks = Read-Source 'Code/core/policy/RecentFeatureBenchmarkRules.cs'
$failures = [Collections.Generic.List[string]]::new()

if ($authority.Contains('TemporaryLevyService.ProcessPreparationMonth')) {
    $failures.Add('monthly preparation levy must not run from authority cycles')
}
if ($annual.Contains('TemporaryLevyService.OnKingdomYear')) {
    $failures.Add('annual kingdom work must not run AW3 levy recruitment')
}
if ($warPatch.Contains('TemporaryLevyService.OnEmergencyChanged') -or
    $warNotice.Contains('TemporaryLevyService.OnEmergencyChanged')) {
    $failures.Add('war lifecycle must not enqueue proactive levy work')
}
foreach ($legacyField in @('EligibleActorIds', 'ActorIds', 'ActorCursors',
        'ValidationAfterActorIds')) {
    if ($reserve.Contains($legacyField)) {
        $failures.Add("integer manpower pool still contains $legacyField")
    }
}
if (-not $replenishment.Contains(
        'CityReservePoolService.TryReserveWarManpower(')) {
    $failures.Add('replenishment must reserve integer manpower')
}
if (-not $replenishment.Contains('SyntheticLevyService.CreateBatch(')) {
    $failures.Add('replenishment must materialize soldier actors')
}
if (-not $replenishment.Contains(
        'CityReservePoolService.ReleaseUnmaterializedWarReservation(')) {
    $failures.Add('failed soldier creation must return integer manpower')
}
if ($replenishment.Contains(
        'return SyntheticLevyService.CreateBatch(sourceCity,')) {
    $failures.Add('wartime recovery must not spawn before reserving integer manpower')
}
if ($synthetic.Contains('TemporaryLevyService.RegisterSyntheticLevy(')) {
    $failures.Add('replenishment soldiers must not join the removed levy system')
}
if (-not $warPatch.Contains('SyntheticLevyService.OnWarEnded(pWar)') -or
    -not $synthetic.Contains('internal static void OnWarEnded(War war)') -or
    -not $synthetic.Contains('LineageKeys.SYNTHETIC_LEVY_EMERGENCY_ID')) {
    $failures.Add('spawned replenishment soldiers need metadata-based war-end cleanup')
}
if (-not $warPatch.Contains(
        'SyntheticLevyService.OnKingdomLeftWar(pWar, pKingdom)') -or
    -not $synthetic.Contains(
        'internal static void OnKingdomLeftWar(War war, Kingdom kingdom)')) {
    $failures.Add('early war exits must clean that realm synthetic replenishments')
}
if (-not $reserve.Contains('RebuildSyntheticCounts()')) {
    $failures.Add('snapshot-free restore must rebuild integer synthetic counts')
}
$refreshStart = $reserve.IndexOf(
    'internal static void RefreshCapturedCity(City city)')
$countStart = $reserve.IndexOf(
    'internal static int CountAvailable(Kingdom kingdom)', $refreshStart)
if ($refreshStart -ge 0 -and $countStart -gt $refreshStart) {
    $refreshBody = $reserve.Substring($refreshStart,
        $countStart - $refreshStart)
    if ($refreshBody.Contains('ResetWarReserve(pool)')) {
        $failures.Add('occupation refresh must preserve consumed war manpower')
    }
}
if (-not $synthetic.Contains(
        'sourceCity.kingdom?.id == sourceKingdomId')) {
    $failures.Add('synthetic cleanup must not debit a captured city new owner')
}
$cityTransferSynthetic = $reservePatch.IndexOf(
    'SyntheticLevyService.OnCityKingdomChanged(__instance, __state,')
$cityTransferLedger = $reservePatch.IndexOf(
    'CityReservePoolService.OnCityKingdomChanged(__instance, __state,')
if ($cityTransferSynthetic -lt 0 -or $cityTransferLedger -lt 0 -or
    $cityTransferSynthetic -gt $cityTransferLedger -or
    -not $synthetic.Contains(
        'internal static void OnCityKingdomChanged(City city,')) {
    $failures.Add('city transfer must dispose source synthetic actors before rebuilding manpower')
}
if (-not $warPatch.Contains(
        'TemporaryLevyService.ClearReplenishmentStateForWar(pWar)') -or
    -not $temporary.Contains(
        'internal static void ClearReplenishmentStateForWar(War war)')) {
    $failures.Add('reserve exhaustion state must not leak into the next war')
}
if (-not $warPatch.Contains(
        'TemporaryLevyService.ClearReplenishmentState(pKingdom)') -or
    -not $temporary.Contains(
        'internal static void ClearReplenishmentState(Kingdom kingdom)')) {
    $failures.Add('early war exits must clear pending replenishment state')
}
if ($warPatch.Contains('TemporaryLevyService.OnWarStarted(') -or
    $warPatch.Contains('TemporaryLevyService.OnWarEnded(')) {
    $failures.Add('war start and end must not activate legacy levy state')
}
if ($actorDeath.Contains('TemporaryLevyService.OnActorInvalidated(') -or
    $armySafety.Contains('TemporaryLevyService.OnActorInvalidated(') -or
    $enlist.Contains('TemporaryLevyService.OnActorInvalidated(')) {
    $failures.Add('actor lifecycle hooks must not maintain legacy levy membership')
}
if ($actorDeath.Contains('TemporaryLevyService.OnMilitaryCasualty(')) {
    $failures.Add('military casualties must not enter legacy levy recruitment')
}
if ($armySafety.Contains(
        'TemporaryLevyService.TryPromoteExistingLevyCaptain(')) {
    $failures.Add('captain repair must not promote legacy levy members')
}
if (-not $armySafety.Contains(
        'TryPromoteSyntheticCaptain(__instance,') -or
    -not $armySafety.Contains(
        'SyntheticLevyService.Promote(pCaptain)')) {
    $failures.Add('an army containing only replenishment soldiers needs a permanent captain fallback')
}
foreach ($legacyInvalidationSource in @($slaveryPatch, $courtTransition,
        $accession, $slaveService, $synthetic)) {
    if ($legacyInvalidationSource.Contains(
            'TemporaryLevyService.OnActorInvalidated(')) {
        $failures.Add('runtime actor transitions must not maintain legacy levy indexes')
        break
    }
}
foreach ($actorCallback in @('OnActorBecameAdult(', 'OnActorCityChanged(',
        'OnActorKingdomChanged(', 'OnActorEnlisted(',
        'OnActorProfessionChanged(')) {
    if ($reservePatch.Contains("CityReservePoolService.$actorCallback") -or
        $enlist.Contains("CityReservePoolService.$actorCallback") -or
        $slaveryPatch.Contains("CityReservePoolService.$actorCallback")) {
        $failures.Add("integer manpower must not hook $actorCallback")
    }
}
$rebuildStart = $temporary.IndexOf('public static void RebuildRuntime()')
$clearStart = $temporary.IndexOf('public static void ClearRuntime()',
    $rebuildStart)
if ($rebuildStart -ge 0 -and $clearStart -gt $rebuildStart) {
    $rebuild = $temporary.Substring($rebuildStart,
        $clearStart - $rebuildStart)
    if ($rebuild.Contains('Pool(') -or
        $rebuild.Contains('ResumeActiveRecruitmentPlans(')) {
        $failures.Add('restore must clear rather than rebuild legacy actor levy pools')
    }
}
if (-not $restore.Contains('TemporaryLevyService.RebuildRuntime')) {
    $failures.Add('restore pipeline must retain the legacy cleanup pass')
}
if ($benchmarks.Contains('aw3_month_preparation_levy')) {
    $failures.Add('removed monthly levy work must not retain a benchmark entry')
}
if (-not $authority.Contains(
        'RecentFeatureBenchmarkRules.ReplenishmentIndex')) {
    $failures.Add('integer reserve and replenishment work need their own diagnostic')
}
if ($standingPatch.Contains(
        '[HarmonyPatch(typeof(City), "tryToMakeWarrior")]')) {
    $failures.Add('vanilla City.tryToMakeWarrior must not be intercepted')
}
foreach ($legacyRecoverySource in @($armyService, $warDirector)) {
    if ($legacyRecoverySource.Contains(
            'TemporaryLevyService.RequestOffensiveRecovery(') -or
        $legacyRecoverySource.Contains(
            'TemporaryLevyService.RequestCaptainRecovery(') -or
        $legacyRecoverySource.Contains(
            'TemporaryLevyService.HasPendingOffensiveRecovery(')) {
        $failures.Add('army recovery must not schedule legacy levy recruitment')
        break
    }
}
foreach ($syntheticConsumer in @($armyService, $slaveService, $retirement,
        $standingPatch, $garrison, $slaveVanguard)) {
    if ($syntheticConsumer.Contains(
            'TemporaryLevyService.IsTemporaryLevy(')) {
        $failures.Add('spawned replenishment soldiers must use synthetic metadata, not levy membership')
        break
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Vanilla recruitment ownership failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Vanilla recruitment ownership guard passed.'
