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

if ($authority.Contains('SuccessionRelationshipIndex.ProcessAuthorityCycle')) {
    throw 'Database-backed succession queries must not rebuild an unused runtime index each authority cycle.'
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
# AW genealogy includes archived relationships absent from vanilla parent fields.
# Keep the runtime index lifecycle, but never use it to reject those successors.
foreach ($required in @('LineageQuery.GetLivingLineageMemberIds',
        'LineageQuery.GetLivingShiMemberIds',
        'InheritanceCandidateRules.MaximumArchiveIds', 'AtLimit(pResult)')) {
    if (-not $candidate.Contains($required)) {
        throw "Bounded AW succession candidate discovery is missing: $required"
    }
}
if ($candidate.Contains('SuccessionRelationshipIndex.') -or
    $heir.Contains('SuccessionRelationshipIndex.')) {
    throw 'AW succession must not substitute vanilla parent fields for archived genealogy.'
}
foreach ($required in @('LineageQuery.GetChildIds',
        'LineageQuery.GetParentIds',
        'LineageQuery.AgnaticAncestorDepths', 'kingAncestry.NearestCommon',
        'LineageQuery.IsAgnaticDescendant')) {
    if (-not $heir.Contains($required)) {
        throw "Heir selection must use AW genealogy: $required"
    }
}
$query = Read-Source 'Code/core/lineage/LineageQuery.cs'
foreach ($method in @('GetLivingLineageMemberIds', 'GetLivingShiMemberIds')) {
    $start = $query.IndexOf("public static List<long> $method(",
        [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing bounded genealogy query: $method" }
    $end = $query.IndexOf("`n        }", $start, [StringComparison]::Ordinal)
    if ($end -le $start) { throw "Missing query boundary: $method" }
    $body = $query.Substring($start, $end - $start)
    foreach ($required in @('IS_ALIVE=1',
            'ORDER BY BIRTH_TIME ASC,ID ASC LIMIT @limit',
            'cmd.Parameters.AddWithValue("@limit", pLimit)')) {
        if (-not $body.Contains($required)) {
            throw "Genealogy query $method lost its bounded alive-member contract: $required"
        }
    }
}

Write-Host 'Succession relationship index source guard passed.'
