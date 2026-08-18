$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Read-Source([string]$path) {
    Get-Content -Raw (Join-Path $root $path)
}

function Require([string]$content, [string]$pattern, [string]$message) {
    if ($content -notmatch $pattern) { throw $message }
}

function Reject([string]$content, [string]$pattern, [string]$message) {
    if ($content -match $pattern) { throw $message }
}

$readModel = Read-Source 'Code/core/court/LocalCourtReadModel.cs'
$readService = Read-Source 'Code/core/court/CourtReadModelService.cs'
$pyramidRules = Read-Source 'Code/core/court/CourtPyramidRules.cs'
$courtWindow = Read-Source 'Code/ui/windows/CourtWindow.cs'
$cityCard = Read-Source 'Code/ui/components/CourtCityGovernmentCard.cs'
$actorNode = Read-Source 'Code/ui/items/CourtActorNodeView.cs'
$cityPatch = Read-Source 'Code/patch/AW_CityTabPatch.cs'
$workflow = Read-Source 'Code/ui/windows/CustomCourtWorkflowWindow.cs'
$runtime = Read-Source 'Code/core/court/CustomCourtRuntime.cs'

Require $readModel 'TemplateId' `
    'local court read model does not expose the selected stable template id'
Require $readModel 'CityTypeName' `
    'local court read model does not expose the template name as city type'
Require $readService 'BuildLocal\(' `
    'court read service cannot build a selected city government'
Require $readService 'TryGetLocalTemplate\(' `
    'local court read service does not resolve the city template'
Require $pyramidRules 'BuildLocalOrthogonalLinks\(' `
    'city context has no hierarchy-link path for local offices'
Require $courtWindow 'BuildLocalOrthogonalLinks\(' `
    'shared court window does not render built-in local hierarchy links'
Require $courtWindow 'public static void OpenCity\(' `
    'shared court window has no city-context entry'
Require $courtWindow 'CourtCityGovernmentCard' `
    'national court view does not use city-government cards'
Require $courtWindow 'AWStringDropdown' `
    'city court context has no shared template selector'
Require $cityCard 'CourtActorNodeView' `
    'city-government cards do not reuse the shared actor-node view'
Require $actorNode 'CourtOfficeHistoryWindow' `
    'shared actor nodes no longer preserve the office-history path'
Require $cityPatch 'CourtWindow\.OpenCity\(' `
    'city window does not open the shared local court context'
Require $workflow 'CustomLocalCourtTemplateRules' `
    'custom court workflow does not manage local templates through shared rules'
Require $runtime 'TryGetLocalTemplate\(' `
    'runtime lacks the shared city-template resolution path'

$allWindowSources = (Get-ChildItem (Join-Path $root 'Code/ui/windows') `
    -Filter '*.cs' | ForEach-Object { Get-Content -Raw $_.FullName }) -join "`n"
Reject $allWindowSources 'class\s+LocalCourtWindow\b' `
    'a second local-court window was introduced instead of reusing CourtWindow'

Write-Host 'Local government UI source guard passed.'
