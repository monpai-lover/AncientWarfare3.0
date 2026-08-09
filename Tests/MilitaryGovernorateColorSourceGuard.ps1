$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Require([string]$source, [string]$token, [string]$message) {
    if (-not $source.Contains($token)) { throw $message }
}

function Forbid([string]$source, [string]$token, [string]$message) {
    if ($source.Contains($token)) { throw $message }
}

$servicePath = Join-Path $root `
    'Code\core\lineage\MilitaryGovernorateColorService.cs'
if (-not (Test-Path -LiteralPath $servicePath)) {
    throw 'Missing MilitaryGovernorateColorService.'
}
$service = Get-Content -Raw -LiteralPath $servicePath
$patch = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\patch\AW_KingdomColorPatch.cs')
$creation = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\MilitaryGovernorateCreationService.cs')
$vassal = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\VassalService.cs')
$store = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\MilitaryGovernorateStore.cs')

foreach ($token in @(
    'MilitaryGovernorateStore.GetDirectActive(',
    'MilitaryGovernorateRules.ShouldSynchronizeColor(',
    'VassalService.GetSuzerain(pSubject) == pSuzerain',
    'VassalService.GetSubjectKind(pSubject)',
    'pSubject.updateColor(pSuzerain.getColor())',
    'MaximumSynchronizedChildren'
)) {
    Require $service $token "Missing event-driven color token: $token"
}
Require $patch '[HarmonyPatch(typeof(Kingdom), nameof(Kingdom.updateColor))]' `
    'Original Kingdom.updateColor hook is missing.'
Require $patch 'MilitaryGovernorateColorService.OnSuzerainColorChanged(' `
    'Kingdom color change does not notify military governorates.'
Require $creation 'MilitaryGovernorateColorService.CopyFromSuzerain(' `
    'Creation does not copy the suzerain color once.'
Require $vassal 'MilitaryGovernorateStore.TryEndWithRelation(' `
    'Military-governorate relation and state are not ended atomically.'
Require $store 'TryEndWithRelation(' `
    'Military governorate store lacks an atomic relation-end operation.'
Require $store 'BeginTransaction(IsolationLevel.Serializable)' `
    'Military governorate relation end does not use a SQLite transaction.'
$atomicEnd = [regex]::Match($store,
    'public static bool TryEndWithRelation[\s\S]*?' +
    'public static bool RestoreProjection').Value
if ([string]::IsNullOrEmpty($atomicEnd)) {
    throw 'Cannot inspect atomic military-governorate relation end.'
}
Forbid $atomicEnd 'ClearProjection(' `
    'The persistence transaction must not mutate runtime projections.'
Require $vassal 'ShouldRandomizeIndependentColor(pReason)' `
    'Independent recoloring does not use the canonical end-reason rule.'
Require $vassal '"independence_war"' `
    'Successful independence is not detected at relation end.'
Require $vassal 'KingdomVisualRandomizationService.RerollNewCivVisuals(' `
    'Successful independence does not restore native independent colors.'

foreach ($token in @('Update(', 'OnKingdomYear(', 'World.world.kingdoms')) {
    Forbid $service $token `
        "Military governorate color synchronization must not poll: $token"
}

Write-Output 'Military governorate color source guard passed.'
