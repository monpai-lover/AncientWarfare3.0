$ErrorActionPreference = 'Stop'

function Read-Source([string]$Path) {
    return [System.IO.File]::ReadAllText(
        [System.IO.Path]::Combine($PSScriptRoot, '..', $Path))
}

function Require-Text([string]$Text, [string]$Needle, [string]$Message) {
    if (-not $Text.Contains($Needle)) { throw "Missing $Message" }
}

$qualification = Read-Source 'Code/core/court/CivilServiceQualificationService.cs'
$service = Read-Source 'Code/core/court/OfficialCareerService.cs'

Require-Text $qualification 'LocalLowOfficeVacancyRules.CanUseUnqualifiedFallback(' `
    'qualification gate for the lowest local vacancy fallback'
Require-Text $qualification 'OfficeGradeForOffice(' `
    'resolved office grade before qualification checks'
Require-Text $service 'pAllowLocalLowerQualification' `
    'appointment forwards the local fallback flag'

Write-Output 'Local low-office vacancy source guard passed.'
