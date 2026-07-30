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

$query = Read-Source 'Code/core/court/CivilServiceExamCandidateQuery.cs'
$pool = Read-Source 'Code/core/court/CivilServiceExamCandidatePoolQuery.cs'
$candidateQuery = $query + $pool
$service = Read-Source 'Code/core/court/CivilServiceExamService.cs'
$rules = Read-Source 'Code/core/court/CivilServiceExamRules.cs'
$qualification = Read-Source 'Code/core/court/CivilServiceQualificationService.cs'
$travel = Read-Source 'Code/core/schools/HistoricalSchoolTravelService.cs'
$guest = Read-Source 'Code/core/schools/SchoolGuestOfficeService.cs'
$indexes = Read-Source 'Code/core/db/LineageArchiveIndexRules.cs'

foreach ($required in @(
        'SchoolAffiliationTableItem.GetTableName()',
        'RESIDENCE_CITY_ID',
        'LIFECYCLE_STATE=@resident',
        'SERVICE_KINGDOM_ID<0',
        'HOME_KINGDOM_ID<>@kingdom',
        'ORDER BY',
        'LIMIT @limit',
        'World.world?.units?.get(actorId)',
        'SelectCandidatesWithLocalPriority')) {
    Require-Text $candidateQuery $required "bounded foreign candidate query $required"
}
Reject-Text $candidateQuery 'foreach (Actor actor in World.world.units' `
    'foreign candidate query cannot enumerate world actors'
Reject-Text $candidateQuery 'World.world.units.ToList' `
    'foreign candidate query cannot materialize world actors'
Require-Text $indexes 'idx_SchoolAffiliation_exam_travel' `
    'foreign invitation query has a dedicated affiliation index'
Require-Text $indexes `
    'LIFECYCLE_STATE, SERVICE_KINGDOM_ID, ' `
    'foreign invitation index starts with bounded status filters'

foreach ($required in @(
        'SuggestedCandidateTarget = 24',
        'AnnualForeignInvitationLimit = 4',
        'ForeignInvitationCount(',
        'IsEligibleForeignExamCandidate(',
        'CanInviteForeignScholar(',
        'HasEquivalentHostQualification(',
        'CanEnterGuestCandidateIndex(',
        'SelectCandidatesWithLocalPriority(')) {
    Require-Text $rules $required "foreign talent rule $required"
}

foreach ($required in @(
        'CountEligibleForInvitation(',
        'LoadForeignInvitationActorIds(',
        'HistoricalSchoolTravelService.TryInviteToCity(',
        'CivilServiceExamRules.ForeignInvitationCount(')) {
    Require-Text $service $required "annual invitation flow $required"
}
Reject-Text $service 'World.world.units' `
    'annual invitation flow cannot scan world actors'

foreach ($required in @(
        'SchoolLineageService.TryReserveItinerant(',
        'SchoolLineageService.TryReserveExamTraveler(',
        'HistoricalAffiliationService.TryBeginTravel(',
        'EnsureTravelTask(',
        'HistoricalAffiliationService.CancelTravel(pActor);',
        'SchoolLineageService.ReleaseItinerant(pActor);')) {
    Require-Text $travel $required "atomic invited travel $required"
}
$lineage = Read-Source 'Code/core/schools/SchoolLineageService.cs'
Require-Text $lineage 'public static bool TryReserveExamTraveler(' `
    'examination travel has a narrow reservation entry'
Require-Text $lineage 'if (!IsQualifiedTeacher(pActor)) return false;' `
    'ordinary itinerant travel remains teacher-only'
Require-Text $lineage 'ItinerantReservations.TryReserve(' `
    'examination travel reuses the itinerant reservation book'
Require-Text $lineage `
    'HistoricalSchoolTravelReservationRestoreRules.ShouldUseExamTravelerReservation(' `
    'reload classifies non-teacher exam travellers explicitly'
Require-Text $lineage 'TryReserveExamTraveler(actor, schoolId)' `
    'reload restores ordinary examination travellers into the bounded reservation book'
Require-Text $query 'IsAtWar(pKingdom, home)' `
    'foreign invitation results exclude enemy source kingdoms'
Reject-Text ($query + $service + $travel) 'setKingdom(' `
    'foreign scholars retain nationality'

Require-Text $qualification `
    'if (!HasExaminationSystem(pKingdom)) return true;' `
    'legacy appointments remain open before examination research'
Require-Text $guest 'GuestOfficePersistence.PrepareStart(' `
    'foreign appointments reuse guest-office atomic persistence'
Require-Text $guest 'OfficialCareerService.PrepareAppointment(' `
    'foreign appointments reuse official-career preparation'
Require-Text $guest `
    'LoadQualifiedForeignResidentActorIds(pHost,' `
    'host-qualified foreign residents enter the guest candidate index'
Require-Text $guest 'CivilServiceExamRules.CanEnterGuestCandidateIndex(' `
    'guest candidate eligibility preserves teacher or host qualification gate'
Require-Text $guest 'GuestOfficeSubmissionOutcome submission = TryAppointAndRecord(' `
    'qualified foreign graduates reuse the atomic guest appointment chain'
Require-Text $guest 'candidate.Actor, pHost, office, residence' `
    'qualified foreign graduate identity reaches the atomic guest appointment chain'
Require-Text $guest '.ResidentTeacherIds(city.data.id, school.Id)' `
    'ordinary teacher guest discovery remains intact'

if ($failures.Count -gt 0) {
    Write-Host "Civil-service foreign talent guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Civil-service foreign talent source guard passed.'
