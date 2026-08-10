$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Read-Source([string] $relativePath) {
    return [IO.File]::ReadAllText((Join-Path $repo $relativePath))
}

function Section([string] $source, [string] $start, [string] $end) {
    $startIndex = $source.IndexOf($start, [StringComparison]::Ordinal)
    if ($startIndex -lt 0) { return '' }
    $endIndex = $source.IndexOf($end, $startIndex + $start.Length,
        [StringComparison]::Ordinal)
    if ($endIndex -lt 0) { return $source.Substring($startIndex) }
    return $source.Substring($startIndex, $endIndex - $startIndex)
}

$authority = Read-Source 'Code/core/performance/AWAuthorityCycleService.cs'
$annual = Read-Source 'Code/core/policy/KingdomAnnualWorkService.cs'
$reserve = Read-Source 'Code/core/lineage/CityReservePoolService.cs'
$replenishment = Read-Source `
    'Code/core/lineage/ArmyReplenishmentOperationService.cs'
$synthetic = Read-Source 'Code/core/lineage/SyntheticLevyService.cs'
$temporary = Read-Source 'Code/core/lineage/TemporaryLevyService.cs'
$standing = Read-Source 'Code/patch/AW_StandingArmyPatch.cs'
$armySafety = Read-Source 'Code/patch/AW_ArmySafetyPatch.cs'
$actorDeath = Read-Source 'Code/patch/AW_ActorDeathPatch.cs'
$enlist = Read-Source 'Code/patch/AW_EnlistPatch.cs'
$slavery = Read-Source 'Code/patch/AW_SlaveryPatch.cs'
$reservePatch = Read-Source 'Code/patch/AW_CityReservePoolPatch.cs'
$membership = Read-Source `
    'Code/core/lineage/ArmyMembershipReconciliationService.cs'
$rebellionCollapse = Read-Source `
    'Code/core/lineage/RebellionCollapseSettlementService.cs'
$wartimePotential = Read-Source `
    'Code/core/lineage/WartimeMilitaryPotentialService.cs'
$diplomacyProposal = Read-Source `
    'Code/core/lineage/DiplomacyProposalService.cs'
$failures = [Collections.Generic.List[string]]::new()

function Reject([string] $source, [string] $token, [string] $message) {
    if ($source.Contains($token)) { $failures.Add($message) }
}

function Require([string] $source, [string] $token, [string] $message) {
    if (-not $source.Contains($token)) { $failures.Add($message) }
}

Reject $standing '[HarmonyPatch(typeof(City), "tryToMakeWarrior")]' `
    'vanilla City.tryToMakeWarrior must not be intercepted'
Reject $annual 'TemporaryLevyService.OnKingdomYear' `
    'annual work must not run legacy levy recruitment'
Reject $authority 'TemporaryLevyService.ProcessPreparationMonth' `
    'authority cycles must not run legacy preparation recruitment'

foreach ($token in @('EligibleActorIds', 'ActorCursors',
        'ValidationAfterActorIds')) {
    Reject $reserve $token "city reserve runtime still owns $token"
}
foreach ($token in @('WarReserveCapacity', 'WarReserveConsumed',
        'OpenOrReadWarReserve(', 'TryReserveWarManpower(',
        'ReleaseUnmaterializedWarReservation(')) {
    Reject $reserve $token `
        "city reserve runtime still owns parallel wartime ledger token $token"
}
$authorityBody = Section $reserve `
    'internal static void ProcessAuthorityCycle()' `
    'internal static void OnWarStarted('
Reject $authorityBody 'World.world.units' `
    'high-frequency reserve maintenance must not scan all actors'
Reject $authorityBody '.units' `
    'high-frequency reserve maintenance must not inspect resident actors'

Require $replenishment `
    'SyntheticMobilizationLedgerService.TryReserveReplacement(' `
    'replenishment must reserve from the synthetic city-war ledger'
Require $replenishment 'SyntheticLevyService.CreateBatch(' `
    'replenishment must materialize bounded synthetic soldiers'
Reject $replenishment 'CityReservePoolService.TryReserveWarManpower(' `
    'replenishment must not use the retired city reserve war ledger'

foreach ($token in @('TemporaryLevyService.RegisterSyntheticLevy(',
        'TemporaryLevyService.OnActorInvalidated(',
        'SyntheticLevyService.Promote(')) {
    Reject $synthetic $token `
        "synthetic lifecycle still depends on retired behavior $token"
}
Reject $armySafety 'SyntheticLevyService.Promote(' `
    'a synthetic soldier must never become a permanent captain'
Reject $armySafety 'TryPromoteExistingLevyCaptain(' `
    'retired temporary levies must never become permanent captains'

foreach ($source in @($actorDeath, $enlist, $slavery, $membership)) {
    Reject $source 'TemporaryLevyService.OnActorInvalidated(' `
        'actor transitions must not maintain the retired levy index'
}
foreach ($source in @($enlist, $slavery, $reservePatch)) {
    foreach ($callback in @('OnActorBecameAdult(', 'OnActorCityChanged(',
            'OnActorKingdomChanged(', 'OnActorEnlisted(',
            'OnActorProfessionChanged(')) {
        Reject $source "CityReservePoolService.$callback" `
            "integer reserve must not hook actor callback $callback"
    }
}

$rebuild = Section $temporary 'public static void RebuildRuntime()' `
    'public static void ClearRuntime()'
Require $rebuild 'BeginLegacyMigration();' `
    'load restore must start bounded legacy levy migration'
Reject $rebuild 'World.world.units' `
    'load restore must not synchronously scan every actor'
Reject $rebuild 'ResumeActiveRecruitmentPlans(' `
    'load restore must not resume legacy recruitment plans'
Reject $rebuild 'Pool(' `
    'load restore must not rebuild legacy actor pools'
Require $temporary 'ProcessLegacyMigration()' `
    'legacy real levies need a bounded migration worker'
Require $authority 'TemporaryLevyService.ProcessLegacyMigration' `
    'authority scheduling must drain bounded legacy levy migration'
Require $temporary 'TemporaryMilitaryDemobilizationService.RestoreCivilian(' `
    'legacy real levies must return to civilian life'

$kingdomCount = Section $reserve `
    'internal static int CountAvailable(Kingdom kingdom)' `
    'internal static int CountAvailable(City city)'
Reject $kingdomCount 'kingdom.cities' `
    'kingdom availability reads must remain O(1)'
Reject $kingdomCount 'for (' `
    'kingdom availability reads must not synchronously scan cities'
Require $reserve 'PublishedTotalAvailable' `
    'kingdom availability must publish only complete cache generations'
Require $reserve 'BuildingTotalAvailable' `
    'bounded city refreshes must build totals off to the side'
Require $reserve 'TryCountAvailable(Kingdom kingdom,' `
    'callers need an explicit readiness result for unpublished totals'
Reject $kingdomCount '? 1 : 0' `
    'an unpublished cache cannot fabricate one available soldier'
Require $reserve 'RebuildRequested' `
    'ledger changes during a rebuild must coalesce into a later generation'
Require $reserve 'CurrentCityCount(kingdom)' `
    'city-count changes must be detected without scanning the city list'
$ledgerChanged = Section $reserve `
    'internal static void OnSyntheticLedgerChanged(' `
    'private static int CountWartimeReplacement('
Require $ledgerChanged 'RequestRebuild(kingdom, state);' `
    'synthetic ledger changes must request a coalesced cache generation'
Reject $ledgerChanged 'Invalidate(kingdom, state);' `
    'synthetic ledger changes must not restart an in-progress generation'
Require $rebellionCollapse `
    '.TryCountAvailable(rebel,' `
    'rebellion collapse must wait for a published reserve generation'
Reject $rebellionCollapse 'CountAvailable(rebel)' `
    'rebellion collapse cannot treat unpublished reserves as zero'
Require $wartimePotential `
    '.TryCountAvailable(pKingdom,' `
    'wartime potential must preserve reserve readiness'
Require $diplomacyProposal 'TryCountPotentialWarriorsBounded(' `
    'separate-peace exhaustion must wait for published reserve potential'

if ($failures.Count -gt 0) {
    Write-Host "Vanilla recruitment ownership failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Vanilla recruitment ownership guard passed.'
