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

# Threading contract. Vanilla BuildingManager.precalculateRenderDataParallel
# calls Building.calculateMainSprite inside Parallel.For, so this Postfix runs
# on worker threads. It used to write a plain static Dictionary from there
# (observed tearing into a NullReferenceException inside Dictionary.TryInsert,
# which stopped the scheduler and paused the game) and it also called
# Resources.LoadAll and texture creation off the main thread.
$threadRequired = @(
    'private static volatile Dictionary<string, CapitalSpriteCatalog>',
    'HarmonyPatch(typeof(BuildingManager),',
    '"precalculateRenderDataParallel"'
)
foreach ($fragment in $threadRequired) {
    if (-not $source.Contains($fragment)) {
        throw "Mandate capital sprite patch lost its threading contract: $fragment"
    }
}

$lookupStart = $source.IndexOf('private static bool TryGetCatalog(')
$lookupEnd = $source.IndexOf('private static void DrainPendingCatalogs(',
    $lookupStart)
if ($lookupStart -lt 0 -or $lookupEnd -le $lookupStart) {
    throw 'Mandate capital catalog lookup boundary cannot be located.'
}
$lookup = $source.Substring($lookupStart, $lookupEnd - $lookupStart)
foreach ($forbidden in @(
    'SpriteTextureLoader.getSpriteList',
    'new CapitalSpriteCatalog(',
    'DynamicSpriteCreator.'
)) {
    if ($lookup.Contains($forbidden)) {
        throw "Worker-thread catalog lookup must not call $forbidden; it is main-thread only."
    }
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
