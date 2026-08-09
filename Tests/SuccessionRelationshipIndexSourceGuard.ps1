$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
function Read-Source([string] $relative) {
    $path = Join-Path $projectRoot $relative
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing source: $relative"
    }
    return Get-Content -Raw -Encoding UTF8 -LiteralPath $path
}

$runtime = Read-Source `
    'Code/core/lineage/SuccessionRelationshipIndex.cs'
$authority = Read-Source `
    'Code/core/performance/AWAuthorityCycleService.cs'
$birth = Read-Source 'Code/patch/AW_BirthPatch.cs'
$death = Read-Source 'Code/patch/AW_ActorDeathPatch.cs'
$candidate = Read-Source `
    'Code/core/lineage/InheritanceCandidateService.cs'
$heir = Read-Source 'Code/core/lineage/HeirService.cs'

foreach ($required in @('RebuildActorsPerCycle = 128',
        'internal static void ProcessAuthorityCycle()',
        'internal static void OnBorn(',
        'internal static void OnDying(',
        'internal static void Reset()')) {
    if (-not $runtime.Contains($required)) {
        throw "Succession index runtime is missing: $required"
    }
}

$gate = $authority.IndexOf(
    'if (!pGate.TryEnter(pCycleToken, allowed)) return;',
    [StringComparison]::Ordinal)
$process = $authority.IndexOf(
    'SuccessionRelationshipIndex.ProcessAuthorityCycle();',
    [StringComparison]::Ordinal)
if ($gate -lt 0 -or $process -le $gate) {
    throw 'Succession index rebuild must remain behind the authority gate.'
}
if (-not $authority.Contains('SuccessionRelationshipIndex.Reset();')) {
    throw 'Authority reset must clear the succession index.'
}
if (-not $birth.Contains('SuccessionRelationshipIndex.OnBorn(')) {
    throw 'Birth completion must update the succession index.'
}
if (-not $death.Contains('SuccessionRelationshipIndex.OnDying(__instance)')) {
    throw 'Actor death must remove succession index membership.'
}
if ($candidate.Contains(
        'LineageQuery.GetLivingLineageMemberIds') -or
    $candidate.Contains('LineageQuery.GetLivingShiMemberIds')) {
    throw 'Succession candidate pools must not query living members via SQLite.'
}
foreach ($forbidden in @('LineageQuery.GetChildIds',
        'LineageQuery.GetParentIds',
        'LineageQuery.NearestCommonAgnaticAncestor',
        'LineageQuery.IsAgnaticDescendant')) {
    if ($heir.Contains($forbidden)) {
        throw "Heir selection still bypasses the runtime index: $forbidden"
    }
}

Write-Host 'Succession relationship index source guard passed.'
