$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$windowPath = Join-Path $root 'Code\ui\windows\CourtWindow.cs'
$rulesPath = Join-Path $root 'Code\core\court\CourtPyramidRules.cs'
$window = Get-Content -Raw $windowPath
$rules = Get-Content -Raw $rulesPath
$readModel = Get-Content -Raw (Join-Path $root 'Code\core\court\RegionalGovernmentReadModel.cs')
$readService = Get-Content -Raw (Join-Path $root 'Code\core\court\CourtReadModelService.cs')
$cityCard = Get-Content -Raw (Join-Path $root 'Code\ui\components\CourtCityGovernmentCard.cs')

function Require-Text([string]$content, [string]$needle, [string]$message) {
    if (-not $content.Contains($needle)) {
        throw "Regional government court guard failed: $message"
    }
}

Require-Text $window '_regionSectionLabelPool' 'region headings must be pooled with the court UI'
Require-Text $window 'GroupCityGovernmentsByRegion' 'local-government cards must be grouped by region'
Require-Text $window 'RenderRegionalGovernmentLinks' 'regional governors must connect to member local governments'
Require-Text $window 'RegionSeatCityId' 'card grouping must use the runtime region seat identity'
Require-Text $rules 'BuildRegionalSuperiorLinks' 'local courts must retain the dynamic superior connection'
Require-Text $window 'BuildRegionalSuperiorLinks' 'custom local graphs must render the dynamic superior connection'
Require-Text $readModel 'MemberCount' 'regional read models must expose bounded member counts'
Require-Text $readModel 'LocalLevelTitle' 'regional read models must expose the configured city level'
Require-Text $readService 'AdministrativeLabel' 'regional actor labels must separate place and level'
Require-Text $readService 'aw_court_regional_node_summary' 'regional nodes must combine governor, seat, and member count'
Require-Text $readService 'region.MemberCount' 'central regional nodes must include their member count'
Require-Text $readService 'pModel.RegionMemberCount' 'local superior nodes must include their member count'
Require-Text $window 'aw_court_regional_member_count' 'region headings must display member city count'
Require-Text $cityCard 'LocalLevelTitle' 'city cards must show the city level separately from CityName'

Write-Output 'Regional government court source guard PASS'
