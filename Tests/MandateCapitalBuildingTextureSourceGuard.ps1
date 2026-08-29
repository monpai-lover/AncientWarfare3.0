$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$patchPath = Join-Path $root 'Code\patch\AW_MandateCapitalBuildingSpritePatch.cs'
$resourcePath = Join-Path $root 'GameResources\buildings\civ_main\Xia_MandateCapital'
$ordinaryDocksPath = Join-Path $root 'GameResources\buildings\civ_main\Xia\docks_Xia'
$capitalDocksPath = Join-Path $resourcePath 'docks_Xia'

if (-not (Test-Path -LiteralPath $patchPath)) {
    throw 'Mandate capital building sprite patch is missing.'
}
if (-not (Test-Path -LiteralPath $resourcePath)) {
    throw 'Mandate capital building texture resources are missing.'
}

$source = Get-Content -Raw -Encoding UTF8 $patchPath
$required = @(
    'HarmonyPatch(typeof(Building), nameof(Building.calculateMainSprite))',
    'MandateCapitalBuildingTextureRules.ShouldUseCapitalTexture',
    'MandateService.IsRuntimeMandateKingdom',
    'kingdom.capital == city',
    'buildings/civ_main/Xia_MandateCapital/',
    'SpriteTextureLoader.getSpriteList'
)
foreach ($fragment in $required) {
    if (-not $source.Contains($fragment)) {
        throw "Mandate capital building sprite patch is missing: $fragment"
    }
}
if ($source.Contains('asset.main_path =')) {
    throw 'Mandate capital textures must not mutate the shared BuildingAsset path.'
}

$expectedDocksMainSprites = @(
    'main_0.png', 'main_0_0.png', 'main_0_1.png', 'main_0_2.png',
    'main_0_3.png', 'main_0_4.png', 'main_0_5.png', 'main_0_6.png',
    'main_0_7.png'
)
$actualDocksMainSprites = @(
    Get-ChildItem -File -LiteralPath $ordinaryDocksPath -Filter 'main_*.png' |
        Sort-Object Name | ForEach-Object Name
)
if (($actualDocksMainSprites -join '|') -ne
    (($expectedDocksMainSprites | Sort-Object) -join '|')) {
    throw 'Xia docks must contain only the legacy main_0 animation series.'
}
if (Test-Path -LiteralPath $capitalDocksPath) {
    throw 'Mandate capitals must reuse the ordinary legacy Xia docks sprites.'
}

Write-Output 'Mandate capital building texture source guard passed.'
