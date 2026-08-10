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
Forbid $service 'NativeSiniticIdentityMigration' `
    'actor projection must not invoke the removed native Sinitic identity migration'

$lineageService = 'Code/core/lineage/LineageService.cs'
Forbid $lineageService 'UsesNativeSiniticGenealogy' `
    'lineage service must not admit the five native Sinitic species as genealogy actors'
Forbid $lineageService 'NativeSiniticIdentityMigration' `
    'lineage service must not invoke the removed native Sinitic identity migration'

foreach ($migration in @(
        'Code/core/lineage/NativeSiniticIdentityMigrationService.cs',
        'Code/core/lineage/NativeSiniticIdentityMigrationRules.cs',
        'Code/core/lineage/NativeSiniticIdentityMigrationPersistence.cs')) {
    if ([IO.File]::Exists((Join-Path $root $migration))) {
        $failures.Add("removed native Sinitic migration source still exists: $migration")
    }
}

$manual = 'Code/core/naming/ActorManualRenameService.cs'
Require $manual 'profile == NamingProfileId.NativeSinitic' `
    'manual rename does not use the surname-first editor for the new profile'

$birth = 'Code/patch/AW_BirthPatch.cs'
Require $birth 'IsNativeXiaCultureActor(__instance)' `
    'actor creation boundary changed beyond Xia/civilized-monkey lifecycle'
$clan = 'Code/patch/AW_ClanEventPatch.cs'
Require $clan 'IsNativeXiaCultureActor(__instance)' `
    'clan-change boundary changed beyond Xia/civilized-monkey lifecycle'
$archive = 'Code/core/lineage/LineageArchiveWriter.cs'
Require $archive 'IsNativeXiaCultureActor(pActor)' `
    'lineage archive boundary changed beyond Xia/civilized-monkey lifecycle'
$promotion = 'Code/patch/AW_PromotionPatch.cs'
Forbid $promotion 'EnsureNativeSiniticActorIdentity' `
    'promotion must not trigger native Sinitic identity migration'

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
