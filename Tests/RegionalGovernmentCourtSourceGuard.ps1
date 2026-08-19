$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$windowPath = Join-Path $root 'Code\ui\windows\CourtWindow.cs'
$rulesPath = Join-Path $root 'Code\core\court\CourtPyramidRules.cs'
$window = Get-Content -Raw $windowPath
$rules = Get-Content -Raw $rulesPath

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

Write-Output 'Regional government court source guard PASS'
