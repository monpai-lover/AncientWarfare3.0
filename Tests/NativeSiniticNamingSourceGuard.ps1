param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Require([string]$path, [string]$needle, [string]$message) {
    $source = [IO.File]::ReadAllText((Join-Path $root $path))
    if (-not $source.Contains($needle)) { $failures.Add($message) }
}

function Forbid([string]$path, [string]$needle, [string]$message) {
    $source = [IO.File]::ReadAllText((Join-Path $root $path))
    if ($source.Contains($needle)) { $failures.Add($message) }
}

$service = 'Code/core/naming/AWLocalizedNameService.cs'
Require $service 'IsNativeSiniticActor(pActor)' `
    'localized actor naming does not identify native Sinitic actors'
Require $service 'AWNativeSiniticNamePartsRules.Resolve(' `
    'localized actor naming does not preserve complete current-library words'
Require $service 'pParts.DisplayName' `
    'localized actor naming does not project surname before complete given name'
Require $service 'NativeSiniticIdentityMigrationService.TryRepair(pActor)' `
    'actor projection does not lazily repair an existing native Sinitic identity'
$serviceSource = [IO.File]::ReadAllText((Join-Path $root $service))
if ([regex]::Matches($serviceSource,
        'NativeSiniticIdentityMigrationService\.TryRepair\(pActor\)').Count -lt 2) {
    $failures.Add('promotion identity checks can bypass native Sinitic migration')
}

$lineageService = 'Code/core/lineage/LineageService.cs'
Require $lineageService 'NativeSiniticIdentityMigrationService.TryRepair(pActor)' `
    'family-tree display boundaries do not trigger native Sinitic migration'

$migration = 'Code/core/lineage/NativeSiniticIdentityMigrationService.cs'
Require $migration '[ThreadStatic]' `
    'native Sinitic migration has no recursion guard'
Require $migration 'MigrationVersionKey' `
    'native Sinitic migration repeats branch reads after successful repair'
Require $migration 'GetShiBranchInfo(shiId)' `
    'native Sinitic migration does not read exactly one existing branch'
Require $migration 'pActor.data.get(LineageKeys.NAMING_PROFILE' `
    'branchless actors with a legacy Western profile are not repairable'
Forbid $migration 'World.world.units' `
    'native Sinitic migration scans the world actor collection'
Forbid $migration 'Update(' `
    'native Sinitic migration is registered as periodic update work'
Forbid $migration 'OnWorldLoaded' `
    'native Sinitic migration performs synchronous load-wide repair'

$migrationPersistence = `
    'Code/core/lineage/NativeSiniticIdentityMigrationPersistence.cs'
Require $migrationPersistence 'BeginTransaction(IsolationLevel.Serializable)' `
    'native Sinitic branch migration is not transactional'
Require $migrationPersistence 'WHERE SHI_ID=@shi AND NAMING_PROFILE=@profile' `
    'native Sinitic branch migration does not guard the prior profile'

$manual = 'Code/core/naming/ActorManualRenameService.cs'
Require $manual 'profile == NamingProfileId.NativeSinitic' `
    'manual rename does not use the surname-first editor for the new profile'

$birth = 'Code/patch/AW_BirthPatch.cs'
Require $birth 'UsesNativeSiniticGenealogy(__instance)' `
    'actor creation does not enter the native Sinitic genealogy lifecycle'
$clan = 'Code/patch/AW_ClanEventPatch.cs'
Require $clan 'UsesNativeSiniticGenealogy(__instance)' `
    'clan changes do not refresh native Sinitic genealogy'
$archive = 'Code/core/lineage/LineageArchiveWriter.cs'
Require $archive 'UsesNativeSiniticGenealogy(pActor)' `
    'lineage archive does not recognize native Sinitic genealogy'
$promotion = 'Code/patch/AW_PromotionPatch.cs'
Require $promotion 'EnsureNativeSiniticActorIdentity(pActor)' `
    'king promotion does not ensure the current-library family identity'

$policy = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/policy/CivMonkeyPolicyRules.cs'))
foreach ($id in @('civ_dog', 'civ_fox', 'civ_lemon_man', 'civ_rabbit',
        'civ_turtle')) {
    if ($policy.Contains($id)) {
        $failures.Add("native Sinitic naming leaked into monkey policy: $id")
    }
}

$rules = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/naming/AWNativeSiniticSpeciesRules.cs'))
foreach ($forbidden in @('Surnames', 'GivenNames', 'SurnamePool',
        'GivenNamePool')) {
    if ($rules.Contains($forbidden)) {
        $failures.Add("native Sinitic species rules contain forbidden name pool: $forbidden")
    }
}

$creatures = [IO.File]::ReadAllText((Join-Path $root `
    'name_generators/default/creatures.json'))
foreach ($id in @('civ_dog_name', 'civ_fox_name', 'civ_lemon_man_name',
        'civ_rabbit_name', 'civ_turtle_name')) {
    if (-not $creatures.Contains('"' + $id + '"')) {
        $failures.Add("current actor generator is missing: $id")
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Native Sinitic naming source failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Native Sinitic naming source guards passed.'
