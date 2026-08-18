$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Read-Source([string]$path) {
    Get-Content -Raw (Join-Path $root $path)
}
function Require([string]$content, [string]$pattern, [string]$message) {
    if ($content -notmatch $pattern) { throw $message }
}
function Reject([string]$content, [string]$pattern, [string]$message) {
    if ($content -match $pattern) { throw $message }
}

$appointment = Read-Source 'Code/core/court/LocalCourtAppointmentService.cs'
$bureau = Read-Source 'Code/core/court/CityBureauAnnualWorkService.cs'
$career = Read-Source 'Code/core/court/OfficialCareerService.cs'
$state = Read-Source 'Code/core/court/OfficialCareerStateService.cs'

Require $appointment `
    'WHERE KINGDOM_ID=@kingdom AND CITY_ID=@city' `
    'local appointments are not read through a city-scoped active query'
Require $appointment 'AND LAYER=@layer AND ACTIVE=1' `
    'local appointment query is not restricted to active city-layer rows'
Require $appointment 'TryLoadLocalActorIds\(' `
    'local vacancies do not use the bounded local candidate pool'
Require $career 'pAllowLocalLowerQualification' `
    'local lower qualification is not an explicit appointment boundary'
Require $state 'LocalOfficialTermRules\.TermLength\(' `
    'local appointments do not receive finite local terms'
Require $bureau 'LocalCourtAppointmentService\.ReconcileCity\(' `
    'annual city slices do not reconcile real local appointments'
Require $bureau 'OFFICER_ACTOR_IDS' `
    'city bureau state does not persist real officer ids'
Reject $bureau 'CourtBureauRules\.FilledSlots\(' `
    'city bureau filled count is still synthetic'

Write-Host 'Local government SQL source guard passed.'
