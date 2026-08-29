$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$patchPath = Join-Path $root 'Code\patch\AW_MandateCapitalBuildingSpritePatch.cs'
$resourcePath = Join-Path $root 'GameResources\buildings\civ_main\Xia_MandateCapital'

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

Write-Output 'Mandate capital building texture source guard passed.'
