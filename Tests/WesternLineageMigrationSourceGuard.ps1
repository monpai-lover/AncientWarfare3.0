$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $root `
    'Code/core/lineage/WesternLineageMigrationService.cs'
if (-not (Test-Path -LiteralPath $servicePath)) {
    throw 'Missing WesternLineageMigrationService.'
}
$service = Get-Content -LiteralPath $servicePath -Raw -Encoding UTF8
foreach ($token in @(
    'ProcessAuthorityCycle'
    'Request(Kingdom pKingdom)'
    'PendingKingdomIds'
    'WesternLineageAdmissionService.TryEnsure'
    'AWCultureNamingTraditionService.ResolveForActorReadOnly'
    'NamingProfileId.Western'
    'NamingProfileId.OrcNomadic'
    'HeirService.PeekRegisteredHeir'
    'LineageKeys.COURT_OFFICE_ID'
)) {
    if ($service -notmatch [regex]::Escape($token)) {
        throw "Western lineage migration is missing: $token"
    }
}
if ($service -match 'NamingProfileId\.Monkey' -or
    $service -match 'MapBox\.Update' -or
    $service -match 'World\.world\.units\.units_only_alive') {
    throw 'Western migration must remain kingdom-bounded and exclude monkey/global scans.'
}

$authority = Get-Content -LiteralPath (Join-Path $root `
    'Code/core/performance/AWAuthorityCycleService.cs') -Raw -Encoding UTF8
if ($authority -notmatch 'WesternLineageMigrationService\.ProcessAuthorityCycle' -or
    $authority -notmatch 'WesternLineageMigrationService\.Reset') {
    throw 'Western lineage migration is not owned by the authority cycle.'
}

$restore = Get-Content -LiteralPath (Join-Path $root `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs') -Raw -Encoding UTF8
if ($restore -notmatch 'WesternLineageMigrationService\.Request') {
    throw 'World restore does not request old-save western lineage migration.'
}

Write-Output 'Western lineage migration source guard passed.'
