$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$service = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\court\CourtService.cs')
$qualification = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\court\CivilServiceQualificationService.cs')
$persistence = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\court\CivilServiceExamPersistence.cs')

foreach ($requirement in @(
    @('CaptureManualAppointmentQualifications\(',
        'manual appointment qualification snapshot'),
    @('qualification_by_actor_id',
        'scan-owned qualification lookup'),
    @('pQualificationsCaptured',
        'candidate projection snapshot boundary'),
    @('LoadLatestQualificationsForActors\(',
        'batched qualification persistence query')
)) {
    if (($service + $qualification + $persistence) -notmatch $requirement[0]) {
        throw "Court appointment scan performance guard is missing $($requirement[1])"
    }
}

$projectionStart = $service.IndexOf(
    'internal static bool TryProjectManualAppointmentCandidate(')
$projectionEnd = $service.IndexOf(
    'internal static CourtManualAppointmentResult TryManualAppointment(',
    $projectionStart)
if ($projectionStart -lt 0 -or $projectionEnd -le $projectionStart) {
    throw 'Court appointment candidate projection boundary cannot be located.'
}
$projection = $service.Substring($projectionStart,
    $projectionEnd - $projectionStart)
if ($projection -match 'LoadOrRepair\(') {
    throw 'Court appointment candidate projection must not perform per-actor qualification repair.'
}

Write-Output 'Court appointment candidate scan performance source guard passed.'
