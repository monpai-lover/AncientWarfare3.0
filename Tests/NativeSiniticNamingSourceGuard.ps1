param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Require([string]$path, [string]$needle, [string]$message) {
    $source = [IO.File]::ReadAllText((Join-Path $root $path))
    if (-not $source.Contains($needle)) { $failures.Add($message) }
}

$service = 'Code/core/naming/AWLocalizedNameService.cs'
Require $service 'IsNativeSiniticActor(pActor)' `
    'localized actor naming does not identify native Sinitic actors'
Require $service 'AWNativeSiniticNamePartsRules.Resolve(' `
    'localized actor naming does not preserve complete current-library words'
Require $service 'pParts.DisplayName' `
    'localized actor naming does not project surname before complete given name'

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
