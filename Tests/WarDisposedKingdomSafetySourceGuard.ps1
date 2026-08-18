$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$source = Get-Content -Raw (Join-Path $root 'Code/patch/AW_WarPatch.cs')
$service = Get-Content -Raw `
    (Join-Path $root 'Code/core/lineage/WarRosterIntegrityService.cs')
$warBoundary = $source + "`n" + $service
$extinction = Get-Content -Raw `
    (Join-Path $root 'Code/core/lineage/KingdomExtinctionQueue.cs')
$extinctionPatch = Get-Content -Raw `
    (Join-Path $root 'Code/patch/AW_KingdomExtinctionPatch.cs')

foreach ($required in @(
        '[HarmonyPatch(typeof(War), nameof(War.update))]',
        'RepairActiveWarRoster',
        'TryResolveLiveKingdom',
        'pWar.data.main_attacker =',
        'pWar.data.main_defender =',
        'World.world?.wars?.endWar')) {
    if (-not $warBoundary.Contains($required)) {
        throw "War disposed-kingdom safety boundary is missing: $required"
    }
}

$removeStart = $source.IndexOf('private static bool RemoveFromWar_Prefix(')
$removeEnd = $source.IndexOf('[HarmonyPostfix]', $removeStart)
if ($removeStart -lt 0 -or $removeEnd -le $removeStart) {
    throw 'War.removeFromWar prefix cannot be inspected.'
}
$remove = $source.Substring($removeStart, $removeEnd - $removeStart)
if ($remove -notmatch 'pKingdom\?\.data\s*==\s*null[\s\S]*RepairActiveWarRoster[\s\S]*return false') {
    throw 'War.removeFromWar still forwards a disposed kingdom into DiplomacyManager.getRelation.'
}

if ($source.Contains('Config.paused')) {
    throw 'War roster recovery must not pause the simulation.'
}

if ($extinction.Contains('manager.removeObject(kingdom)')) {
    throw 'AW extinction queue still disposes kingdoms outside vanilla checkMetaObjectsDestroy.'
}
if (-not $extinction.Contains('MarkVerifiedForVanillaRemoval') -or
    -not $extinction.Contains('TryDetachFromActiveWars') -or
    -not $extinctionPatch.Contains('IsVerifiedForVanillaRemoval')) {
    throw 'Zero-city extinction does not restore the vanilla removal boundary after war cleanup.'
}

Write-Host 'War disposed-kingdom safety source guard passed.'
