$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$catalogPath = Join-Path $root 'Code\content\CivMonkeyTextureCatalog.cs'
$namingPath = Join-Path $root 'Code\content\CivMonkeyNamingContent.cs'
$failures = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path $catalogPath)) {
    $failures.Add('missing civ_monkey texture catalog repair')
} else {
    $catalog = [IO.File]::ReadAllText($catalogPath)
    foreach ($needle in @(
        'internal static class CivMonkeyTextureCatalog',
        'pAsset.id != CivMonkeyNamingRules.ActorAssetId',
        'pAsset.skin_citizen_male = Repeat("male_1", slotCount);',
        'pAsset.skin_citizen_female = Repeat("female_1", slotCount);',
        'pAsset.skin_warrior = Repeat("warrior_1", slotCount);',
        'internal static bool TryGetRuntimeTexturePath(Actor pActor, out string pTexture)',
        'pActor.isEgg() || pActor.isBaby() || pActor.isKing() || pActor.isCityLeader()',
        'pActor.isWarrior()',
        '? "warrior_1"',
        'private static string[] Repeat(string pSkin, int pCount)')) {
        if (-not $catalog.Contains($needle)) {
            $failures.Add("texture catalog repair missing: $needle")
        }
    }
}

if (-not (Test-Path $namingPath) -or
    -not ([IO.File]::ReadAllText($namingPath).Contains('CivMonkeyTextureCatalog.Repair(actor);'))) {
    $failures.Add('civ_monkey texture catalog repair is not initialized with naming content')
}

$texturePatchPath = Join-Path $root 'Code\content\XiaTexturePatch.cs'
if (-not (Test-Path $texturePatchPath) -or
    -not ([IO.File]::ReadAllText($texturePatchPath).Contains(
        'CivMonkeyTextureCatalog.TryGetRuntimeTexturePath(__instance, out string monkeyTexture)'))) {
    $failures.Add('runtime civ_monkey texture path fallback is not installed')
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'CivMonkey texture catalog source guard passed.'
