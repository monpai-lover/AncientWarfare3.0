$ErrorActionPreference = 'Stop'
$read = Get-Content -Raw "$PSScriptRoot/../Code/core/court/CourtReadModelService.cs"
$node = Get-Content -Raw "$PSScriptRoot/../Code/core/court/CourtPyramidRules.cs"
$view = Get-Content -Raw "$PSScriptRoot/../Code/ui/items/CourtActorNodeView.cs"
$cache = Get-Content -Raw "$PSScriptRoot/../Code/core/court/RegionalGovernmentAggregationService.cs"
if ($read -notmatch 'IsFixedRole') { throw 'fixed nodes are not marked' }
if ($node -notmatch 'IsFixedRole') { throw 'node model lacks fixed marker' }
if ($view -notmatch '!pNode\.IsFixedRole') { throw 'fixed vacancy can still be appointed' }
if ($cache -notmatch 'Invalidate') { throw 'cache invalidation missing' }
Write-Output 'FixedChiefVacancyCompatibility source guard passed'
