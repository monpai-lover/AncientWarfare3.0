$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$persistencePath = Join-Path $root 'Code\core\schools\GuestOfficeEndPersistence.cs'
$courtPath = Join-Path $root 'Code\core\court\CourtService.cs'
$rulesPath = Join-Path $root 'Code\core\schools\GuestOfficePersistenceRules.cs'

foreach ($path in @($persistencePath, $courtPath, $rulesPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required source file is missing: $path"
    }
}

$persistence = Get-Content -LiteralPath $persistencePath -Raw
$court = Get-Content -LiteralPath $courtPath -Raw
$rules = Get-Content -LiteralPath $rulesPath -Raw

foreach ($token in @(
    'GuestOfficeEndRecoveryRules.CanCloseMissingCareer(',
    'CareerAlreadyClosed',
    'ReadActiveCentralCareers(',
    'if (current.CareerAlreadyClosed)',
    'StageAffiliation(pDb, pTransaction, current);')) {
    if (-not $persistence.Contains($token)) {
        throw "Guest-office orphan recovery is missing $token"
    }
}

if (-not $rules.Contains('public static class GuestOfficeEndRecoveryRules')) {
    throw 'Guest-office orphan recovery rules are missing.'
}

$endStart = $persistence.IndexOf('internal static GuestOfficeEndResult EndInTransaction(', [StringComparison]::Ordinal)
$refreshStart = $persistence.IndexOf('private static GuestOfficeEndRequest RefreshEndRequestForTransaction(', [StringComparison]::Ordinal)
if ($endStart -lt 0 -or $refreshStart -le $endStart) {
    throw 'Unable to inspect guest-office end transaction.'
}

$endTransaction = $persistence.Substring($endStart, $refreshStart - $endStart)
$guardIndex = $endTransaction.IndexOf('if (current.CareerAlreadyClosed)', [StringComparison]::Ordinal)
$closeIndex = $endTransaction.IndexOf('OfficialCareerPersistence.StageClose(', [StringComparison]::Ordinal)
if ($guardIndex -lt 0 -or $closeIndex -lt 0 -or $guardIndex -gt $closeIndex) {
    throw 'The affiliation-only recovery path must bypass official career closure.'
}

$reformStart = $court.IndexOf('if (layer == CourtOfficeLayer.Central && tierOffices.Count > 0', [StringComparison]::Ordinal)
$reformEnd = $court.IndexOf('SyncSchoolTrait(actor, active: true);', $reformStart, [StringComparison]::Ordinal)
if ($reformStart -lt 0 -or $reformEnd -le $reformStart) {
    throw 'Unable to inspect court reform validation branch.'
}

$reform = $court.Substring($reformStart, $reformEnd - $reformStart)
$guestEnd = 'SchoolGuestOfficeService.EndGuestOfficer(actor, pKingdom,'
if (-not $reform.Contains($guestEnd)) {
    throw 'Court reform must route serving guests through the guest-office end state machine.'
}

$guestEndIndex = $reform.IndexOf($guestEnd, [StringComparison]::Ordinal)
$clearIndex = $reform.IndexOf('ClearOfficer(actor, "reform")', [StringComparison]::Ordinal)
if ($clearIndex -ge 0 -and $clearIndex -lt $guestEndIndex) {
    throw 'Court reform clears a guest before its durable guest-office end is scheduled.'
}

Write-Host 'Guest-office reform lifecycle source guard passed.'
