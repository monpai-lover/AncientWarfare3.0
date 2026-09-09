$ErrorActionPreference = 'Stop'

function Read-Source([string] $Path) {
    $full = Join-Path (Split-Path $PSScriptRoot -Parent) $Path
    if (-not (Test-Path -LiteralPath $full)) {
        throw "Missing source file: $Path"
    }
    return Get-Content -LiteralPath $full -Raw
}

function Require-Text([string] $Source, [string] $Needle, [string] $Label) {
    if (-not $Source.Contains($Needle)) {
        throw "Missing ${Label}: $Needle"
    }
}

$session = Read-Source 'Code/core/db/MilitaryExamSessionTableItem.cs'
$candidate = Read-Source 'Code/core/db/MilitaryExamCandidateTableItem.cs'
$officer = Read-Source 'Code/core/db/MilitaryOfficerRecordTableItem.cs'
$indexes = Read-Source 'Code/core/db/LineageArchiveIndexRules.cs'

Require-Text $session '[TableDef("MilitaryExamSession")]' 'session table'
foreach ($field in @('kingdom_id', 'cycle_year', 'open_world_day',
        'next_due_world_day', 'mode', 'general_target', 'local_target',
        'general_vacancies', 'local_vacancies', 'reserve_target')) {
    Require-Text $session $field "session field $field"
}

Require-Text $candidate '[TableDef("MilitaryExamCandidate")]' 'candidate table'
foreach ($field in @('session_id', 'actor_id', 'age_snapshot', 'warfare_score',
        'damage_score', 'strength_score', 'speed_score', 'diplomacy_score',
        'military_merit_score', 'qualification', 'appointment_tier',
        'appointment_status')) {
    Require-Text $candidate $field "candidate field $field"
}

Require-Text $officer '[TableDef("MilitaryOfficerRecord")]' 'officer table'
foreach ($field in @('actor_id', 'kingdom_id', 'tier', 'slot_key', 'city_id',
        'army_id', 'appointed_time', 'ended_time', 'active', 'end_reason')) {
    Require-Text $officer $field "officer field $field"
}

foreach ($index in @('uq_MilitaryExamSession_kingdom_cycle',
        'uq_MilitaryExamCandidate_session_actor',
        'uq_MilitaryOfficerRecord_active_slot',
        'uq_MilitaryOfficerRecord_active_actor')) {
    Require-Text $indexes $index "required index $index"
}

Write-Host 'Military exam persistence source guard passed.'
