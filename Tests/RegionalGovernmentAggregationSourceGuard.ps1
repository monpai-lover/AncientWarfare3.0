$ErrorActionPreference = 'Stop'
$read = Get-Content -Raw "$PSScriptRoot/../Code/core/court/RegionalGovernmentReadModel.cs"
$service = Get-Content -Raw "$PSScriptRoot/../Code/core/court/DeJureRegionReadModelService.cs"
if ($read -notmatch 'LegalSeatCityId') { throw 'missing legal seat field' }
if ($read -notmatch 'EffectiveSeatCityId') { throw 'missing effective seat field' }
if ($read -notmatch 'ControllerMemberCounts') { throw 'missing controller counts' }
if ($service -notmatch 'GroupBy\([^\r\n]*kingdom') { throw 'missing per-controller grouping' }
if ($service -notmatch 'SelectEffectiveSeat') { throw 'missing effective seat selection' }
Write-Output 'RegionalGovernmentAggregation source guard passed'
