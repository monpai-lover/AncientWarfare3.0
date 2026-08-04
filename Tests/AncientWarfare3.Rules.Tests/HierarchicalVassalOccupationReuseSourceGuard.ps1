$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$patchPath = Join-Path $root `
    'Code\patch\AW_HierarchicalVassalOccupationPatch.cs'
$rulesPath = Join-Path $root `
    'Code\core\policy\HierarchicalVassalMapModeRules.cs'

if (-not (Test-Path $patchPath)) {
    throw 'Missing hierarchical vassal occupation compatibility patch.'
}

$patch = Get-Content -Raw $patchPath
$rules = Get-Content -Raw $rulesPath

if ($patch -notmatch
        'HarmonyPatch\s*\(\s*typeof\s*\(\s*Zones\s*\)\s*,\s*' +
        'nameof\s*\(\s*Zones\.showKingdomZones\s*\)\s*\)') {
    throw 'Occupation reuse patch must target Zones.showKingdomZones.'
}
if ($patch -notmatch '\[HarmonyPostfix\]') {
    throw 'Occupation reuse patch must use a Harmony Postfix.'
}
if ($patch -notmatch
        'if\s*\(\s*HierarchicalVassalMapModeService\s*\.\s*' +
        'IsActive\s*\(\s*\)\s*\)\s*(?:\{\s*)?' +
        '__result\s*=\s*true\s*;') {
    throw 'Active hierarchical map mode must force __result to true.'
}
if ($patch -notmatch 'ref\s+bool\s+__result') {
    throw 'Occupation reuse postfix must receive ref bool __result.'
}
if (-not $rules.Contains('pAssetId == "capturing_zones"')) {
    throw 'capturing_zones must remain in the minimap quantum asset whitelist.'
}

$forbidden = @(
    'World.world.cities',
    'CapturingZonesCalculator',
    'drawCapturingZones',
    'QuantumSpriteLibrary',
    'Nameplate'
)
foreach ($needle in $forbidden) {
    if ($patch.Contains($needle)) {
        throw "Occupation reuse patch must not reference $needle."
    }
}

if ($patch -match '\[Harmony(?:Prefix|Transpiler|Finalizer)\]') {
    throw 'Occupation reuse patch must contain only a Harmony Postfix.'
}

Write-Output 'Hierarchical vassal occupation reuse source guard passed.'
