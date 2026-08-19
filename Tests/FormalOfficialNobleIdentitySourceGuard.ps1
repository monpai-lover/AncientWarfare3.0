$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$rules = Get-Content -Raw (Join-Path $root 'Code\core\court\CourtOfficerRecordRules.cs')
$court = Get-Content -Raw (Join-Path $root 'Code\core\court\CourtService.cs')

if (-not $rules.Contains('ShouldGrantNobleIdentity')) {
    throw 'Formal official noble identity decision rule is missing'
}
if ($court -notmatch
    'ShouldGrantNobleIdentity\(\s*careerResult\.IsCommitted, pActing\)[\s\S]{0,180}EnsureOfficialShiAndClan') {
    throw 'Noble identity must be granted only after a committed formal appointment'
}
if (([regex]::Matches($court, 'EnsureOfficialShiAndClan')).Count -ne 2) {
    throw 'Noble identity admission must have committed and restore boundaries'
}
if ($court -notmatch
    'if \(!pAppointment\.IsActing\)[\s\S]{0,120}EnsureOfficialShiAndClan') {
    throw 'Formal appointments must repair noble identity during bounded restore'
}
Write-Output 'Formal official noble identity source guard PASS'
