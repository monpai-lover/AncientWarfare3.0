$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$patchPath = Join-Path $repo 'Code/patch/AW_BabyNamePatch.cs'
if (-not [IO.File]::Exists($patchPath)) {
    throw 'Birth patch is missing.'
}

$source = [IO.File]::ReadAllText($patchPath)
if ($source -notmatch 'CreateBabyActorFromData_Postfix') {
    throw 'Birth affiliation must run at ActorManager.createBabyActorFromData completion.'
}
if (-not $source.Contains('[HarmonyPatch(typeof(ActorManager),') -or
    -not $source.Contains('nameof(ActorManager.createBabyActorFromData)')) {
    throw 'Birth affiliation patch must target createBabyActorFromData.'
}
if ($source -notmatch 'ActorBirthAffiliationService\.Reconcile\(\s*__result,\s*pCity\s*\)') {
    throw 'Birth affiliation patch must reconcile against the explicit birth city.'
}

$methodStart = $source.IndexOf(
    'public static void CreateBabyActorFromData_Postfix',
    [StringComparison]::Ordinal)
$makeBabyStart = $source.IndexOf(
    'public static void MakeBaby_Postfix',
    [StringComparison]::Ordinal)
if ($methodStart -lt 0 -or $makeBabyStart -lt 0 -or
    $methodStart -gt $makeBabyStart) {
    throw 'Explicit birth-city reconciliation must precede final makeBaby reconciliation.'
}

$servicePath = Join-Path $repo 'Code/core/lineage/ActorBirthAffiliationService.cs'
if (-not [IO.File]::Exists($servicePath)) {
    throw 'Birth affiliation service is missing.'
}
$service = [IO.File]::ReadAllText($servicePath)
if ($service -notmatch 'ResolveTargetCity\(') {
    throw 'Final birth reconciliation must resolve a preferred birth city.'
}
if ($service -notmatch 'pBaby\.city') {
    throw 'Final birth reconciliation must preserve the baby current city before parent fallback.'
}

Write-Output 'Actor birth affiliation source guard passed.'
