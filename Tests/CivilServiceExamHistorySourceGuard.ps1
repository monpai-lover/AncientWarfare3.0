$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    $path = Join-Path $repo $relativePath
    if (-not [IO.File]::Exists($path)) {
        $failures.Add("missing source file $relativePath")
        return ''
    }
    return [IO.File]::ReadAllText($path)
}

function Require-Text([string]$source, [string]$needle, [string]$name) {
    if (-not $source.Contains($needle)) {
        $failures.Add("${name}: missing '$needle'")
    }
}

function Reject-Text([string]$source, [string]$needle, [string]$name) {
    if ($source.Contains($needle)) {
        $failures.Add("${name}: forbidden '$needle'")
    }
}

function Has-Cjk([string]$value) {
    foreach ($character in $value.ToCharArray()) {
        $code = [int]$character
        if ($code -ge 0x3400 -and $code -le 0x9FFF) { return $true }
    }
    return $false
}

$keys = Read-Source 'Code/core/lineage/ChronicleKeys.cs'
$events = Read-Source 'Code/core/lineage/ChronicleEvents.cs'
$rules = Read-Source 'Code/core/court/OfficialCareerBiographyRules.cs'
$exam = Read-Source 'Code/core/court/CivilServiceExamService.cs'
$court = Read-Source 'Code/core/court/CourtService.cs'
$lineageKeys = Read-Source 'Code/core/lineage/LineageKeys.cs'
$locale = Read-Source 'Locales/aw3_court.csv'

foreach ($required in @(
        'CIVIL_SERVICE_QUALIFIED = "civil_service_qualified"',
        'CIVIL_SERVICE_TOP_RANKED = "civil_service_top_ranked"',
        'CIVIL_SERVICE_FIRST_APPOINTMENT =',
        'CIVIL_SERVICE_EXAM_OPENED = "civil_service_exam_opened"',
        'CIVIL_SERVICE_EXAM_COMPLETED =')) {
    Require-Text $keys $required "examination history event $required"
}

foreach ($required in @(
        'OnCivilServiceExamOpened(',
        'OnCivilServiceQualification(',
        'OnCivilServiceTopRanked(',
        'OnCivilServiceExamCompleted(',
        'OnCivilServiceFirstAppointment(',
        'HistoryWriter.RecordKingdom(',
        'HistoryWriter.RecordPerson(')) {
    Require-Text $events $required "examination chronicle $required"
}
Reject-Text $events 'OnCivilServiceExamFailed(' `
    'ordinary examination failures are not person biography events'
Reject-Text $keys 'CIVIL_SERVICE_FAILED' `
    'ordinary failures have no chronicle event type'

foreach ($required in @(
        'ShouldRecordFirstFormalAppointment(',
        'case "civil_service_qualified":',
        'case "civil_service_top_ranked":',
        'case "civil_service_first_appointment":')) {
    Require-Text $rules $required "career biography classification $required"
}

foreach ($required in @(
        'CIVIL_SERVICE_FIRST_APPOINTMENT_RECORDED',
        'RecordCommittedQualificationHistory(',
        'RecordCommittedTopRanks(',
        'ChronicleEvents.OnCivilServiceExamOpened(',
        'ChronicleEvents.OnCivilServiceExamCompleted(')) {
    $target = if ($required -eq 'CIVIL_SERVICE_FIRST_APPOINTMENT_RECORDED') {
        $lineageKeys + $court
    } else {
        $exam
    }
    Require-Text $target $required "committed examination history $required"
}
Require-Text $court 'ShouldRecordFirstFormalAppointment(' `
    'one-shot first formal appointment gate'
Require-Text $court 'ChronicleEvents.OnCivilServiceFirstAppointment(' `
    'first appointment chronicle call'

$localeKeys = @(
    'aw_civil_service_mode_imperial',
    'aw_civil_service_mode_tribute',
    'aw_civil_service_qualification_juren',
    'aw_civil_service_qualification_gongshi',
    'aw_civil_service_qualification_jinshi',
    'aw_civil_service_rank_zhuangyuan',
    'aw_civil_service_rank_bangyan',
    'aw_civil_service_rank_tanhua',
    'aw_hist_civil_service_exam_opened_mid',
    'aw_hist_civil_service_exam_completed_mid',
    'aw_hist_civil_service_qualified_mid',
    'aw_hist_civil_service_top_ranked_mid',
    'aw_hist_civil_service_first_appointment_mid')
foreach ($key in $localeKeys) {
    $line = ($locale -split "`r?`n" |
        Where-Object { $_.StartsWith($key + ',') } |
        Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($line)) {
        $failures.Add("localization row missing: $key")
        continue
    }
    $columns = $line.Split(',')
    if ($columns.Count -ne 4 -or
        [string]::IsNullOrWhiteSpace($columns[1]) -or
        [string]::IsNullOrWhiteSpace($columns[2]) -or
        [string]::IsNullOrWhiteSpace($columns[3]) -or
        -not (Has-Cjk $columns[1]) -or
        -not (Has-Cjk $columns[3]) -or
        $columns[2] -notmatch '[A-Za-z]') {
        $failures.Add("localization row must contain Simplified Chinese English and Traditional Chinese: $key")
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Civil service exam history guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Civil service exam history source guard passed.'
