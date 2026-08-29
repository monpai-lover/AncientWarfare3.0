$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$rank = Get-Content -Raw (Join-Path $root 'Code\core\court\OfficialCareerRankRules.cs')
$qualification = Get-Content -Raw (Join-Path $root 'Code\core\court\CivilServiceQualificationService.cs')
$career = Get-Content -Raw (Join-Path $root 'Code\core\court\OfficialCareerStateService.cs')
$court = Get-Content -Raw (Join-Path $root 'Code\core\court\CourtService.cs')
$local = Get-Content -Raw (Join-Path $root 'Code\core\court\LocalCourtAppointmentService.cs')
$cityLeader = Get-Content -Raw (Join-Path $root 'Code\patch\AW_CityLeaderPatch.cs')
$cityGovernor = Get-Content -Raw (Join-Path $root 'Code\core\court\CityGovernorProjectionRepairService.cs')
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
# The intent here is that the local roster still applies the hard-validity
# facts gate. The original anchor was LoadAllCandidates, a helper that was
# retired when local filling moved onto the shared CourtCandidateSession
# (CourtVacancySourceGuardTests now asserts that helper stays gone). Re-anchor
# on the two pools the session hands out; both must be built through the gate.
foreach ($pool in @('StrictCandidates', 'FactsCandidates')) {
    if ($local -notmatch
        "$pool\([\s\S]{0,200}CanUseCandidateFacts\(actor, pKingdom\)") {
        throw 'The local world roster must preserve hard-valid legacy candidates'
    }
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
# City governor selection kept its strict-then-fallback shape but moved out of
# AW_CityLeaderPatch.cs; TryGetRealmLeader no longer exists anywhere in the
# tree. Anchor on CityGovernorProjectionRepairService.Apply, where the strict
# pass is the default-argument call and the fallback is gated on !formal.
if ($cityGovernor -notmatch
    'bool formal =[\s\S]{0,400}bool vacancyFallback = !formal &&[\s\S]{0,400}pAllowVacancyPromotion: true') {
    throw 'City governor selection must try strict candidates before vacancy fallback'
}
if ($cityLeader -match 'pAllowVacancyPromotion') {
    throw 'City leader patch must not regain its own appointment selection'
}
Write-Output 'Regional government nine-rank source guard PASS'
