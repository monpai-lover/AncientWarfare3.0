$ErrorActionPreference = 'Stop'
function Read-Source([string] $Path) {
    $full = Join-Path (Split-Path $PSScriptRoot -Parent) $Path
    if (-not (Test-Path -LiteralPath $full)) { throw "Missing source file: $Path" }
    Get-Content -LiteralPath $full -Raw
}
function Require-Text([string] $Source, [string] $Needle, [string] $Label) {
    if (-not $Source.Contains($Needle)) { throw "Missing ${Label}: $Needle" }
}
$model = Read-Source 'Code/core/court/LocalCourtReadModel.cs'
$service = Read-Source 'Code/core/court/CourtReadModelService.cs'
foreach ($field in @('MilitaryGenerals', 'MilitaryGeneralTarget',
        'MilitaryGeneralVacancies', 'MilitaryCommandName')) {
    Require-Text $model $field "local military field $field"
}
Require-Text $service 'GeneralService.GetActiveGeneralsForReadModel' 'general projection source'
Require-Text $service 'MilitaryGeneralVacancies' 'military vacancy projection'
Require-Text $service 'AddLocalMilitaryGenerals' 'local military projection'
Write-Host 'Local court military projection source guard passed.'
