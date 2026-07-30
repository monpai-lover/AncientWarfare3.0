$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $root 'Code\core\lineage\CityArmyReinforcementService.cs'
$standingPath = Join-Path $root 'Code\core\lineage\StandingArmyService.cs'
$controllerPath = Join-Path $root 'Code\core\lineage\ArmyRtsControllerService.cs'
$levyPath = Join-Path $root 'Code\core\lineage\TemporaryLevyService.cs'

if (-not (Test-Path -LiteralPath $servicePath)) {
    throw 'CityArmyReinforcementService must provide shared city allocations.'
}

$service = Get-Content -LiteralPath $servicePath -Raw
$standing = Get-Content -LiteralPath $standingPath -Raw
$controller = Get-Content -LiteralPath $controllerPath -Raw
$levy = Get-Content -LiteralPath $levyPath -Raw

foreach ($token in @(
    'CityArmyReinforcementRules.Allocate(',
    'World.world?.armies',
    'ArmyRtsControllerService.TryGetMission(')) {
    if (-not $service.Contains($token)) {
        throw "shared city allocation service missing $token"
    }
}

if (-not $standing.Contains('CityArmyReinforcementService.ApprovedTarget(')) {
    throw 'StandingArmyService must project ordinary targets through the city service.'
}

if (-not $controller.Contains('CityArmyReinforcementService.ApprovedTarget(')) {
    throw 'RTS mission targets must clamp persisted strength through the city service.'
}

foreach ($token in @(
    'CityArmyReinforcementService.ApprovedTarget(',
    'CityArmyReinforcementRules.Shortage(')) {
    if (-not $levy.Contains($token)) {
        throw "directed levy demand must use approved city shortage: $token"
    }
}
