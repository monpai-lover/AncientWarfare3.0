$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$qualificationPath = Join-Path $root 'Code/core/court/CivilServiceQualificationService.cs'
$careerPath = Join-Path $root 'Code/core/court/OfficialCareerService.cs'
$statePath = Join-Path $root 'Code/core/court/OfficialCareerStateService.cs'
$historyLocalizationPath = Join-Path $root 'Code/core/lineage/HistoryLocalizationRules.cs'
$historyWindowPath = Join-Path $root 'Code/ui/windows/HistoryListWindow.cs'
$courtPath = Join-Path $root 'Code/core/court/CourtService.cs'
$cityPatchPath = Join-Path $root 'Code/patch/AW_CityLeaderPatch.cs'
$keysPath = Join-Path $root 'Code/core/lineage/LineageKeys.cs'
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$path, [string]$label) {
    if (-not (Test-Path -LiteralPath $path)) {
        $failures.Add("${label}: missing file '$path'")
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

function Require-Match([string]$source, [string]$pattern, [string]$label) {
    if (-not [regex]::IsMatch($source, $pattern)) {
        $failures.Add("${label}: missing pattern '$pattern'")
    }
}

function Reject-Match([string]$source, [string]$pattern, [string]$label) {
    if ([regex]::IsMatch($source, $pattern)) {
        $failures.Add("${label}: forbidden pattern '$pattern'")
    }
}

$qualification = Read-Source $qualificationPath 'qualification service'
$career = Read-Source $careerPath 'career appointment service'
$state = Read-Source $statePath 'career state service'
$historyLocalization = Read-Source $historyLocalizationPath `
    'history localization rules'
$historyWindow = Read-Source $historyWindowPath 'history list window'
$court = Read-Source $courtPath 'court service'
$cityPatch = Read-Source $cityPatchPath 'city leader patch'
$keys = Read-Source $keysPath 'lineage keys'

Require-Text $keys 'CIVIL_SERVICE_QUALIFICATION' 'qualification projection key'
Require-Text $keys 'CIVIL_SERVICE_ISSUING_KINGDOM_ID' 'issuing kingdom projection key'
Require-Text $keys 'CIVIL_SERVICE_SESSION_ID' 'session projection key'
Require-Text $keys 'CIVIL_SERVICE_RESULT_YEAR' 'result year projection key'
Require-Text $keys 'CIVIL_SERVICE_ENTRY_BONUS' 'entry bonus projection key'

Require-Text $qualification 'CanReceiveFormalCivilAppointment(' 'shared formal appointment gate'
Require-Text $qualification 'LoadLatestQualification(' 'database-backed projection repair'
Require-Text $qualification 'HistoricalSchoolEducationService.CanAppoint(' 'education gate'
Require-Text $qualification 'IsAppointmentExempt(' 'explicit appointment exemptions'
Require-Text $qualification 'HasRequiredServiceHistory(' 'career service ladder'
Require-Text $qualification 'OfficialCareerRankRules.CanEnterOffice(' 'rank and service gate'
Require-Text $qualification 'ShouldUseVacancyPromotion(' `
    'formal qualification gate has a vacancy-only promotion exception'
Require-Match $qualification `
    'OfficialCareerRankRules\.IsRequiredServiceGrade\(\s*grade,\s*requiredOfficeGrade\)' `
    'service history delegates exact office-tier matching'
Require-Text $qualification 'IFNULL(IS_ACTING,0)=0' `
    'acting appointments cannot satisfy formal service history'
Reject-Match $qualification `
    'grade\s*<=\s*(?:required|maximum)OfficeGrade' `
    'service history cannot use descending threshold comparison'
Reject-Match $qualification `
    'grade\s*>=\s*(?:required|maximum)OfficeGrade' `
    'service history cannot use ascending threshold comparison'

Require-Text $career 'CanReceiveFormalCivilAppointment(' 'commit-time appointment gate'
Require-Text $court 'CanReceiveFormalCivilAppointment(' 'candidate-discovery appointment gate'
Require-Text $court 'pAllowVacancyPromotion: true' `
    'automatic and manual vacant-office paths request formal promotion'
Require-Text $court 'TryAssignActingCityGovernor(' 'one-year acting governor path'
Require-Text $cityPatch 'TryAssignActingCityGovernor(' 'vacancy uses acting path'
Require-Text $court 'ShouldUseActingCentralFallback(' `
    'central vacancies use an educated acting fallback'
Require-Text $court 'ShouldExpireActingCentralOfficial(' `
    'central acting officials are reconsidered after one year'
Require-Text $court 'pActing: true' `
    'central acting fallback is persisted as acting rather than formal'
Require-Text $state 'ResolveInitialAppointmentRank(' 'qualification-based initial rank'
Require-Text $state 'ResolveVacancyPromotionRank(' `
    'vacancy promotion persists the target office rank floor'
Require-Text $state 'AW_L10n.Text(' `
    'evaluation history resolves official rank keys through runtime localization'
Reject-Match $state `
    'HistoryLocalizationRules\.Text\(\s*OfficialCareerRankRules\.RankNameKey' `
    'evaluation history cannot leak official rank localization keys'
Require-Text $historyLocalization `
    'new Entry("aw_hist_official_rank_grant_mid"' `
    'first official rank grant middle fragment is localized in history'
Require-Text $historyLocalization `
    'new Entry("aw_hist_official_rank_grant_suffix"' `
    'first official rank grant suffix is localized in history'
Require-Text $historyWindow 'NormalizeLegacyLocalizationKeys(' `
    'history rendering repairs legacy official rank keys without rewriting the archive'
Require-Text $state 'pActing ? pYearAfter(currentYear)' 'one-year acting term'
Require-Text $state 'pActing ? currentYear : -1' 'durable acting marker'
Require-Text $state 'TryExpireActingCityGovernor(' 'acting expiry before promotion'
Require-Text $state 'TryExpireActingCentralOfficial(' `
    'central acting expiry runs in the annual career cycle'
Require-Text $court 'TryExpireActingCityGovernor(' 'acting vacancy retry entry'
Require-Text $court 'TryRestoreActiveOfficerProjection(' `
    'acting expiry restores any remaining authoritative formal office'
Require-Text $cityPatch 'CanEnterActingGovernorCandidatePool(' `
    'acting governor discovery rejects actors with an existing court office'

Reject-Text $career 'ApplyOfficeRankFloor(' 'appointment-time rank flooring'
Reject-Text $state 'ApplyOfficeRankFloor(' 'state projection rank flooring'

if ($failures.Count -gt 0) {
    throw "Civil-service career gate source guard failures:`n - " +
        ($failures -join "`n - ")
}

Write-Output 'Civil-service career gate source guards passed.'
