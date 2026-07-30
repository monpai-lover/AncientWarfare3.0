$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    $path = Join-Path $root $relativePath
    if (-not [IO.File]::Exists($path)) {
        $failures.Add("missing source file $relativePath")
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

$court = Read-Source 'Code/core/court/CourtService.cs'
$guest = Read-Source 'Code/core/schools/SchoolGuestOfficeService.cs'
$persistence = Read-Source 'Code/core/schools/GuestOfficePersistence.cs'
$models = Read-Source 'Code/core/schools/GuestOfficePersistenceDbModels.cs'
$db = Read-Source 'Code/core/schools/GuestOfficePersistenceDb.cs'

Require-Text $court 'SchoolGuestOfficeService.FillVacanciesAfterCivilServiceExam(' `
    'exam completion immediately invites qualified foreign graduates'
Require-Text $court 'bool pActing = false)' `
    'guest appointment gate receives the actual appointment mode'
Require-Text $court 'HasHostIssuedQualification(' `
    'acting foreign graduate must hold a qualification issued by the host'

foreach ($required in @(
        'GuestOfficeSubmissionOutcome TryAppointAndRecord(',
        'candidate.IsActing',
        'SchoolGuestOfficeRules.ReservesOffice(submission)',
        'pActing: pActing',
        'pActing: _pending.IsActing',
        'pActing: recoveryResult.IsActing',
        'PendingOfficeIds(pHost.id)')) {
    Require-Text $guest $required "guest acting appointment chain $required"
}
Reject-Text $guest '_pending.OfficeId, residence, pActing: false);' `
    'guest write cannot erase the acting marker'

Require-Text $persistence 'IsActing = appointment.IsActing' `
    'guest start seed preserves the acting marker'
Require-Text $persistence 'result.Career.IsActing' `
    'guest recovery returns the committed acting marker'
Require-Text $models 'public bool IsActing;' `
    'guest career tuple models the acting flag'
Require-Text $db 'IS_ACTING' `
    'guest career persistence stores and reads the acting flag'

if ($failures.Count -gt 0) {
    Write-Host "Civil-service guest acting guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Civil-service guest acting source guard passed.'
