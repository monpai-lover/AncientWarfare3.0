$ErrorActionPreference = 'Stop'

function Require([string]$Text, [string]$Needle, [string]$Message) {
    if (-not $Text.Contains($Needle)) { throw $Message }
}

$mandate = Get-Content -Raw 'Code/core/lineage/MandateRebelService.cs'
$route = Get-Content -Raw 'Code/core/lineage/PeasantRebelRouteService.cs'
$bandit = if (Test-Path 'Code/core/lineage/PeasantRebelBanditRoute.cs') {
    Get-Content -Raw 'Code/core/lineage/PeasantRebelBanditRoute.cs'
} else {
    ''
}

Require $mandate 'PeasantRebelRouteService.InitializeAndEnter(' `
    'CreateRebelKingdom must dispatch through the route coordinator.'
Require $mandate 'EnterFoundingRoute(' `
    'The existing founding flow must have a dedicated adapter.'
Require $mandate 'TryPullAlignedCities(pRebel, pOriginKingdom, pFoundingCity);' `
    'Aligned-city recruitment must remain behind EnterFoundingRoute.'
Require $mandate 'StartRebelWar(pOriginKingdom, pRebel);' `
    'The existing rebellion war must remain behind EnterFoundingRoute.'
Require $route 'generateName(MetaType.Kingdom' `
    'Route initialization must use the original kingdom name generator.'
Require $bandit 'World.world.wars.endWar(war, WarWinner.Peace)' `
    'Bandit entry must end active wars through the original war manager.'

Write-Host 'Peasant rebel route runtime source guard passed.'
