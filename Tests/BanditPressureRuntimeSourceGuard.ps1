$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
function Read-Source([string] $relative) {
    $path = Join-Path $repoRoot $relative
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing source file: $relative"
    }
    return Get-Content -Raw -Encoding UTF8 -LiteralPath $path
}

$rules = Read-Source 'Code/core/lineage/PeasantRebelBanditPressureRules.cs'
$state = Read-Source 'Code/core/lineage/PeasantRebelBanditStrongholdState.cs'
$store = Read-Source 'Code/core/lineage/PeasantRebelBanditStateStore.cs'
$stronghold = Read-Source 'Code/core/lineage/PeasantRebelBanditStrongholdService.cs'
$route = Read-Source 'Code/core/lineage/PeasantRebelBanditRoute.cs'
$loyalty = Read-Source 'Code/content/WarLoyaltyContent.cs'
$locale = Read-Source 'Locales/aw3_mandate_extra.csv'
$service = Read-Source 'Code/core/lineage/PeasantRebelBanditPressureService.cs'

foreach ($token in @('AnnualPressure = 6', 'MaximumPressure = 300',
        'ActiveTargetLoyaltyPenalty = -25')) {
    if (-not $rules.Contains($token)) { throw "Pressure rules missing $token" }
}
foreach ($token in @('CurrentSchemaVersion = 5',
        'PressureTargetCityId', 'Pressure = 0', 'LastPressureYear')) {
    if (-not $state.Contains($token)) { throw "Pressure state missing $token" }
}
foreach ($token in @('MaximumPressure', 'PressureTargetCityId',
        'LastPressureYear')) {
    if (-not ($store + $stronghold).Contains($token)) {
        throw "Pressure state lifecycle missing $token"
    }
}
foreach ($token in @('aw_bandit_pressure',
        'aw_loyalty_bandit_pressure',
        'CalculateBanditPressurePenalty')) {
    if (-not $loyalty.Contains($token)) {
        throw "Bandit loyalty asset missing $token"
    }
}
foreach ($token in @('aw_loyalty_bandit_pressure',
        'aw_hist_bandit_pressure_annexed',
        'aw_hist_bandit_revolution_started')) {
    if (-not $locale.Contains($token)) {
        throw "Bandit pressure localization missing $token"
    }
}

$yearStart = $route.IndexOf('public void OnKingdomYear(')
$yearEnd = $route.IndexOf('public bool CanDeclareWar(', $yearStart)
if ($yearStart -lt 0 -or $yearEnd -lt 0) {
    throw 'Cannot isolate bandit yearly route'
}
$yearBody = $route.Substring($yearStart, $yearEnd - $yearStart)
if (-not $yearBody.Contains(
        'PeasantRebelBanditPressureService.OnKingdomYear(')) {
    throw 'Bandit yearly route does not run pressure progression'
}
if ($yearBody.Contains('TryConvertToFounding(')) {
    throw 'Bandit yearly route still invokes early random conversion'
}

foreach ($token in @('DeferredRuntimeWorkService.EnqueueCoalesced(',
        'DeferredWorkClass.CriticalRuntime', 'bandit_pressure_annex:',
        'joinAnotherKingdom(', 'pRebellion: true',
        'PeasantRebelRouteService.RealmStrength(',
        'PeasantRebelRouteService.ConvertBanditToFounding(')) {
    if (-not $service.Contains($token)) {
        throw "Bandit pressure runtime missing $token"
    }
}
foreach ($token in @('QueueOrphanCleanup(', 'RemoveStrongholdTowers(',
        'RestoreWalls(')) {
    if (-not $stronghold.Contains($token)) {
        throw "Bandit orphan cleanup missing $token"
    }
}

Write-Output 'Bandit pressure runtime source guard passed.'
