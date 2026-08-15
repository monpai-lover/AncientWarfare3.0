$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$raidPath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditRaidService.cs'
$statePath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditStrongholdState.cs'
$storePath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditStateStore.cs'
$routePath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditRoute.cs'
$routeServicePath = Join-Path $root 'Code/core/lineage/PeasantRebelRouteService.cs'

$raid = Get-Content -Raw -Encoding UTF8 $raidPath
$state = Get-Content -Raw -Encoding UTF8 $statePath
$store = Get-Content -Raw -Encoding UTF8 $storePath
$route = Get-Content -Raw -Encoding UTF8 $routePath
$routeService = Get-Content -Raw -Encoding UTF8 $routeServicePath

foreach ($token in @('CarriedFoodByResourceId',
        'SuppressionExpiryByKingdomId')) {
    if ($state -notmatch [regex]::Escape($token)) {
        throw "Persistent raid custody is missing $token"
    }
}
if ($store -notmatch 'CarriedFoodByResourceId\s*\?\?=') {
    throw 'Legacy state normalization does not initialize food cargo'
}

foreach ($token in @(
        'getTotalResourceSlots(', 'ResType.Food',
        'ResType.Ingredient_Food', '.takeResource(',
        '.addResourcesToRandomStockpile(', 'CarriedFoodByResourceId',
        'PeasantRebelBanditRaidRules.StealableFood',
        'SuppressionExpiryByKingdomId', 'RestoreObservedInventory(',
        'PruneSuppressionRights(')) {
    if ($raid -notmatch [regex]::Escape($token)) {
        throw "Bandit raid settlement is missing $token"
    }
}
if ($raid -notmatch
    'PeasantRebelBanditRaidRules\s*\.\s*SuppressionExpiryYear') {
    throw 'Bandit raid settlement does not compute suppression expiry'
}

$takeIndex = $raid.IndexOf('.takeResource(')
$lootedIndex = $raid.IndexOf('Stage = BanditRaidStage.Looted', $takeIndex)
$lootWriteIndex = $raid.IndexOf('PeasantRebelBanditStateStore.Write',
    $lootedIndex)
if ($takeIndex -lt 0 -or $lootedIndex -lt 0 -or $lootWriteIndex -lt 0) {
    throw 'Loot is not removed, placed in custody, and persisted'
}

$deliveryIndex = $raid.IndexOf('.addResourcesToRandomStockpile(')
$cooldownIndex = $raid.LastIndexOf('BanditRaidStage.Cooldown',
    $deliveryIndex)
if ($deliveryIndex -lt 0 -or $cooldownIndex -lt 0 -or
    $cooldownIndex -gt $deliveryIndex) {
    throw 'Delivery must durably clear custody before adding food once'
}
if ($raid -notmatch 'survivors\.Count\s*==\s*0[\s\S]{0,260}CarriedFood') {
    throw 'Total party loss does not explicitly discard carried food'
}

foreach ($token in @('OriginKingdomId', 'Date.getCurrentYear()',
        'SuppressionExpiryByKingdomId.TryGetValue',
        'currentYear < expiryYear')) {
    if ($route -notmatch [regex]::Escape($token)) {
        throw "Direct suppression permission is missing $token"
    }
}
foreach ($token in @('attackerHasSuppressionRight',
        'CanReceiveDirectWar(pDefender, pAttacker)',
        'attackerIsOrigin')) {
    if ($routeService -notmatch [regex]::Escape($token)) {
        throw "War permission integration is missing $token"
    }
}
if ($raid -match 'startWar\(|newWar\(') {
    throw 'A food raid must not automatically start a war'
}

Write-Output 'Bandit raid settlement source guard passed.'
