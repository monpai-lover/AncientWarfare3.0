$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$service = Get-Content -Raw (Join-Path $root 'Code/core/court/CourtService.cs')
$career = Get-Content -Raw (Join-Path $root 'Code/core/court/OfficialCareerStateService.cs')
$profile = Get-Content -Raw (Join-Path $root 'Code/core/court/WesternCourtProfile.cs')
$cityOffice = Get-Content -Raw (Join-Path $root 'Code/core/court/CourtCityOfficeRules.cs')
$ids = Get-Content -Raw (Join-Path $root 'Code/core/court/CourtIds.cs')
$migration = Get-Content -Raw (Join-Path $root 'Code/core/court/WesternCourtMigrationRules.cs')

foreach ($needle in @('GetActiveOfficers(pKingdom, 96)',
                      'WesternCourtElectionService.QueueKingdomVacancies',
                      'WesternCourtElectionRules.CanQueueVacancy',
                      'KingdomPolicyEffectService.Read(pKingdom)',
                      'MilitaryOfficeIdsForCurrentProfile')) {
    if (-not $service.Contains($needle)) { throw "missing western lifecycle contract: $needle" }
}
foreach ($needle in @('public static string Resolve(', 'WestMayor', 'WestCount')) {
    if (-not $cityOffice.Contains($needle)) { throw "missing city office projection contract: $needle" }
}
if ($service.Contains('CourtInstitutionId.WesternElective') -or
    $service.Contains('CourtInstitutionId.WesternRoyalDirect')) {
    throw 'appointment behavior must not be selected from legacy institution IDs'
}
foreach ($needle in @('WESTERN_MAYOR_CYCLE_END_YEAR',
                      'WesternMayorTermRules',
                      'GovernorRotationRuntimeScope')) {
    if (-not $career.Contains($needle)) { throw "missing mayor lifecycle contract: $needle" }
}
foreach ($needle in @('WesternBureaucratic', 'WesternFeudalBureaucratic',
                      'WestMayor', 'WestCount', 'WestRoyalConstable')) {
    if (-not $profile.Contains($needle)) { throw "missing western catalog contract: $needle" }
}
if (-not $ids.Contains('WestRoyalChamberlain') -or
    -not $ids.Contains('WestRoyalConstable') -or
    -not $migration.Contains('NormalizeOfficeId') -or
    -not $migration.Contains('WestRoyalChamberlain') -or
    -not $migration.Contains('WestRoyalConstable')) {
    throw 'legacy Royal Chamberlain migration contract is missing'
}
foreach ($forbidden in @('GetActorsInKingdom', 'GetAllActors', 'GetObjects')) {
    if ($career.Contains($forbidden)) { throw "western career lifecycle must not use actor-wide scan: $forbidden" }
}
Write-Output 'western court office lifecycle source guard passed'
