$ErrorActionPreference = 'Stop'

function Require([string]$Text, [string]$Needle, [string]$Message) {
    if (-not $Text.Contains($Needle)) { throw $Message }
}

function Forbid([string]$Text, [string]$Needle, [string]$Message) {
    if ($Text.Contains($Needle)) { throw $Message }
}

function RequireOrder([string]$Text, [string]$Before, [string]$After,
        [string]$Message) {
    $beforeIndex = $Text.IndexOf($Before)
    $afterIndex = $Text.IndexOf($After)
    if ($beforeIndex -lt 0 -or $afterIndex -le $beforeIndex) {
        throw $Message
    }
}

function RequireCount([string]$Text, [string]$Needle, [int]$Count,
        [string]$Message) {
    $actual = ([regex]::Matches($Text, [regex]::Escape($Needle))).Count
    if ($actual -ne $Count) {
        throw ($Message + " Expected $Count, found $actual.")
    }
}

$mandate = Get-Content -Raw 'Code/core/lineage/MandateRebelService.cs'
$rebelState = Get-Content -Raw `
    'Code/core/lineage/MandateRebelStateRules.cs'
$route = Get-Content -Raw 'Code/core/lineage/PeasantRebelRouteService.cs'
$policy = Get-Content -Raw 'Code/core/policy/KingdomPolicyService.cs'
$warDecision = Get-Content -Raw 'Code/core/lineage/WarDecisionService.cs'
$warPatch = Get-Content -Raw 'Code/patch/AW_WarPatch.cs'
$occupation = Get-Content -Raw `
    'Code/patch/AW_CityOccupationAccelerationPatch.cs'
$settlement = Get-Content -Raw `
    'Code/core/lineage/WarPeaceSettlementRuntime.cs'
$rulerProjection = Get-Content -Raw `
    'Code/core/lineage/RulerAppellationService.cs'
$heirProjection = Get-Content -Raw `
    'Code/core/lineage/HeirTitleRules.cs'
$historyRules = Get-Content -Raw `
    'Code/core/lineage/HistoryLocalizationRules.cs'
$othersLocale = Get-Content -Raw -Encoding UTF8 'locales/others.csv'
$mandateLocale = Get-Content -Raw -Encoding UTF8 `
    'locales/aw3_mandate_extra.csv'
$restorePipeline = Get-Content -Raw `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs'
$chroniclePatch = Get-Content -Raw 'Code/patch/AW_ChroniclePatch.cs'
$uiSources = (Get-ChildItem 'Code/ui' -Recurse -File -Filter '*.cs' |
    ForEach-Object { Get-Content -Raw $_.FullName }) -join "`n"
$policyUi = Get-Content -Raw 'Code/ui/windows/KingdomPolicyWindow.cs'
$kingdomUi = Get-Content -Raw 'Code/ui/windows/KingdomWindowAddition.cs'
$policyLocale = Get-Content -Raw -Encoding UTF8 'Locales/aw3_policy_ui.csv'
$bandit = if (Test-Path 'Code/core/lineage/PeasantRebelBanditRoute.cs') {
    Get-Content -Raw 'Code/core/lineage/PeasantRebelBanditRoute.cs'
} else {
    ''
}
$wall = if (Test-Path `
        'Code/core/lineage/PeasantRebelBanditWallService.cs') {
    Get-Content -Raw `
        'Code/core/lineage/PeasantRebelBanditWallService.cs'
} else {
    ''
}
$territory = if (Test-Path `
        'Code/core/lineage/PeasantRebelBanditTerritoryService.cs') {
    Get-Content -Raw `
        'Code/core/lineage/PeasantRebelBanditTerritoryService.cs'
} else {
    ''
}
$government = if (Test-Path `
        'Code/core/lineage/PeasantRebelGovernmentTransitionService.cs') {
    Get-Content -Raw `
        'Code/core/lineage/PeasantRebelGovernmentTransitionService.cs'
} else {
    ''
}
$sharedWall = if (Test-Path `
        'Code/core/lineage/CultiwayStyleCityWallService.cs') {
    Get-Content -Raw `
        'Code/core/lineage/CultiwayStyleCityWallService.cs'
} else {
    ''
}
$notice = Get-Content -Raw -Encoding UTF8 'THIRD_PARTY_NOTICES.md'
$packagedNotice = if (Test-Path `
        'THIRD_PARTY_NOTICES/Cultiway-Wall-MIT.txt') {
    Get-Content -Raw -Encoding UTF8 `
        'THIRD_PARTY_NOTICES/Cultiway-Wall-MIT.txt'
} else {
    ''
}

Require $sharedWall 'CultiwayStyleWallGeometryRules.Compute(' `
    'The WorldBox wall adapter must use detached Cultiway geometry.'
Require $sharedWall 'tile.setTopTileType(pWallType)' `
    'The shared tool must place the caller-selected original wall asset.'
Require $sharedWall 'building.asset.type' `
    'Remote utility filtering must follow Cultiway building bounds.'
Require $sharedWall 'building.asset.docks' `
    'Dock tiles must feed Cultiway passage carving.'
Forbid $sharedWall 'MapAction.terraformTop' `
    'The shared tool must not mutate terrain or destroy paths.'
Require $notice 'Cultiway-Reborn city-wall geometry' `
    'The adapted wall source needs an MIT notice.'
Require $packagedNotice 'Copyright (c) 2025 Inmny' `
    'The packaged wall notice must retain the Cultiway copyright.'

Require $mandate 'PeasantRebelRouteService.InitializeAndEnter(' `
    'CreateRebelKingdom must dispatch through the route coordinator.'
Require $mandate 'EnterFoundingRoute(' `
    'The existing founding flow must have a dedicated adapter.'
Require $mandate 'TryPullAlignedCities(pRebel, pOriginKingdom, pFoundingCity);' `
    'Aligned-city recruitment must remain behind EnterFoundingRoute.'
Require $mandate 'StartExistingRebelWar(pOriginKingdom, pRebel);' `
    'The existing rebellion war must remain behind EnterFoundingRoute.'
Require $route 'generateName(MetaType.Kingdom' `
    'Route initialization must use the original kingdom name generator.'
Require $bandit 'World.world.wars.endWar(war, WarWinner.Peace)' `
    'Bandit entry must end active wars through the original war manager.'
Require $warDecision 'PeasantRebelRouteService.CanStartWar' `
    'AW3 war decisions must use the route permission source.'
Require $warDecision 'PeasantRebelRouteService.IsOriginSuppressionPair' `
    'Origin suppression must bypass non-engine war policy checks.'
Require $warPatch 'PeasantRebelRouteService.CanStartWar' `
    'Native war starts must use the route permission source.'
Require $occupation 'PeasantRebelRouteService.CanAcquireCity(' `
    'Capture and direct city transfer must enforce the one-city invariant.'
Require $occupation 'City.joinAnotherKingdom' `
    'The authoritative original city transfer boundary must stay patched.'
Require $settlement 'PeasantRebelRouteService.CanAcquireCity(' `
    'Peace cessions must enforce the one-city invariant before mutation.'
Require $territory 'CaptureCurrentCities(' `
    'Bandit entry must persist all current city IDs.'
Require $territory 'MANDATE_REBEL_BANDIT_ENTRY_CITY_IDS' `
    'Bandit territory must use its persisted whitelist.'
Require $territory 'JsonConvert.SerializeObject' `
    'Bandit territory persistence must be structured JSON.'
Require $territory 'IsWhitelistMissing(' `
    'Restore must distinguish missing legacy data from corruption.'
Require $route 'PeasantRebelBanditTerritoryService.CanAcquire(' `
    'Acquisition boundaries must query the whitelist service.'
Forbid $route 'currentCityCount == 0' `
    'The single-city invariant must be removed.'
Require $government 'TrySetClassState(' `
    'Special government changes need one coordinator.'
Require $government 'CanSwitchGovernment(' `
    'Authority must share detached transition rules with UI.'
Require $government `
    'if (pTargetClass == KingdomPolicyDefs.ClassRebel)' `
    'Manual peasant-rebel selection needs dedicated initialization.'
Require $government 'InitializeManualFoundingGovernment(' `
    'Manual peasant rebels must receive complete route metadata.'
Require $route 'internal static bool InitializeManualFoundingGovernment(' `
    'The route service must own manual founding-state initialization.'
RequireCount $route 'TryInitializeRouteMetadata(' 3 `
    'Real and manual rebels must share one route metadata initializer.'
Require $route 'MandateRebelService.MarkRebelKingdom(' `
    'Manual peasant rebels must receive the normal rebel flags.'
Require $bandit 'CaptureCurrentCities(' `
    'Bandit entry must capture retained territory.'
RequireOrder $government 'CanMutateAuthority(' 'EnterBandit(' `
    'Authority must be checked before full bandit entry.'
Require $route 'PeasantRebelGovernmentTransitionService.TryEnterBandit(' `
    'AI and manual bandit entry must share the transition coordinator.'
Forbid $bandit 'city.joinAnotherKingdom(' `
    'Formal bandit entry must retain every current city.'
Require $rebelState 'KingdomPolicyDefs.ClassBandit' `
    'Formal bandits must remain current rebel governments.'
Require $route 'ResolveGovernmentClass(' `
    'Restore must reconcile route and formal government state.'
Require $route 'HasValidWhitelist(' `
    'Restore must validate persisted entry territory.'
Require $route 'resolvedRoute' `
    'Runtime cache must follow the reconciled route.'
Require $policy 'if (value == KingdomPolicyDefs.ClassBandit)' `
    'Class reads must preserve the formal bandit state.'
RequireOrder $policy 'if (!string.IsNullOrEmpty(value)) return value;' `
    'MandateRebelService.IsRebelKingdom(pKingdom)' `
    'Persisted policy class must outrank stale rebel flags.'
Require $government 'SettleRebelGovernment(' `
    'Rebel-to-ordinary transitions must use settlement cleanup.'
Require $mandate 'public static bool SettleRebelGovernment(' `
    'Settlement must report whether the requested class was applied.'
RequireOrder $mandate 'CanMutateAuthority(' `
    'pKingdom.data.set(LineageKeys.MANDATE_REBEL, false);' `
    'Settlement authority must be checked before rebel state writes.'
Require $route 'ApplyClassStateDirect(' `
    'Bandit exit must persist peasant rebel government first.'
Require $policyUi 'CanSwitchGovernment(current, classId)' `
    'UI must share transition availability with authority.'
Require $policyUi 'button.interactable = !active && canSwitch;' `
    'Invalid transitions must be disabled.'
Require $policyUi 'AW3CommandRequest.SetPolicyClass(' `
    'Policy UI must keep using the authoritative multiplayer command.'
Require $policyUi 'KingdomPolicyDefs.ClassBandit' `
    'The formal bandit class must render.'
Require $policyUi 'AddClassStateIcon(box.transform, classId)' `
    'Every government choice must render its mapped icon.'
Require $kingdomUi 'GetClassIconPath(classId)' `
    'The kingdom summary must share the formal class icon mapping.'
Require $policyLocale 'aw_policy_class_peasant_bandit,' `
    'Bandit class name must be localized.'
Require $policyLocale 'aw_policy_class_peasant_bandit_desc,' `
    'Bandit class description must be localized.'
Require $policyLocale 'aw_policy_class_transition_locked,' `
    'Invalid transition feedback must be localized.'
Require $wall 'CultiwayStyleCityWallService.Build(' `
    'Every bandit city must use the shared Cultiway wall tool.'
Require $wall 'TopTileLibrary.wall_wild' `
    'Bandits must retain original wooden walls.'
Require $wall 'foreach (City city in pKingdom.getCities())' `
    'Every retained bandit city must receive its own wall.'
Require $wall 'CultiwayStyleCityWallService.TryPlan(' `
    'Bandit entry must preflight complete city wall geometry.'
Forbid $wall 'city.border_zones' `
    'Bandit walls must no longer scan incomplete border zones.'
Forbid $wall 'TouchesOutsideKingdom' `
    'Bandit wall geometry belongs to the shared city tool.'
Forbid $wall 'CaptureAndBuild(Kingdom pKingdom, City pCity)' `
    'Walls must not remain tied to one city.'
Require $wall 'tile.setTopTileType(TopTileLibrary.wall_wild)' `
    'Bandit walls must use the original top-tile mutation API.'
Require $wall 'World.world?.GetTile(point.x, point.y)' `
    'Bandit wall repair must resolve only persisted coordinates.'
Forbid $wall 'new TopTileType' `
    'Bandit walls must not instantiate a custom wall type.'
Forbid $wall 'AssetManager.top_tiles.add' `
    'Bandit walls must not register a custom wall asset.'
Forbid $wall 'void setTopTileType' `
    'Bandit wall service must not copy the original tile implementation.'
Require $route 'StartExistingRebelWar' `
    'Bandit conversion must reuse the existing rebellion-war path.'
Require $route 'PeasantRebelRouteIds.Founding' `
    'Bandit conversion must persist the one-way founding route.'
Forbid $route 'MANDATE_REBEL_BANDIT_WALLS, ""' `
    'Conversion must preserve the fixed wooden wall coordinates.'
Forbid $route 'new War(' `
    'Route conversion must not implement a second war constructor.'
RequireOrder $bandit 'CanEvaluateWeakOriginTransition' `
    'Randy.randomInt(0, 100)' `
    'Transition randomness must run only after eligibility checks.'
Require $route 'RenameForRoute(' `
    'Route names must use the shared kingdom projection boundary.'
Require $rulerProjection 'RouteRulerTitleKey(true)' `
    'The shared ruler read model must project the bandit title.'
Require $heirProjection 'RouteHeirTitleKey(true)' `
    'The shared heir read model must project the bandit title.'
Require $warPatch 'PeasantRebelRouteService.OnWarStarted(__result)' `
    'Native war lifecycle must record origin suppression starts.'
foreach ($key in @(
        'aw_bandit_route_name', 'aw_founding_route_name',
        'aw_bandit_ruler_title', 'aw_bandit_heir_title')) {
    Require $othersLocale ($key + ',') `
        ('Missing route locale key: ' + $key)
}
foreach ($key in @(
        'aw_hist_rebel_route_founding', 'aw_hist_rebel_route_bandit',
        'aw_hist_bandit_suppression_started',
        'aw_hist_bandit_converted', 'aw_hist_bandit_destroyed')) {
    Require $mandateLocale ($key + ',') `
        ('Missing bandit history CSV key: ' + $key)
    Require $historyRules ('new Entry("' + $key + '"') `
        ('Missing bandit history registry key: ' + $key)
}
$hardCodedRuler = -join ([char]0x5927, [char]0x5F53, [char]0x5BB6)
$hardCodedHeir = -join ([char]0x5C11, [char]0x5F53, [char]0x5BB6)
Forbid $uiSources $hardCodedRuler `
    'UI files must not hard-code the bandit ruler title.'
Forbid $uiSources $hardCodedHeir `
    'UI files must not hard-code the bandit heir title.'

Require $route 'internal static void RebuildRuntime()' `
    'Route runtime must rebuild from persisted kingdom data.'
Require $route 'ClearRuntime();' `
    'Route runtime rebuild must start from a clean cache.'
Require $route 'RulerAppellationService.RefreshLivingProjection(kingdom);' `
    'Restored routes must refresh the shared ruler projection.'
$rebuildStart = $route.IndexOf('internal static void RebuildRuntime()')
$rebuildEnd = $route.IndexOf('internal static void RemoveRuntime(',
    $rebuildStart)
if ($rebuildStart -lt 0 -or $rebuildEnd -le $rebuildStart) {
    throw 'Could not isolate the route runtime rebuild method.'
}
$rebuildBody = $route.Substring($rebuildStart,
    $rebuildEnd - $rebuildStart)
Forbid $rebuildBody 'InitializeAndEnter(' `
    'Restore must not replay route entry effects.'
Forbid $rebuildBody 'EnterExistingBanditGovernment' `
    'Restore must not replay formal government entry.'
Forbid $rebuildBody 'endWar' `
    'Restore must not mutate diplomacy.'
Forbid $rebuildBody 'RenameForRoute' `
    'Restore must not rename kingdoms.'
Forbid $rebuildBody 'CaptureAndBuild' `
    'Restore must not rebuild entry walls.'
RequireCount $restorePipeline `
    'new AW3RestoreStage("peasant_rebel_routes",' 3 `
    'Both restore pipelines and cache reset must own a route stage.'
RequireCount $restorePipeline `
    'PeasantRebelRouteService.RebuildRuntime),' 2 `
    'Both restore pipelines must rebuild peasant rebel routes.'
RequireCount $restorePipeline `
    'PeasantRebelRouteService.ClearRuntime),' 1 `
    'Runtime cache reset must clear peasant rebel routes.'

RequireOrder $mandate 'CanMutateAuthority(' 'pCity.makeOwnKingdom(' `
    'Replica authority must be checked before creating a rebel kingdom.'
$mandateYearStart = $mandate.IndexOf(
    'public static void OnKingdomYear(Kingdom pKingdom)')
$mandateYearEnd = $mandate.IndexOf(
    'internal static void RunFoundingRouteYear(', $mandateYearStart)
if ($mandateYearStart -lt 0 -or $mandateYearEnd -le $mandateYearStart) {
    throw 'Could not isolate the Mandate rebel annual dispatcher.'
}
$mandateYearBody = $mandate.Substring($mandateYearStart,
    $mandateYearEnd - $mandateYearStart)
RequireOrder $mandateYearBody 'CanMutateAuthority(' `
    'pKingdom.data.set(LineageKeys.MANDATE_REBEL_LAST_YEAR, year);' `
    'Replica authority must be checked before the annual marker write.'
RequireOrder $route 'CanMutateAuthority(' 'Randy.randomInt(0, 100)' `
    'Replica authority must be checked before route selection randomness.'
RequireOrder $bandit 'CanMutateAuthority(' `
    'World.world.wars.endWar(war, WarWinner.Peace)' `
    'Replica authority must be checked before bandit peace mutations.'
RequireOrder $wall 'CanMutateAuthority(' `
    'tile.setTopTileType(TopTileLibrary.wall_wild)' `
    'Replica authority must be checked before wall placement or repair.'
$banditYearStart = $bandit.IndexOf(
    'public void OnKingdomYear(Kingdom pKingdom)')
$banditYearEnd = $bandit.IndexOf(
    'public bool CanDeclareWar(', $banditYearStart)
if ($banditYearStart -lt 0 -or $banditYearEnd -le $banditYearStart) {
    throw 'Could not isolate the bandit annual dispatcher.'
}
$banditYearBody = $bandit.Substring($banditYearStart,
    $banditYearEnd - $banditYearStart)
Require $banditYearBody 'ResolveTransitionCity(pKingdom)' `
    'Annual bandit work must survive loss of the original founding city.'
Forbid $banditYearBody 'SafeCityCount(pKingdom) > 1' `
    'Multi-city formal bandits must continue annual route work.'

$removeStart = $chroniclePatch.IndexOf(
    'internal static void RemoveKingdom_Prefix(')
$removeEnd = $chroniclePatch.IndexOf(
    'internal static void RemoveKingdom_Postfix(', $removeStart)
if ($removeStart -lt 0 -or $removeEnd -le $removeStart) {
    throw 'Could not isolate the kingdom removal prefix.'
}
$removeBody = $chroniclePatch.Substring($removeStart,
    $removeEnd - $removeStart)
RequireOrder $removeBody `
    'KingdomSelectionLifecycleService.OnKingdomDestroying(pKingdom);' `
    'PeasantRebelRouteService.OnKingdomDestroying(pKingdom,' `
    'Route cleanup must follow local kingdom selection cleanup.'
RequireOrder $removeBody `
    'PeasantRebelRouteService.OnKingdomDestroying(pKingdom,' `
    'if (AW3MultiplayerReplicaScope.IsApplying)' `
    'Route cleanup must run before the replica destruction early return.'
RequireOrder $removeBody 'CanMutateAuthority(' `
    'PeasantRebelRouteService.OnKingdomDestroying(pKingdom,' `
    'Extinction authority must reject replica sessions, not only apply scope.'
Require $route 'internal static void OnKingdomDestroying(' `
    'Route runtime must clean up when the original kingdom path destroys it.'
Require $bandit 'internal static void RecordDestruction(' `
    'Bandit destruction history must validate active origin suppression.'
Forbid ($route + $bandit + $wall) 'KingdomManager.removeObject' `
    'Route-owned code must reuse normal kingdom extinction.'
Forbid ($route + $bandit + $wall) 'setTopTileType(null)' `
    'Route extinction and conversion must not remove fixed wall tiles.'

Write-Host 'Peasant rebel route runtime source guard passed.'
