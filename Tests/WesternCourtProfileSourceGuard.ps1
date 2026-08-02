$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

function Read-Source([string] $relative) {
    $path = Join-Path $root $relative
    if (-not [IO.File]::Exists($path)) { throw "Missing source: $relative" }
    return [IO.File]::ReadAllText($path)
}

function Require([string] $name, [string] $source, [string] $text) {
    if (-not $source.Contains($text)) { throw "$name missing: $text" }
}

function Require-Regex([string] $name, [string] $source, [string] $pattern) {
    if ($source -notmatch $pattern) { throw "$name missing pattern: $pattern" }
}

$runtime = Read-Source 'Code/core/court/CourtProfileRegistryRuntime.cs'
Require 'runtime profile resolution' $runtime `
    'KingdomPolicyService.GetPolicyProfile(pKingdom)'
Require 'runtime institution office filter' $runtime `
    'OfficeIdsForInstitution('
Require 'runtime profile office lookup' $runtime 'FindOffice('

$court = Read-Source 'Code/core/court/CourtService.cs'
Require 'court profile central offices' $court `
    'CourtProfileRegistry.CentralOfficeIdsFor(pKingdom)'
Require 'court profile school preference' $court `
    'CourtProfileRegistry.PreferredSchoolFor(pKingdom, pOfficeId)'
Require 'court profile manual validation' $court `
    'CourtProfileRegistry.IsOfficeAvailableFor('

$institution = Read-Source 'Code/core/court/CourtInstitutionService.cs'
Require 'institution profile resolution' $institution `
    'CourtProfileRegistry.For(pKingdom)'
Require 'institution Western policy effect' $institution `
    'WesternCourtUnlocked'
Require 'institution Western government state' $institution `
    'POLICY_GOVERNMENT_STATE'

$career = Read-Source 'Code/core/court/OfficialCareerStateService.cs'
Require 'profile office grade' $career `
    'CourtProfileRegistry.FindOfficeAcrossProfiles(pOfficeId)'
Require 'profile military office' $career `
    'CourtProfileRegistry.IsMilitaryOfficeAcrossProfiles(pOfficeId)'

$election = Read-Source 'Code/core/court/WesternCourtElectionService.cs'
Require 'election merged vacancy queue' $election `
    'private static readonly Queue<WesternCourtVacancy> VacancyQueue'
Require 'election vacancy deduplication' $election `
    'private static readonly HashSet<string> QueuedVacancies'
Require 'election authority-cycle entry point' $election `
    'public static void ProcessAuthorityCycle()'
Require 'election per-cycle vacancy budget' $election `
    'WesternCourtElectionRules.MaxVacanciesPerCycle'
Require 'election per-office candidate budget' $election `
    'WesternCourtElectionRules.MaxCandidatesPerVacancy'
Require 'election bounded candidate selection' $election `
    'WesternCourtElectionRules.SelectWinner(candidates)'
Require 'election atomic appointment bridge' $election `
    'CourtService.TryElectCentralOfficer('

Require 'court elective annual queue' $court `
    'WesternCourtElectionService.QueueKingdomVacancies(pKingdom)'
Require-Regex 'court elective skips bounded AI' $court `
    '(?s)if \(IsWesternElective\(pKingdom\)\).*?QueueKingdomVacancies\(pKingdom\).*?else.*?EnsureMinimumCourt\('
Require-Regex 'exam elective only queues' $court `
    '(?s)FillVacanciesAfterCivilServiceExam\(.*?IsWesternElective\(pKingdom\).*?QueueKingdomVacancies\(pKingdom\).*?return;'
Require 'court exposed atomic election appointment' $court `
    'internal static bool TryElectCentralOfficer('
Require 'court elective manual appointment gate' $court `
    'WesternCourtElectionRules.CanManualAppoint('
Require 'court feudal landed noble preference' $court `
    'FiefService.GetFiefCityId(pActor)'
Require 'court feudal education preference' $court `
    'HistoricalSchoolEducationService.IsEducated(pActor,'

Require 'career elective fixed term' $career `
    'WesternCourtElectionRules.TermEndYear(currentYear)'
Require 'career elective expiry path' $career `
    'CourtService.TryExpireWesternElectiveCentralOfficial('
Require 'career expiry vacancy queue' $career `
    'WesternCourtElectionService.EnqueueVacancy('
Require 'career elective bypasses lifetime migration' $career `
    'bool migratedLifetime = !westernElectiveCentral &&'
Require 'career term due uses normalized term' $career `
    'mutation.TermEndYear <= year;'
Require 'elective expiry uses close-first dismissal' $court `
    'return TryDismissOfficer(pActor, pKingdom, "elective_term_ended");'
Require-Regex 'elective guest expiry uses durable guest end' $court `
    '(?s)TryExpireWesternElectiveCentralOfficial\(.*?CourtAffiliationResolver\.IsValidGuestService\(\s*pActor, pKingdom\).*?return EndGuestOfficer\(pActor, pKingdom,\s*"elective_term_ended", Date\.getCurrentYear\(\)\);.*?return TryDismissOfficer\('

$guest = Read-Source 'Code/core/schools/SchoolGuestOfficeService.cs'
Require-Regex 'guest annual AI defers elective vacancies' $guest `
    '(?s)ProcessHostAppointments\(Kingdom pHost.*?if \(CourtService\.IsWesternElective\(pHost\)\).*?WesternCourtElectionService\.QueueKingdomVacancies\(pHost\);.*?return;'
Require-Regex 'guest appointment API rejects elective' $court `
    '(?s)CanAppointGuestOfficer\(Actor pActor, Kingdom pKingdom,.*?if \(IsWesternElective\(pKingdom\)\) return false;'
Require-Regex 'pending guest live eligibility helper' $guest `
    '(?s)CanCommitPendingGuestStart\(Actor pActor,\s*Kingdom pHost,.*?!CourtService\.IsWesternElective\(pHost\).*?CourtService\.CanAppointGuestOfficer\('
Require-Regex 'pending guest prequeue live institution gate' $guest `
    '(?s)TryProcessPending\(PendingGuestOffice pPending\).*?if \(pPending\.StartRequest != null &&.*?!CanCommitPendingGuestStart\(actor, host, residence,\s*pPending\).*?CancelInvalidPendingGuestStart\('
Require-Regex 'committed reform compensation precedes live entity gate' $guest `
    '(?s)TryProcessPending\(PendingGuestOffice pPending\).*?Kingdom host = FindKingdom\(pPending\.HostKingdomId\);.*?if \(pPending\.StartRequest != null &&\s*pPending\.CommittedStartResult != null &&\s*CourtService\.IsWesternElective\(host\)\)\s*return CancelInvalidPendingGuestStart\(pPending, actor, host\);.*?if \(actor\?\.data == null \|\| host\?\.data == null \|\| residence\?\.data == null'
Require-Regex 'pending guest committed compensation' $guest `
    '(?s)CancelInvalidPendingGuestStart\(.*?ConvertCommittedStartToEnd\(.*?TryProcessPendingEnd\('
Require-Regex 'completed guest end opens elective vacancy' $guest `
    '(?s)TryProcessPendingEnd\(PendingGuestOffice pPending\).*?GuestOfficeEndPendingRules\.ShouldRetain\(.*?return false;.*?QueueWesternElectiveVacancy\(pPending\);\s*return true;'
Require-Regex 'elective vacancy enqueue follows guest end completion' $guest `
    '(?s)QueueWesternElectiveVacancy\(PendingGuestOffice pPending\).*?CourtService\.IsWesternElective\(host\).*?WesternCourtElectionService\.EnqueueVacancy\(host,\s*pPending\.OfficeId, pPending\.ActorId\);'
Require-Regex 'guest write transaction live institution gate' $guest `
    '(?s)class GuestStartWriteOperation.*?Execute\(.*?if \(!CanCommitPendingGuestStart\(actor, host, residence,\s*_pending\)\).*?return HistoricalSchoolTeachingPersistenceOutcome\.CleanFailure;.*?GuestOfficePersistence\.StartInTransaction\('
Require-Regex 'committed guest end retries failed career cleanup' $court `
    '(?s)ApplyCommittedGuestOfficerEnd\(.*?if \(frozenProjectionStillLive\).*?ClearOfficer\(.*?else if \(!OfficialCareerStateService\.ClearCurrentOffice\(pActor,\s*pHostKingdomId, pOfficeId\)\)\s*return false;'
Require-Regex 'career cleanup treats absent or changed office as reconciled' $career `
    '(?s)ClearCurrentOffice\(Actor pActor, long pKingdomId,\s*string pOfficeId\).*?if \(state == null \|\| state\.KingdomId != pKingdomId \|\|.*?state\.OfficeId != pOfficeId\)\).*?transaction\.Rollback\(\);\s*return true;'

$authority = Read-Source 'Code/core/performance/AWAuthorityCycleService.cs'
Require-Regex 'election runs after authority gate' $authority `
    '(?s)if \(!pGate\.TryEnter\(pCycleToken, allowed\)\) return;.*?WesternCourtElectionService\.ProcessAuthorityCycle\(\);'
Require 'election runtime reset' $authority `
    'WesternCourtElectionService.Reset();'

foreach ($relative in @(
        'Code/core/policy/KingdomPolicyAI.cs',
        'Code/core/court/CivilServiceExamService.cs',
        'Code/core/court/CourtReadModelService.cs',
        'Code/core/schools/SchoolGuestOfficeService.cs',
        'Code/core/schools/HistoricalSchoolTravelService.cs'
    )) {
    $source = Read-Source $relative
    if ($source.Contains(
            'CourtTierRules.CentralOfficesForTier(' +
            [Environment]::NewLine +
            '                CourtService.ResolveTier(')) {
        throw "Profile-blind central office lookup remains in $relative"
    }
}

Write-Output 'Western court profile source guard passed.'
