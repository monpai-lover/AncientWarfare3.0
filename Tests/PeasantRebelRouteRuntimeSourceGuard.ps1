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
$route = Get-Content -Raw 'Code/core/lineage/PeasantRebelRouteService.cs'
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
Require $wall 'pCity.recalculateNeighbourZones()' `
    'Bandit wall capture must use the original city boundary refresh.'
Require $wall 'pCity.border_zones' `
    'Bandit walls must start from the entry-time city border zones.'
Require $wall 'neighboursAll' `
    'Bandit wall capture must inspect original neighboring tiles.'
Require $wall 'TopTileLibrary.wall_wild' `
    'Bandit walls must reuse the original wooden wall asset.'
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
RequireOrder $bandit 'CanMutateAuthority(' 'city.joinAnotherKingdom(' `
    'Replica authority must be checked before bandit city transfer.'
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
RequireOrder $banditYearBody 'TryResolveFoundingCity(' `
    'SafeCityCount(pKingdom) > 1' `
    'Founding-city validity must be checked before malformed city counts.'

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
