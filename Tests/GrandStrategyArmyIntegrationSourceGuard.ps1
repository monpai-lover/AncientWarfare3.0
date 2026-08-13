$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$modePatch = Get-Content -Raw (Join-Path $root 'Code\patch\AW_GrandStrategyArmyModePatch.cs')
$pathFiles = Get-ChildItem (Join-Path $root 'Code\core\grandstrategy') -Filter '*.cs' |
    ForEach-Object { Get-Content -Raw $_.FullName }
$pathText = $pathFiles -join "`n"

foreach ($required in @(
    'HarmonyPatch(typeof(City), "updateCapture")',
    'BehCityActorCheckAttack',
    'CityBehCheckAttackZone',
    'DecisionHelper.makeDecisionFor',
    'RoyalGuardService.IsRoyalGuard(actor)',
    'GrandStrategyRuntimeHost.Active')) {
    if (-not $modePatch.Contains($required)) {
        throw "Missing grand strategy compatibility boundary: $required"
    }
}
foreach ($forbidden in @('getCaptain(', '.goTo(', 'City.updateCapture(',
    'finishCapture(')) {
    if ($pathText.Contains($forbidden)) {
        throw "Grand strategy authority depends on forbidden native API: $forbidden"
    }
}
Write-Output 'Grand strategy integration source guard passed.'
