$ErrorActionPreference = 'Stop'

function Read-Source([string]$Path) {
    return [System.IO.File]::ReadAllText(
        [System.IO.Path]::Combine($PSScriptRoot, '..', $Path))
}

function Require-Text([string]$Text, [string]$Needle, [string]$Message) {
    if (-not $Text.Contains($Needle)) { throw "Missing $Message" }
}

function Reject-Text([string]$Text, [string]$Needle, [string]$Message) {
    if ($Text.Contains($Needle)) { throw "Found forbidden $Message" }
}

$service = Read-Source 'Code/core/court/LocalCourtAppointmentService.cs'

Require-Text $service 'LocalLowOfficeVacancyRules.CandidateTier(' `
    'candidate tier ordering before score tie-breakers'
Require-Text $service 'LineageKeys.SHI_ID' `
    'shi identity detection for local candidates'
Require-Text $service 'hasClan()' `
    'native clan identity detection for local candidates'
Reject-Text $service 'if (++inspected > CandidateScanLimit) break;' `
    'permanent first-page candidate cutoff'

Write-Output 'Local court candidate tier source guard passed.'
