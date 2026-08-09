$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$patchPath = Join-Path $root 'Code\patch\AW_ActorVisualRolePatch.cs'
$providerPath = Join-Path $root 'Code\core\presentation\MilitaryGovernorateVisualRoleProvider.cs'
$storePath = Join-Path $root 'Code\core\lineage\MilitaryGovernorateStore.cs'
$appearancePath = Join-Path $root 'Code\core\presentation\MilitaryGovernorateAppearanceService.cs'
$successionPath = Join-Path $root 'Code\core\lineage\MilitaryGovernorateSuccessionService.cs'
$vassalPath = Join-Path $root 'Code\core\lineage\VassalService.cs'
$courtPath = Join-Path $root 'Code\core\court\CourtReadModelService.cs'
$courtViewPath = Join-Path $root 'Code\ui\items\CourtActorNodeView.cs'

if (-not (Test-Path -LiteralPath $patchPath)) {
    throw 'Actor visual-role presentation patch is missing.'
}

$patch = Get-Content -Raw -LiteralPath $patchPath
$provider = Get-Content -Raw -LiteralPath $providerPath
$store = Get-Content -Raw -LiteralPath $storePath
$appearance = Get-Content -Raw -LiteralPath $appearancePath
$succession = Get-Content -Raw -LiteralPath $successionPath
$vassal = Get-Content -Raw -LiteralPath $vassalPath
$court = Get-Content -Raw -LiteralPath $courtPath
$courtView = Get-Content -Raw -LiteralPath $courtViewPath

@(
    '[HarmonyPatch(typeof(Actor), "getUnitTexturePath")]'
    '[HarmonyPatch(typeof(Actor), "checkSpriteHead")]'
    '[HarmonyPatch(typeof(ActorAvatarData), "setData", typeof(Actor))]'
    '[HarmonyPriority(Priority.First)]'
    'ActorVisualRoleResolver.Resolve('
    'subspecies.getSkinWarrior()'
    'mutation_skin_asset?.skin_warrior'
    'pActor.asset.unit_zombie'
    'case ActorVisualRole.Civilian:'
    'case ActorVisualRole.Warrior:'
    'case ActorVisualRole.Leader:'
    'case ActorVisualRole.King:'
) | ForEach-Object {
    if (-not $patch.Contains($_)) {
        throw "Actor visual-role patch is missing required boundary: $_"
    }
}

@(
    'setProfession('
    'profession_asset ='
    'joinKingdom('
    'TryGetActive('
    'SQLite'
) | ForEach-Object {
    if ($patch.Contains($_) -or $provider.Contains($_)) {
        throw "Presentation hot path contains forbidden gameplay/storage operation: $_"
    }
}

if (-not $provider.Contains('TryGetRuntimeProjection(')) {
    throw 'Governorate provider must use the runtime projection.'
}
if (-not $store.Contains('public static bool TryGetRuntimeProjection(')) {
    throw 'Governorate store must expose an in-memory projection reader.'
}

@(
    'public static void OnProjectionChanged('
    'public static void OnGovernorChanged('
    'clearGraphicsFully()'
) | ForEach-Object {
    if (-not $appearance.Contains($_)) {
        throw "Appearance invalidation service is missing: $_"
    }
}
if (-not $store.Contains('MilitaryGovernorateAppearanceService.OnProjectionChanged(')) {
    throw 'Store projection transitions do not invalidate actor graphics.'
}
if (-not $store.Contains('MilitaryGovernorateAppearanceService.OnGovernorChanged(')) {
    throw 'Runtime restoration does not invalidate repaired governors.'
}
if (-not $succession.Contains('MilitaryGovernorateAppearanceService.OnGovernorChanged(') -or
    -not $succession.Contains('MilitaryGovernorateAppearanceService.OnProjectionChanged(')) {
    throw 'Succession transitions do not invalidate old and new role holders.'
}
if (-not $vassal.Contains('MilitaryGovernorateAppearanceService.OnProjectionChanged(')) {
    throw 'Vassal projection clear does not invalidate former role holders.'
}

@(
    'AddMilitaryGovernorates(seeds, pKingdom);'
    'VassalService.GetVassals(pKingdom)'
    'MilitaryGovernorateStore.GetDirectActive(pKingdom, 256)'
    '.OrderBy(p => p.id)'
    'MilitaryGovernorateCourtRules.IsSubjectActor('
    'CourtPyramidRoleId.MilitaryGovernorateGovernor'
    'CourtPyramidRoleId.MilitaryGovernorateSuccessor'
) | ForEach-Object {
    if (-not $court.Contains($_)) {
        throw "Suzerain court projection is missing: $_"
    }
}
if ($court.Contains('GetVassals(pKingdom, pRecursive: true)')) {
    throw 'Suzerain court must not include indirect governorates.'
}
if (-not $courtView.Contains('!IsMilitaryGovernorateCommandNode(pNode)')) {
    throw 'Foreign governorate nodes must not expose suzerain office actions.'
}

Write-Host 'Military governorate warrior appearance source guard passed.'
