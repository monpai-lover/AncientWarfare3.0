$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$rank = Get-Content -Raw (Join-Path $root 'Code\core\court\OfficialCareerRankRules.cs')
$qualification = Get-Content -Raw (Join-Path $root 'Code\core\court\CivilServiceQualificationService.cs')
$career = Get-Content -Raw (Join-Path $root 'Code\core\court\OfficialCareerStateService.cs')
$court = Get-Content -Raw (Join-Path $root 'Code\core\court\CourtService.cs')
$local = Get-Content -Raw (Join-Path $root 'Code\core\court\LocalCourtAppointmentService.cs')
$cityLeader = Get-Content -Raw (Join-Path $root 'Code\patch\AW_CityLeaderPatch.cs')
foreach ($pair in @(
    @($rank, 'RequiredRankForLocalOfficeGrade'),
    @($rank, 'ResolveInitialLocalAppointmentRank'),
    @($qualification, 'RequiredRankForLocalOfficeGrade'),
    @($qualification, 'pLayer == CourtOfficeLayer.City'),
    @($qualification, 'ShouldUseVacancyFallback'),
    @($career, 'ResolveInitialLocalAppointmentRank'),
    @($career, 'ResolveLocalVacancyPromotionRank'),
    @($career, 'ResolveVacancyPromotionRank'),
    @($career, 'OfficeGradeForOffice(Kingdom pKingdom'),
    @($career, 'CustomCourtRuntime.TryGetLocalTemplate'),
    @($career, 'CustomCourtRuntime.TryGetSnapshot'),
    @($career, 'bool localOffice'),
    @($court, 'pAllowVacancyPromotion: false'),
    @($court, 'pAllowVacancyPromotion: true'),
    @($local, 'pAllowVacancyPromotion: false'),
    @($local, 'pAllowVacancyPromotion: true'),
    @($local, 'CanUseCandidate')
)) {
    if (-not $pair[0].Contains($pair[1])) { throw "Missing nine-rank token: $($pair[1])" }
}
if ($career.Contains('regional_governor')) { throw 'Do not persist a regional governor appointment office' }
if ($career -notmatch
    'if \(!examinationSystem\)[\s\S]{0,1600}ResolveInitialLocalAppointmentRank') {
    throw 'No-examination nine-rank appointments must use local office rank floors'
}
if ($local -notmatch
    'LoadAllCandidates[\s\S]{0,2200}CanUseCandidateFacts\(actor, pKingdom\)') {
    throw 'The local world roster must preserve hard-valid legacy candidates'
}
if (-not $local.Contains('examinationEnabled: false')) {
    throw 'The local hard-validity filter must retain actor identity checks'
}
foreach ($token in @(
    'RoyalGuardOfficeRules.CanAppearInOfficeCandidateList',
    'RoyalAsylumRules.CanPerformProtectedRole',
    'hasTrait("madness")'
)) {
    if (-not $local.Contains($token)) {
        throw "Missing local hard-validity filter: $token"
    }
}
if ($cityLeader -notmatch
    'TryGetRealmLeader[\s\S]{0,500}pAllowVacancyPromotion: false[\s\S]{0,500}pAllowVacancyPromotion: true') {
    throw 'City governor selection must try strict candidates before vacancy fallback'
}
Write-Output 'Regional government nine-rank source guard PASS'
