$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    $path = Join-Path $root $relativePath
    if (-not [IO.File]::Exists($path)) {
        $failures.Add("missing source file: $relativePath")
        return ''
    }
    return [IO.File]::ReadAllText($path)
}

function Require-Text([string]$source, [string]$needle, [string]$label) {
    if (-not $source.Contains($needle)) {
        $failures.Add("${label}: missing '$needle'")
    }
}

function Reject-Text([string]$source, [string]$needle, [string]$label) {
    if ($source.Contains($needle)) {
        $failures.Add("${label}: forbidden '$needle'")
    }
}

$query = Read-Source 'Code/core/court/CivilServiceWaitingPoolQuery.cs'
$rules = Read-Source 'Code/core/court/CivilServiceExamRules.cs'
$service = Read-Source 'Code/core/court/CivilServiceExamService.cs'
$window = Read-Source 'Code/ui/windows/CivilServiceExamWindow.cs'
$framePatches = (Get-ChildItem (Join-Path $root 'Code/patch') -Filter '*.cs' |
    ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"

foreach ($required in @(
        'TryLoadActorIds(',
        "S.STATUS='completed'",
        "C.QUALIFICATION IN ('gongshi','jinshi')",
        'A.IS_ALIVE=1',
        'A.SEX=0',
        "IFNULL(A.STATUS,'')<>@slave",
        'NOT EXISTS (SELECT 1 FROM ',
        'O.ACTOR_ID=C.ACTOR_ID AND O.ACTIVE=1',
        'C2.ACTOR_ID=C.ACTOR_ID',
        'S2.CYCLE_YEAR>S.CYCLE_YEAR',
        'GROUP BY C.ACTOR_ID',
        'LIMIT @limit')) {
    Require-Text $query $required "waiting query $required"
}
Require-Text $rules 'IsWaitingCandidate(' `
    'live waiting-candidate eligibility rule'
Require-Text $rules 'MinimumWaitingReserve = 4' 'reserve lower bound'
Require-Text $rules 'MaximumWaitingReserve = 32' 'reserve upper bound'

foreach ($forbidden in @('ExecuteNonQuery', 'INSERT ', 'UPDATE ', 'DELETE ')) {
    Reject-Text $query $forbidden 'waiting query remains read-only'
}
Reject-Text $query 'SELECT *' 'waiting query uses explicit projection'
Reject-Text $window 'CivilServiceWaitingPoolQuery' `
    'exam window cannot query waiting candidates'
Reject-Text $framePatches 'CivilServiceWaitingPoolQuery' `
    'per-frame patches cannot query waiting candidates'

foreach ($required in @(
        'private sealed class ExamDemandSnapshot',
        'TryResolveDemandSnapshot(',
        'CivilServiceWaitingPoolQuery.TryLoadActorIds(',
        'WaitingCandidateCount = demand.WaitingCandidateCount',
        'ReserveTarget = demand.ReserveTarget',
        'CivilServiceExamRules.ReserveTarget(establishedPosts)',
        'CivilServiceExamRules.FinalAdmissionQuota(',
        'pSession?.AdmissionQuota >= 0')) {
    Require-Text $service $required "authority demand snapshot $required"
}
Reject-Text $service 'pSession?.AdmissionQuota > 0' `
    'a frozen one-person quota remains authoritative'

if ($failures.Count -gt 0) {
    throw "Civil-service reserve-pool guard failures:`n - " +
        ($failures -join "`n - ")
}

Write-Output 'Civil-service reserve-pool source guards passed.'
