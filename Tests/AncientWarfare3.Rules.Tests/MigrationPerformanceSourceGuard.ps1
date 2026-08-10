$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$authority = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\performance\AWAuthorityCycleService.cs')
$culture = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\lineage\XiaCultureIntegrationService.cs')
$transition = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\lineage\XiaizedFamilyBranchTransitionPersistence.cs')

foreach ($forbidden in @(
        'IntegratedCultureNamingMigrationService',
        'aw3.authority.integrated_culture_naming_migration')) {
    if ($authority.Contains($forbidden)) {
        throw "Removed live-name migration authority path returned: $forbidden"
    }
}
if ($culture.Contains('IntegratedCultureNamingMigrationService.Request')) {
    throw 'Culture trait projection must not request live actor migration.'
}
if ($transition.Contains('WHERE IS_ALIVE=1 AND (SHI_ID=@old')) {
    throw 'Xia branch transition must not migrate every living relative.'
}
if (-not $transition.Contains('WHERE ID=@actor AND IS_ALIVE=1')) {
    throw 'Xia branch transition must target only the new branch founder.'
}

foreach ($removed in @(
        'Code\core\lineage\IntegratedCultureNamingMigrationService.cs',
        'Code\core\lineage\IntegratedCultureNamingMigrationPersistence.cs')) {
    if (Test-Path -LiteralPath (Join-Path $repo $removed)) {
        throw "Removed migration source still exists: $removed"
    }
}

Write-Output 'Future-only Xia naming performance guard passed.'
