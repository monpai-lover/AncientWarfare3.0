$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
function Read-Source([string] $relative) {
    $path = Join-Path $repoRoot $relative
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing source file: $relative"
    }
    return Get-Content -Raw -Encoding UTF8 -LiteralPath $path
}

$defense = Read-Source `
    'Code/core/lineage/MandateBorderDefenseService.cs'
$refresh = Read-Source `
    'Code/core/lineage/MandateBorderWallRefreshService.cs'
$patch = Read-Source 'Code/patch/AW_MandateBorderWallPatch.cs'

$decisionStart = $defense.IndexOf('public static bool ExecuteDecision(')
$decisionEnd = $defense.IndexOf('public static void OnMandateWarStarted(',
    $decisionStart)
$decision = $defense.Substring($decisionStart,
    $decisionEnd - $decisionStart)
if (-not $decision.Contains(
        'MandateBorderWallRefreshService.Activate(')) {
    throw 'Only the border-defense decision may activate wall maintenance'
}

$warStart = $defense.IndexOf('public static void OnMandateWarStarted(')
$warEnd = $defense.IndexOf('private static bool ReinforceBorder(', $warStart)
$war = $defense.Substring($warStart, $warEnd - $warStart)
if (-not $war.Contains(
        'MandateBorderWallRefreshService.IsActivated(')) {
    throw 'Mandate war refresh does not require prior decision activation'
}
if ($war.Contains('MandateBorderWallRefreshService.Activate(')) {
    throw 'Mandate war start activates border walls implicitly'
}

$reinforceStart = $defense.IndexOf('private static bool ReinforceBorder(')
$reinforceEnd = $defense.IndexOf('private static int LimitedGuardCap(',
    $reinforceStart)
$reinforce = $defense.Substring($reinforceStart,
    $reinforceEnd - $reinforceStart)
$towerIndex = $reinforce.IndexOf('BuildBorderTowers(')
$refreshIndex = $reinforce.IndexOf('RefreshCitiesNow(')
if ($towerIndex -lt 0 -or $refreshIndex -lt 0 -or
    $towerIndex -gt $refreshIndex) {
    throw 'Watch towers must be built before the same wall refresh'
}
foreach ($token in @('SelectBorderArmyCities(', 'wallCities')) {
    if (-not $reinforce.Contains($token)) {
        throw "Wall and capped army city scopes are not separated: $token"
    }
}

foreach ($token in @('MandateBorderWallStateStore.Read(',
        'MandateBorderWallStateStore.Write(',
        'MandateBorderWallRefreshRules.ShouldRestore(',
        'AssetManager.top_tiles.get(',
        'asset.type == "type_watch_tower"',
        'building.tiles', 'pCarveRoadPassages: false',
        'DeferredRuntimeWorkService.EnqueueCoalesced(',
        'DeferredWorkClass.CriticalRuntime',
        'mandate_border_wall_refresh:')) {
    if (-not $refresh.Contains($token)) {
        throw "Mandate wall refresh is missing $token"
    }
}
if ($refresh.Contains('removeObject(building)') -or
    $refresh.Contains('removeObject(tower)')) {
    throw 'Mandate wall refresh must never delete watch towers'
}

foreach ($token in @('typeof(City), "setKingdom"',
        'typeof(TileZone), "setCity"',
        'ObserveCityOwnershipChange(', 'ObserveZoneOwnershipChange(')) {
    if (-not $patch.Contains($token)) {
        throw "Mandate wall ownership patch is missing $token"
    }
}

Write-Output 'Mandate border wall lifecycle source guard passed.'
