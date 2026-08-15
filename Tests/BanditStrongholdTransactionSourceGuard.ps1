$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$planPath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditStrongholdPlan.cs'
$servicePath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditStrongholdService.cs'

if (-not (Test-Path -LiteralPath $planPath)) {
    throw 'Missing PeasantRebelBanditStrongholdPlan.cs'
}
if (-not (Test-Path -LiteralPath $servicePath)) {
    throw 'Missing PeasantRebelBanditStrongholdService.cs'
}

$plan = Get-Content -Raw -Encoding UTF8 $planPath
$service = Get-Content -Raw -Encoding UTF8 $servicePath
foreach ($token in @('TryPlan(', 'CultiwayStyleCityWallService.TryPlan',
        'TopTileLibrary.wall_wild', 'IsViableSplit', 'FixedZoneKeys',
        'ReserveMotherActor', 'MotherCoreTile')) {
    if (($plan + $service) -notmatch [regex]::Escape($token)) {
        throw "Stronghold preflight is missing $token"
    }
}
foreach ($token in @('TryCreate(', 'World.world.cities.newCity',
        'setUnitMetas', 'newCityEvent', 'addZone(', 'joinCity(',
        'spawnOn(', 'joinAnotherKingdom(', 'addBuilding("bonfire"',
        'setTopTileType', 'Rollback', 'PeasantRebelBanditStateStore.Write')) {
    if ($service -notmatch [regex]::Escape($token)) {
        throw "Stronghold transaction is missing $token"
    }
}

$tryPlanStart = $service.IndexOf('internal static bool TryPlan(')
$tryCreateStart = $service.IndexOf('internal static bool TryCreate(')
if ($tryPlanStart -lt 0 -or $tryCreateStart -le $tryPlanStart) {
    throw 'TryPlan must precede TryCreate'
}
$preflight = $service.Substring($tryPlanStart,
    $tryCreateStart - $tryPlanStart)
foreach ($mutation in @('.newCity(', '.addZone(', '.joinCity(',
        '.setTopTileType(', '.addBuilding(')) {
    if ($preflight -match [regex]::Escape($mutation)) {
        throw "Preflight mutates world state through $mutation"
    }
}

Write-Output 'Bandit stronghold transaction source guard passed.'
