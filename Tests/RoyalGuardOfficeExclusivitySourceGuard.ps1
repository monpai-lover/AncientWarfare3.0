$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot

Add-Type -Path (Join-Path $repo 'Code/core/lineage/RoyalGuardOfficeRules.cs')

if ([AncientWarfare3.core.lineage.RoyalGuardOfficeRules]::CanAcceptNewKingship($true, $true)) {
    throw 'A royal guard restored from a legacy save must not become king.'
}
if ([AncientWarfare3.core.lineage.RoyalGuardOfficeRules]::CanBecomeSuccessionCandidate($true)) {
    throw 'An ordinary royal guard must remain outside the succession pool.'
}
if (-not [AncientWarfare3.core.lineage.RoyalGuardOfficeRules]::CanEndLifetimeGuardService('became_heir')) {
    throw 'A formally selected heir must be allowed to leave royal-guard service.'
}
if (-not [AncientWarfare3.core.lineage.RoyalGuardOfficeRules]::CanEndLifetimeGuardService('became_king')) {
    throw 'Legacy guard residue must be removable before accession.'
}

function Require-Text([string]$path, [string]$needle, [string]$message) {
    $fullPath = Join-Path $repo $path
    if (-not [IO.File]::Exists($fullPath)) {
        throw $message
    }
    $content = [IO.File]::ReadAllText($fullPath)
    if (-not $content.Contains($needle)) {
        throw $message
    }
}

Require-Text 'Code/core/lineage/RoyalGuardOfficeRules.cs' `
    'CanAcceptOfficeAppointment' `
    'Royal guard office-exclusivity rules are missing.'
Require-Text 'Code/core/lineage/RoyalGuardOfficeRules.cs' `
    'CanReplaceLifetimeGuardIdentity' `
    'Royal guards need a single rule for rejecting identity-replacement flows.'
Require-Text 'Code/core/court/CourtService.cs' `
    'RoyalGuardOfficeRules.CanAcceptOfficeAppointment' `
    'Court office submission must reject royal guards before persistence.'
Require-Text 'Code/core/court/CourtService.cs' `
    'RoyalGuardOfficeRules.CanAppearInOfficeCandidateList' `
    'Court candidate selection must exclude royal guards.'
Require-Text 'Code/core/court/CourtService.cs' `
    'royal_guard_lifetime' `
    'Restored court projections must clear legacy royal-guard offices.'
Require-Text 'Code/patch/AW_RoyalGuardPatch.cs' `
    'CanAcceptNewCityLeadership' `
    'City leader assignment must reject royal guards before demobilization.'
Require-Text 'Code/patch/AW_EnlistPatch.cs' `
    'RoyalGuardOfficeRules.CanLeaveMilitaryService' `
    'Royal guards must reject all non-death military retirement paths.'
Require-Text 'Code/core/lineage/HeirService.cs' `
    'RoyalGuardOfficeRules.CanBecomeSuccessionCandidate' `
    'Royal guards must not enter the succession candidate pool.'
Require-Text 'Code/core/lineage/HeirService.cs' `
    'RoyalGuardService.ReleaseForRegisteredHeir' `
    'A selected heir must leave royal-guard service before registration.'
Require-Text 'Code/core/lineage/AccessionIdentityService.cs' `
    'RoyalGuardOfficeRules.CanReplaceLifetimeGuardIdentity' `
    'King accession must reject royal guards before clearing their military state.'
Require-Text 'Code/core/lineage/AccessionIdentityService.cs' `
    'RoyalGuardService.ReleaseForRegisteredHeir' `
    'Accession must clean legacy guard residue for the registered heir.'
Require-Text 'Code/patch/AW_RoyalGuardPatch.cs' `
    'RoyalGuardService.ReleaseForRegisteredHeir' `
    'The setKing compatibility gate must release only a registered heir.'
Require-Text 'Code/core/lineage/RoyalGuardService.cs' `
    'RemoveGuardFromRoster(pKingdom, pActor)' `
    'Heir release must remove the actor from the destination guard roster.'
Require-Text 'Code/core/lineage/RoyalGuardService.cs' `
    'RemoveFromAnyArmy(pActor)' `
    'Heir release must clear every stale army assignment.'
Require-Text 'Code/core/lineage/RoyalGuardService.cs' `
    'IsKingGuardJob(pActor)' `
    'Heir release must verify the guard citizen job was removed.'
Require-Text 'Code/core/lineage/KingdomIdentityContinuityService.cs' `
    'RoyalGuardOfficeRules.CanReplaceLifetimeGuardIdentity' `
    'Restoration accession must reject royal guards before clearing their military state.'
Require-Text 'Code/core/lineage/RoyalAsylumService.cs' `
    'RoyalGuardOfficeRules.CanReplaceLifetimeGuardIdentity' `
    'Royal asylum evacuation must not replace a royal guard identity.'
Require-Text 'Code/core/lineage/SlaveService.cs' `
    'RoyalGuardOfficeRules.CanReplaceLifetimeGuardIdentity' `
    'Royal guards must not be converted into a second exclusive identity.'
Require-Text 'Code/core/lineage/RoyalGuardService.cs' `
    'RoyalGuardOfficeRules.CanTrimLifetimeGuard' `
    'Royal guard maintenance must not dismiss lifetime guards for over-strength.'

Write-Output 'Royal guard office-exclusivity source guards passed.'
