$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$path = Join-Path $repo 'Code/core/performance/AWDirtyMetaActorIndex.cs'
if (-not [IO.File]::Exists($path)) {
    throw 'Dirty meta actor index source is missing.'
}

$source = [IO.File]::ReadAllText($path)
if ($source -notmatch 'Kingdom kingdom = dying\[i\]\?\.kingdom' -or
    $source -notmatch 'kingdom\?\.data == null \|\| kingdom\.isRekt\(\)') {
    throw 'Invalid kingdoms in the quarantine partition must be skipped before preserveAlive.'
}
if ($source -notmatch 'pActor\?\.data == null \|\| pActor\.asset == null' -or
    $source -notmatch 'kingdom\?\.data == null \|\| kingdom\.isRekt\(\)') {
    throw 'Invalid actors and kingdoms must be skipped before rebuilding kingdom units.'
}
if ($source -notmatch 'if \(pActor\?\.data == null\) continue;' -or
    $source -notmatch 'pActor\.asset != null && pActor\.asset\.is_boat') {
    throw 'Large-scheduler actor classification must tolerate sparse actor and asset entries.'
}

Write-Output 'Actor meta null-kingdom source guard passed.'
