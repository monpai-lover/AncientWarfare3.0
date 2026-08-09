$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$rules = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\court\CourtManualAppointmentRules.cs')
$service = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\court\CourtService.cs')
$node = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\ui\items\CourtActorNodeView.cs')
$window = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\ui\windows\CourtAppointmentWindow.cs')
$locales = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Locales\aw3_court.csv')

foreach ($requirement in @(
    @('CourtManualAppointmentResult\.AppointmentNotAllowed',
        'AppointmentNotAllowed result'),
    @('CourtManualAppointmentRules\.\s*CanUseManualAppointment\s*\(',
        'manual appointment permission rule'),
    @('CourtManualAppointmentRules\.\s*CanOpenVacancyAppointment\s*\(',
        'vacancy appointment gate')
)) {
    if (($service + $node + $rules) -notmatch $requirement[0]) {
        throw "Western manual appointment integration is missing $($requirement[1])"
    }
}

if (-not $window.Contains(
        'case CourtManualAppointmentResult.AppointmentNotAllowed:')) {
    throw 'Western appointment window is missing the permission result.'
}
if (-not $locales.Contains('aw_court_appointment_not_allowed')) {
    throw 'Western court appointment permission localization is missing.'
}

Write-Output 'Western court manual appointment source guard passed.'
