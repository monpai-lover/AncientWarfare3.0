$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$court = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/court/CourtService.cs'))
$career = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/court/OfficialCareerService.cs'))
$persistence = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/court/OfficialCareerPersistence.cs'))
$state = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/court/OfficialCareerStateService.cs'))
$replacement = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/court/CourtOfficerReplacementPersistence.cs'))
$guestOffice = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/schools/SchoolGuestOfficeService.cs'))
$failures = [System.Collections.Generic.List[string]]::new()

function Require-Text([string]$source, [string]$needle, [string]$label) {
    if (-not $source.Contains($needle)) {
        $failures.Add("${label}: missing '$needle'")
    }
}

function Require-Order([string]$source, [string]$first, [string]$second,
        [string]$label) {
    $firstIndex = $source.IndexOf($first, [StringComparison]::Ordinal)
    $secondIndex = $source.IndexOf($second, [StringComparison]::Ordinal)
    if ($firstIndex -lt 0 -or $secondIndex -lt 0 -or
        $firstIndex -ge $secondIndex) {
        $failures.Add("${label}: '$first' must precede '$second'")
    }
}

foreach ($required in @(
        'BuildIndexedFormalCandidateRoster(pKingdom)',
        'CivilServiceExamCandidateTableItem.GetTableName()',
        'CivilServiceExamSessionTableItem.GetTableName()',
        'CivilServiceExamRules.CandidateSourceLimit',
        'LIMIT @limit',
        'FindBestIndexedFormalCandidate(',
        'indexedFormalCandidates')) {
    Require-Text $court $required "bounded formal candidate index"
}

$fillStart = $court.IndexOf('private static void FillCentralOffice(',
    [StringComparison]::Ordinal)
$fillEnd = $court.IndexOf('private static Actor FindBestCandidate(',
    $fillStart, [StringComparison]::Ordinal)
$fill = if ($fillStart -ge 0 -and $fillEnd -gt $fillStart) {
    $court.Substring($fillStart, $fillEnd - $fillStart)
} else { '' }
Require-Order $fill 'FindBestIndexedFormalCandidate(' 'FindBestCandidate(' `
    'formal graduates precede the legacy bounded roster'
Require-Text $fill 'HasExaminationSystem(pKingdom)' `
    'legacy no-examination path remains explicit'
if (-not [regex]::IsMatch($fill,
        'FindBestCandidate\(\s*pKingdom,\s*pRoster,\s*pOfficeId,\s*' +
        'pPreferredSchool,\s*pAllowVacancyPromotion:\s*true,\s*' +
        'pUnavailableActorIds\)')) {
    $failures.Add(
        'vacant central offices must promote qualified domestic candidates outside the bounded index')
}
if (-not [regex]::IsMatch($fill,
        'FindBestCandidate\([\s\S]*?pUnavailableActorIds\);\s*' +
        'vacancyPromotion\s*=\s*candidate\s*!=\s*null;')) {
    $failures.Add(
        'full-roster vacancy promotions must persist their promotion state')
}
Require-Text $fill 'allowActing: pAllowActing' `
    'local acting fallback runs only during the explicit acting pass'
Require-Text $court 'BuildActiveCentralActorSet()' `
    'central vacancy filling reads authoritative occupied actor ids'
Require-Text $court 'pUnavailableActorIds.Contains(pActor.data.id)' `
    'all central candidate paths reject an actor already holding a central office'
Require-Text $fill 'pUnavailableActorIds?.Add(candidate.data.id);' `
    'a committed appointment reserves the actor for the rest of the batch'
Require-Text $court 'runtimeOffice == pOfficeId' `
    'a durable central-office row remains active only for the exact projected office'
if (-not [regex]::IsMatch($court,
        'IsValidActiveOfficeActor\(actor,\s*pKingdom,\s*row\.layer,\s*' +
        'row\.office_id\)')) {
    $failures.Add(
        'stale-row cleanup must validate the durable office id, not only its layer')
}
if (-not [regex]::IsMatch($court,
        'IsValidActiveOfficeActor\(actor,\s*pKingdom,\s*officer\.layer,\s*' +
        'officer\.office_id\)')) {
    $failures.Add(
        'vacancy occupancy must ignore durable rows that do not match the actor projection')
}

$examFillStart = $court.IndexOf(
    'public static void FillVacanciesAfterCivilServiceExam(',
    [StringComparison]::Ordinal)
$examFillEnd = $court.IndexOf('private static void ValidateOfficers(',
    $examFillStart, [StringComparison]::Ordinal)
$examFill = if ($examFillStart -ge 0 -and
    $examFillEnd -gt $examFillStart) {
    $court.Substring($examFillStart, $examFillEnd - $examFillStart)
} else { '' }
Require-Text $examFill 'HashSet<long> occupiedActors = BuildActiveCentralActorSet();' `
    'exam vacancy pass starts from authoritative occupied actors'
$guestIndex = $examFill.LastIndexOf(
    'SchoolGuestOfficeService.FillVacanciesAfterCivilServiceExam(',
    [StringComparison]::Ordinal)
$rebuiltActorIndex = if ($guestIndex -ge 0) {
    $examFill.IndexOf('occupiedActors = BuildActiveCentralActorSet();',
        $guestIndex, [StringComparison]::Ordinal)
} else { -1 }
$actingFillIndex = if ($rebuiltActorIndex -ge 0) {
    $examFill.IndexOf('EnsureMinimumCourt(pKingdom, roster, occupied, tier,',
        $rebuiltActorIndex, [StringComparison]::Ordinal)
} else { -1 }
if ($guestIndex -lt 0 -or $rebuiltActorIndex -le $guestIndex) {
    $failures.Add(
        'guest appointments must commit before actor reservations are rebuilt')
}
if ($actingFillIndex -le $rebuiltActorIndex) {
    $failures.Add(
        'acting fallback must use the rebuilt authoritative actor set')
}

Require-Text $career 'OfficialCareerPersistence.Appoint(db, appointment,' `
    'central acting appointment supplies an atomic state stage'
Require-Text $career 'OfficialCareerStateService.StageAppointment(' `
    'central acting state is staged in the career transaction'
Require-Text $career 'OfficialCareerStateService.PublishAppointment(' `
    'central acting hot state is published only after commit'
if ([regex]::IsMatch($career, 'if\s*\(\s*pActing\s*\)\s*stageState\s*=')) {
    $failures.Add('formal and acting appointments must both stage state atomically')
}
if (-not [regex]::IsMatch($career,
        'stageState\s*=\s*\(connection,\s*transaction\)\s*=>\s*\r?\n\s*stateProjection\s*=\s*OfficialCareerStateService\.StageAppointment\([\s\S]*?pActing,\s*pVacancyPromotion\);')) {
    $failures.Add('career appointment must pass the actual acting and vacancy-promotion flags into unconditional state staging')
}
Require-Text $persistence 'Action<SQLiteConnection, SQLiteTransaction>' `
    'career persistence accepts an atomic state stage'
Require-Text $persistence 'pStageAdditional?.Invoke(pDb, transaction);' `
    'additional state stage executes before transaction commit'
$appointStart = $persistence.IndexOf(
    'public static OfficialCareerAppointmentResult Appoint(',
    [StringComparison]::Ordinal)
$captureStart = $persistence.IndexOf(
    'internal static OfficialCareerPersistenceToken Capture(',
    $appointStart, [StringComparison]::Ordinal)
$appoint = if ($appointStart -ge 0 -and $captureStart -gt $appointStart) {
    $persistence.Substring($appointStart, $captureStart - $appointStart)
} else { '' }
Require-Order $appoint 'pStageAdditional?.Invoke(pDb, transaction);' `
    'transaction.Commit();' 'state stage precedes career commit'
Require-Text $state 'internal static OfficialCareerAppointmentProjection StageAppointment(' `
    'state service exposes transaction-bound staging'
Require-Text $state 'internal static void PublishAppointment(' `
    'state service separates committed hot projection'
Require-Text $replacement `
    'Action<SQLiteConnection, SQLiteTransaction> pStageAdditional' `
    'officer replacement accepts transaction-bound state staging'
Require-Text $replacement `
    'pStageAdditional?.Invoke(pDb, transaction);' `
    'officer replacement stages state before commit'
Require-Order $replacement `
    'pStageAdditional?.Invoke(pDb, transaction);' 'transaction.Commit();' `
    'replacement state stage precedes replacement commit'
Require-Text $court `
    'OfficialCareerStateService.PublishAppointment(candidateStateProjection);' `
    'replacement publishes staged state only after commit'
Require-Text $court 'pStateProjectionCommitted: true' `
    'runtime projection does not repeat an already committed state write'

$guestWriteStart = $guestOffice.IndexOf(
    'private sealed class GuestStartWriteOperation',
    [StringComparison]::Ordinal)
$guestWriteEnd = $guestOffice.IndexOf(
    'private sealed class GuestEndWriteOperation', $guestWriteStart,
    [StringComparison]::Ordinal)
$guestWrite = if ($guestWriteStart -ge 0 -and
    $guestWriteEnd -gt $guestWriteStart) {
    $guestOffice.Substring($guestWriteStart,
        $guestWriteEnd - $guestWriteStart)
} else { '' }
Require-Text $guestWrite 'OfficialCareerStateService.StageAppointment(' `
    'guest career state is staged in the guest start transaction'
Require-Order $guestWrite 'GuestOfficePersistence.StartInTransaction(' `
    'OfficialCareerStateService.StageAppointment(' `
    'guest tuple is validated before staging career state'
Require-Order $guestWrite 'OfficialCareerStateService.StageAppointment(' `
    'OfficialCareerStateService.PublishAppointment(' `
    'guest hot career state publishes only after the transaction commits'
Require-Text $guestOffice 'pStateProjectionCommitted: true' `
    'guest runtime projection does not repeat committed career-state persistence'

if ($failures.Count -gt 0) {
    throw "Central civil-service appointment guard failures:`n - " +
        ($failures -join "`n - ")
}

Write-Output 'Central civil-service appointment source guards passed.'
