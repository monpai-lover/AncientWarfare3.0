$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content -Raw (Join-Path $root 'Code/core/policy/KingdomPolicyEffectService.cs')
$policy = Get-Content -Raw (Join-Path $root 'Code/core/policy/KingdomPolicyService.cs')
$economy = Get-Content -Raw (Join-Path $root 'Code/core/policy/CityEconomyService.cs')
$relief = Get-Content -Raw (Join-Path $root 'Code/core/policy/KingdomFoodReliefService.cs')
$occupation = Get-Content -Raw (Join-Path $root 'Code/patch/AW_CityOccupationAccelerationPatch.cs')
$garrison = Get-Content -Raw (Join-Path $root 'Code/core/lineage/WartimeGarrisonService.cs')
$centralization = Get-Content -Raw (Join-Path $root 'Code/core/lineage/CentralizationService.cs')
$courtInstitution = Get-Content -Raw (Join-Path $root 'Code/core/court/CourtInstitutionService.cs')
$court = Get-Content -Raw (Join-Path $root 'Code/core/court/CourtService.cs')
$patchPath = Join-Path $root 'Code/patch/AW_WesternTechnologyEffectPatch.cs'
if (-not (Test-Path $patchPath)) {
    throw 'Missing western equipment technology effect patch.'
}
$equipment = Get-Content -Raw $patchPath

function Require-Text([string] $text, [string] $needle, [string] $message) {
    if (-not $text.Contains($needle)) { throw $message }
}

function Forbid-Text([string] $text, [string] $needle, [string] $message) {
    if ($text.Contains($needle)) { throw $message }
}

Require-Text $service 'KingdomPolicyEffects Read(' 'Policy effects have no cached runtime read API.'
Require-Text $service 'KingdomPolicyEffectRules.Resolve' 'Runtime policy effects do not use the pure resolver.'
Require-Text $service 'void Invalidate(' 'Policy effect cache has no kingdom invalidation path.'
Require-Text $service 'void ClearRuntime(' 'Policy effect cache has no world reset path.'
Require-Text $policy 'KingdomPolicyEffectService.Invalidate(pKingdom)' 'Policy completion or restore does not invalidate policy effects.'
Require-Text $policy 'KingdomPolicyEffectService.ClearRuntime()' 'Policy world reset does not clear policy effects.'
Require-Text $economy 'KingdomPolicyEffectService.Read(pKingdom)' 'City economy does not consume policy effects.'
Require-Text $relief 'OrganizedFamineTransfers' 'Famine transfer is not gated by organized storage knowledge.'
Require-Text $occupation 'OccupationResistance' 'Occupation capture does not consume resistance effects.'
Require-Text $garrison 'GarrisonMultiplier' 'Wartime garrison targets do not consume defense effects.'
Require-Text $policy 'VassalAdministrationUnlocked' 'Western enfeoffment administration is not connected to the authority gate.'
Require-Text $centralization 'KingdomPolicyEffectService.Read(pKingdom)' 'Western direct-rule effects are not exposed through centralization state.'
Require-Text $courtInstitution 'effects.ElectiveTermsUnlocked &&' 'Elective government is not gated by its enabling technology.'
Require-Text $courtInstitution 'effects.FeudalRetainersUnlocked &&' 'Feudal government is not gated by its enabling technology.'
Require-Text $court 'effects.FeudalRetainersUnlocked' 'Feudal-retainer knowledge does not influence landed court appointments.'
Require-Text $equipment 'tryToCraftRandomEquipment' 'Western workshop output has no bounded extra crafting attempt.'
Require-Text $equipment 'ref int pTries' 'Equipment quality does not adjust crafting tries.'

Forbid-Text $service 'OperatingDB' 'Policy effect hot reads must not query SQLite.'
Forbid-Text $service 'SQLite' 'Policy effect hot reads must not reference SQLite.'
Forbid-Text $equipment 'OperatingDB' 'Equipment patch must not query SQLite.'
Forbid-Text $equipment 'SQLite' 'Equipment patch must not reference SQLite.'
Forbid-Text $equipment 'BehMakeItem.execute(' 'Equipment patch must not recursively invoke BehMakeItem.execute.'

Write-Host 'Western policy effect source guard passed.'
