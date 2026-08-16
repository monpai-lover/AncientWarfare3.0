$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditStrongholdService.cs'
$planPath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditStrongholdPlan.cs'
$keysPath = Join-Path $root 'Code/core/lineage/ChronicleKeys.cs'
$historyPath = Join-Path $root 'Code/core/lineage/HistoryWriter.cs'
$atlasPath = Join-Path $root 'Code/core/atlas/KingdomAtlasHistoryService.cs'
$deathPath = Join-Path $root 'Code/patch/AW_ActorDeathPatch.cs'

$service = Get-Content -Raw -Encoding UTF8 $servicePath
$plan = Get-Content -Raw -Encoding UTF8 $planPath
$keys = Get-Content -Raw -Encoding UTF8 $keysPath
$history = Get-Content -Raw -Encoding UTF8 $historyPath
$atlas = Get-Content -Raw -Encoding UTF8 $atlasPath
$death = Get-Content -Raw -Encoding UTF8 $deathPath

foreach ($token in @('GateCenters', 'TowerAsset', 'TowerTiles')) {
    if (-not ($plan + $service).Contains($token)) {
        throw "Stronghold tower plan is missing $token"
    }
}
foreach ($token in @('ResolveTowerAsset(', 'getBuilding("order_watch_tower")',
        'CanPlaceGateTower(', 'BuildPlacingType.Load',
        'World.world.buildings.addBuilding(', 'building.setKingdom(',
        'Towers.Add(', 'RemoveStrongholdTowers(',
        'World.world.buildings.removeObject(')) {
    if (-not $service.Contains($token)) {
        throw "Stronghold tower lifecycle is missing $token"
    }
}

$candidateStart = $service.IndexOf(
    'foreach (IReadOnlyList<string> candidateKeys in candidates)')
$candidateEnd = $service.IndexOf(
    'if (interior == null || zoneWallPlan == null', $candidateStart)
if ($candidateStart -lt 0 -or $candidateEnd -le $candidateStart) {
    throw 'Could not isolate stronghold candidate selection'
}
$candidateSelection = $service.Substring($candidateStart,
    $candidateEnd - $candidateStart)
$towerCheckIndex = $candidateSelection.IndexOf('CanPlaceGateTower(')
$acceptIndex = $candidateSelection.IndexOf('interior = candidate;')
if ($towerCheckIndex -lt 0 -or $acceptIndex -le $towerCheckIndex) {
    throw 'Each nine-zone candidate must validate gate towers before acceptance'
}

$createStart = $service.IndexOf('internal static bool TryCreate(')
$newCityIndex = $service.IndexOf('newCityEvent(', $createStart)
$towerAddIndex = $service.IndexOf('PlaceTowers(transaction, stronghold)',
    $newCityIndex)
$activeIndex = $service.IndexOf('BanditStrongholdPhase.Active',
    $towerAddIndex)
if ($createStart -lt 0 -or $newCityIndex -lt 0 -or
    $towerAddIndex -le $newCityIndex -or $activeIndex -le $towerAddIndex) {
    throw 'Gate towers must be created after native city initialization and before active state'
}

$fallStart = $service.IndexOf('private static bool CompleteFall(')
$towerRemoveIndex = $service.IndexOf('RemoveStrongholdTowers(', $fallStart)
$wallRestoreIndex = $service.IndexOf('RestoreWalls(', $fallStart)
$cityRemoveIndex = $service.IndexOf('World.world.cities.removeObject(',
    $fallStart)
if ($fallStart -lt 0 -or $towerRemoveIndex -lt 0 -or
    $wallRestoreIndex -le $towerRemoveIndex -or
    $cityRemoveIndex -le $wallRestoreIndex) {
    throw 'Suppression must remove towers before walls and city removal'
}

$events = @('bandit_stronghold_established',
    'bandit_suppression_victory', 'bandit_suppressed',
    'bandit_stronghold_suppressed')
foreach ($event in $events) {
    if (-not ($keys + $service).Contains($event)) {
        throw "Stronghold chronicle lifecycle is missing $event"
    }
    if ($atlas.Contains($event)) {
        throw "$event must not enter atlas territorial queries"
    }
}
foreach ($token in @('TryRecordCity(', 'bandit-stronghold-established:',
        'bandit-suppressed-city:', 'bandit-suppressed-kingdom:',
        'bandit-suppression-victory:', 'OnBanditResidentDied(')) {
    if (-not ($history + $service + $death).Contains($token)) {
        throw "Stronghold chronicle lifecycle is missing $token"
    }
}
foreach ($token in @('CanAttributeHostileKiller(',
        'DeathClearsAttacker(', 'AttackType.Starvation')) {
    if (-not ($service + $death).Contains($token)) {
        throw "Bandit death attribution is missing $token"
    }
}

Write-Output 'Bandit stronghold history and tower source guard passed.'
