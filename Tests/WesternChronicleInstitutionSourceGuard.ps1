$root = Split-Path -Parent $PSScriptRoot
$institution = Get-Content -Raw (Join-Path $root 'Code\core\court\CourtInstitutionService.cs')
$chronicle = Get-Content -Raw (Join-Path $root 'Code\core\lineage\ChronicleEvents.cs')
$policy = Get-Content -Raw (Join-Path $root 'Code\core\policy\KingdomPolicyService.cs')
$court = Get-Content -Raw (Join-Path $root 'Code\core\court\CourtService.cs')

if ($institution -notmatch 'pRecordHistory && previous != next') {
    throw 'same-rank western migration is not recorded'
}
if ($chronicle -notmatch 'pPrevious == pNext') {
    throw 'institution chronicle event still only accepts rank upgrades'
}
if ($policy -notmatch 'RecordWesternInstitutionTransition\(pKingdom\)') {
    throw 'completed western policy does not refresh institution history'
}
if ($court -notmatch 'CourtInstitutionService\.Refresh\(pKingdom, pRecordHistory: true\)') {
    throw 'legacy western institution migration is not recovered annually'
}

Write-Host 'Western chronicle institution source guard passed.'
