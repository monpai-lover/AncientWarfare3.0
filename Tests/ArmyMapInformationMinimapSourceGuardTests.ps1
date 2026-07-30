$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$patch = [IO.File]::ReadAllText((Join-Path $root `
    'Code/patch/AW_ArmyMapInformationMinimapPatch.cs'))
$service = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/presentation/ArmyMapInformationService.cs'))
$rules = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/lineage/ArmyMapInformationRules.cs'))
$models = [IO.File]::ReadAllText((Join-Path $root `
    'Code/api/multiplayer/AW3MultiplayerStrategicStateModels.cs'))
$coordinator = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/multiplayer/AW3MultiplayerStrategicStateCoordinator.cs'))
$keys = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/lineage/LineageKeys.cs'))
$locale = [IO.File]::ReadAllText((Join-Path $root `
    'Locales/aw3_army_rts.csv'))

if ($patch.Contains('drawKings')) {
    throw 'Army information must extend the original army flag, not draw a second king/minimap marker.'
}
if (-not $patch.Contains('drawArmies')) {
    throw 'Army information must hook the original army-flag renderer.'
}
if ($patch.Contains('.getNext()')) {
    throw 'Army information must not allocate another QuantumSprite flag.'
}
if (-not $patch.Contains('getAll()') -or
    -not $patch.Contains('QuantumSpriteWithText')) {
    throw 'Army information must reuse the native army flag and its text child.'
}
if (-not $patch.Contains(
        'ArmyMapInformationService.TryPopulateNativeFlagText')) {
    throw 'Army flag rendering must use the shared RTS information formatter.'
}
if (-not $service.Contains('ResolvePendingState(')) {
    throw 'Missionless armies must derive a visible pending state.'
}
if (-not $service.Contains('PendingOperationLocalizationKey(')) {
    throw 'Missionless army text must expose a localized pending operation.'
}
if ($service.Contains(
        '!ArmyRtsControllerService.TryGetProjection(pArmy,') -and
    $service.Contains(
        '!ArmyRtsControllerService.TryGetMission(pArmy,')) {
    throw 'Army information must not disappear solely because RTS publication is pending.'
}
if (-not $locale.Contains('aw_army_rts_state_awaiting_orders,')) {
    throw 'Army information needs an awaiting-orders localization key.'
}
if (-not $rules.Contains('ComposeManpowerText(')) {
    throw 'Army information must keep replenishment shortage and reserve supply distinct.'
}
if (-not $models.Contains('public int ReplenishmentShortage { get; }') -or
    -not $models.Contains('public int KingdomReserveAvailable { get; }')) {
    throw 'Strategic Army projections must replicate both manpower read values.'
}
if (-not $coordinator.Contains('CityReservePoolService.CountAvailable(')) {
    throw 'Authoritative strategic capture must publish indexed reserve supply.'
}
if (-not $keys.Contains('AW_ARMY_PROJECTED_REPLENISHMENT_SHORTAGE') -or
    -not $keys.Contains('AW_ARMY_PROJECTED_KINGDOM_RESERVE_AVAILABLE')) {
    throw 'Replica manpower values need dedicated Army read keys.'
}
if (-not $locale.Contains('aw_army_replenishment_shortage,') -or
    -not $locale.Contains('aw_army_reserve_supply,')) {
    throw 'Both Army manpower labels must be localized.'
}

Write-Output 'Army flag information source guards passed.'
