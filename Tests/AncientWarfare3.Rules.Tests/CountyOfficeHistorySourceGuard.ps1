$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Read-Source([string] $relativePath) {
    return [IO.File]::ReadAllText((Join-Path $repo $relativePath))
}

function Require-Text([string] $source, [string] $needle, [string] $message) {
    if (-not $source.Contains($needle)) {
        throw $message
    }
}

$model = Read-Source 'Code/core/court/OfficialCareerHistoryModels.cs'
$query = Read-Source 'Code/core/court/OfficialCareerHistoryQuery.cs'
$node = Read-Source 'Code/ui/items/CourtActorNodeView.cs'
$window = Read-Source 'Code/ui/windows/CourtOfficeHistoryWindow.cs'

Require-Text $model 'public long CountyId { get; }' `
    'office history scope must carry a county id'
Require-Text $query 'IFNULL(COUNTY_ID,-1)=@county' `
    'county office history SQL must filter the exact county seat'
Require-Text $node 'long countyId = pNode.CountyId;' `
    'county node history action must pass its county id'
Require-Text $window 'ResolveCountyName(_countyId)' `
    'county office history must display the exact county name'

Write-Output 'County office history source guard passed.'
