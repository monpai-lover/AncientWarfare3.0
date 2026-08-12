$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code/core/schools/SchoolMembershipService.cs')

if ($source -match 'MembershipWriteEpochs|BeginMembershipWrite|IsMembershipWriteCurrent') {
    throw 'School membership joins must not use the post-v1.0 actor epoch gate.'
}
if ($source -match 'MembershipCompensationWriteOperation|TryQueueCompensation') {
    throw 'School membership joins must not be closed again by post-commit compensation.'
}
if ($source -match '(?s)MembershipWriteOperation\s*:\s*IHistoricalSchoolWriteOperation\s*,\s*IHistoricalSchoolAsyncWriteOperation') {
    throw 'School membership joins must stay on the v1.0 buffered main-thread commit path.'
}
if ($source -match 'MembershipJoinBackgroundWrite|MembershipConversionBackgroundWrite|DetachBackgroundWrite\(\)') {
    throw 'School membership joins must not detach actor membership projection from its buffered operation.'
}
if ($source -notmatch '(?s)public void AfterCommit.*HistoricalSchoolStore\.InvalidateTeachingCommit\(Event\.CityId\).*if \(!Adopt\(\)\).*PendingMembershipActors\.Remove\(ActorId\).*Completion\?\.Invoke\(true\)') {
    throw 'Committed memberships must adopt into the runtime index before reporting v1.0 success.'
}
if ($source -notmatch '(?s)public void OnCleanFailure\(\).*PendingMembershipActors\.Remove\(ActorId\).*Completion\?\.Invoke\(false\)') {
    throw 'Failed buffered memberships must release their pending actor and report failure.'
}

Write-Output 'Historical school v1.0 membership source guard passed.'
