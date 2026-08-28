$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $repoRoot 'Code/core/lineage/WarRefugeeService.cs'
$source = [System.IO.File]::ReadAllText($servicePath).Replace("`r`n", "`n")

# The refugee system replaced its self-built journey/member/origin tables and
# the monthly journey state machine with a single vanilla joinCity plus native
# behaviour. The previous assertion (OnActorJoinedCity must gate on the journey
# key before touching the database) died together with the database access.
# These three checks guard what the new design can actually regress on.

# 1. Settlement must go through vanilla joinCity, which already performs
#    joinKingdom and the population bookkeeping.
if ($source -notmatch 'actor\.joinCity\(pDestination\)') {
    throw 'War refugee settlement must go through vanilla Actor.joinCity.'
}

# 2. The authority cycle must not regain a monthly journey pipeline; that was
#    the source of the 22-60ms per month.
foreach ($banned in @(
        'ProcessPersistedJourneys',
        'LoadActiveJourneys',
        'WarRefugeePersistence')) {
    if ($source.Contains($banned)) {
        throw ('War refugee service must not reintroduce the persisted ' +
            'journey pipeline: ' + $banned)
    }
}

# 3. Assimilation must stay event driven (resolved once when the war ends),
#    never a monthly scan.
if ($source -notmatch 'internal static void OnWarEnded\(') {
    throw 'War refugee assimilation must be resolved from the war-end event.'
}

$warPatchPath = Join-Path $repoRoot 'Code/patch/AW_WarPatch.cs'
$warPatch = [System.IO.File]::ReadAllText($warPatchPath).Replace("`r`n", "`n")
if ($warPatch -notmatch 'WarRefugeeService\.OnWarEnded\(pWar\)') {
    throw 'WarManager.endWar must invoke the refugee return/assimilation pass.'
}

Write-Host 'War refugee joinCity hot-path source guard passed.'
