$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$mod = [IO.File]::ReadAllText((Join-Path $repo 'Code/ModClass.cs'))
$race = [IO.File]::ReadAllText((Join-Path $repo 'Code/content/XiaRace.cs'))
$rules = [IO.File]::ReadAllText((Join-Path $repo `
    'Code/content/XiaFertilityRules.cs'))
$decision = [IO.File]::ReadAllText((Join-Path $repo `
    'Code/content/XiaReproductionDecisionContent.cs'))
$traits = [IO.File]::ReadAllText((Join-Path $repo `
    'Code/content/XiaTraits.cs'))
$retirement = [IO.File]::ReadAllText((Join-Path $repo `
    'Code/patch/AW_RetirementPatch.cs'))
$windowRules = [IO.File]::ReadAllText((Join-Path $repo `
    'Code/core/lineage/DynasticReproductionRules.cs'))
$windowService = [IO.File]::ReadAllText((Join-Path $repo `
    'Code/core/lineage/DynasticReproductionService.cs'))
$standingService = [IO.File]::ReadAllText((Join-Path $repo `
    'Code/core/lineage/StandingArmyPeacetimeService.cs'))
$birthPatch = [IO.File]::ReadAllText((Join-Path $repo `
    'Code/patch/AW_DynasticReproductionPatch.cs'))
$pregnancyService = [IO.File]::ReadAllText((Join-Path $repo `
    'Code/core/lineage/NobleHeirPregnancyService.cs'))

if ($decision -notmatch 'DynasticReproductionService\s*\.ReproductionDecisionWeight' -or
    $decision -notmatch '_originalWeight\(pActor\)' -or
    $decision -match 'action_check_launch\s*=' -or
    $decision -match 'birth_rate') {
    throw 'AW3 may only wrap the original decision weight for targeted dynastic succession.'
}
if ($race -match '\("offspring",\s*XiaFertilityRules\.XiaOffspringDelta\)' -or
    $rules -match 'XiaOffspringDelta\s*=\s*[1-9]') {
    throw 'Xia must retain the vanilla offspring cap instead of an AW3 ten-child cap.'
}
if ($traits -notmatch 'heirUrge\.base_stats\["birth_rate"\]\s*=\s*0f' -or
    $retirement -match 'RoyalFertilityService\.RefreshHeirUrge') {
    throw 'The obsolete heir-urge birth multiplier is still active.'
}
if ($windowRules -notmatch 'bool hasLivingSon' -or
    $windowRules -notmatch '!hasLivingSon' -or
    $windowService -notmatch 'HasLivingSon\(' -or
    $windowRules -notmatch 'ShouldObserveReproductionTimeout' -or
    $standingService -notmatch 'ShouldObserveReproductionTimeout') {
    throw 'Dynastic peacetime military release must last until a living son exists.'
}
if ($birthPatch -notmatch 'BabyHelper\),\s*nameof\(BabyHelper\.canMakeBabies\)' -or
    $birthPatch -notmatch 'ReachedPersonalOffspringLimit' -or
    $birthPatch -match 'stats\["offspring"\]' -or
    $pregnancyService -notmatch 'continuationBypass' -or
    $pregnancyService -notmatch 'metaRoom') {
    throw 'Only eligible no-son title lines may bypass the personal offspring cap.'
}

Write-Output 'Dynastic reproduction compatibility source guard passed.'
