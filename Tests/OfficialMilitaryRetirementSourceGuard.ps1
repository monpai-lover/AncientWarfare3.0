$ErrorActionPreference = 'Stop'
function Read-Source([string] $Path) {
    $full = Join-Path (Split-Path $PSScriptRoot -Parent) $Path
    if (-not (Test-Path -LiteralPath $full)) { throw "Missing source file: $Path" }
    Get-Content -LiteralPath $full -Raw
}
function Require-Text([string] $Source, [string] $Needle, [string] $Label) {
    if (-not $Source.Contains($Needle)) { throw "Missing ${Label}: $Needle" }
}
$service = Read-Source 'Code/core/court/OfficialMilitaryRetirementService.cs'
$general = Read-Source 'Code/core/lineage/GeneralService.cs'
$annual = Read-Source 'Code/core/policy/KingdomAnnualWorkService.cs'
Require-Text $service 'actor.getAge()' 'original actor age source'
Require-Text $service 'SoldierRetirementRules.HardRetirementAge' '75 year threshold'
Require-Text $service 'OfficialCareerStateService.ClearCurrentOffice' 'official release'
Require-Text $service 'GeneralService.RetireForAge' 'general release'
Require-Text $annual 'OfficialMilitaryRetirementService.OnKingdomYear' 'annual retirement stage'
Require-Text $general 'internal static void RetireForAge' 'general retirement hook'
Write-Host 'Official and military retirement source guard passed.'
