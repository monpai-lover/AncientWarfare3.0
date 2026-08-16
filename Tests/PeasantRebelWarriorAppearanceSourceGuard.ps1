$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$rulesPath = Join-Path $root 'Code/core/presentation/ActorVisualRoleRules.cs'
$providerPath = Join-Path $root 'Code/core/presentation/PeasantRebelVisualRoleProvider.cs'
$appearancePath = Join-Path $root 'Code/core/presentation/PeasantRebelAppearanceService.cs'
$modPath = Join-Path $root 'Code/ModClass.cs'
$routePath = Join-Path $root 'Code/core/lineage/PeasantRebelRouteService.cs'

foreach ($path in @($providerPath, $appearancePath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Peasant rebel appearance file is missing: $path"
    }
}

$rules = Get-Content -Raw -Encoding UTF8 $rulesPath
$provider = Get-Content -Raw -Encoding UTF8 $providerPath
$appearance = Get-Content -Raw -Encoding UTF8 $appearancePath
$mod = Get-Content -Raw -Encoding UTF8 $modPath
$route = Get-Content -Raw -Encoding UTF8 $routePath

foreach ($token in @('ResolvePeasantRebelRole(',
        'ActorVisualRole.Warrior')) {
    if (-not $rules.Contains($token)) {
        throw "Peasant rebel visual rule is missing $token"
    }
}
foreach ($token in @('IActorVisualRoleProvider',
        'MandateRebelService.IsRebelKingdom(',
        'LineageKeys.KINGDOM_HEIR_ID',
        'ResolvePeasantRebelRole(')) {
    if (-not $provider.Contains($token)) {
        throw "Peasant rebel visual provider is missing $token"
    }
}
foreach ($token in @('ActorVisualRoleResolver.Register(',
        'new PeasantRebelVisualRoleProvider()',
        'OnProjectionChanged(', 'clearGraphicsFully()')) {
    if (-not $appearance.Contains($token)) {
        throw "Peasant rebel appearance service is missing $token"
    }
}
if (-not $mod.Contains('PeasantRebelAppearanceService.Initialize();')) {
    throw 'Peasant rebel appearance provider is not initialized at mod load'
}
if (-not $route.Contains(
        'PeasantRebelAppearanceService.OnProjectionChanged(')) {
    throw 'Peasant rebel route transitions do not invalidate cached graphics'
}

foreach ($token in @('setProfession(', 'joinKingdom(', 'addTrait(',
        'removeTrait(', 'army =', 'SQLite')) {
    if (($provider + $appearance).Contains($token)) {
        throw "Presentation layer contains forbidden gameplay mutation: $token"
    }
}

Write-Output 'Peasant rebel warrior appearance source guard passed.'
