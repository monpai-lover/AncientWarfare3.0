$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw "$PSScriptRoot/../Code/core/court/RegionalEffectiveSeatRules.cs"
if ($source -notmatch 'SelectEffectiveSeat') { throw 'missing pure selector' }
if ($source -notmatch 'Population') { throw 'population tie-break missing' }
if ($source -notmatch 'EconomicScore') { throw 'economic tie-break missing' }
if ($source -notmatch 'CityId') { throw 'city id tie-break missing' }
if ($source -match 'World\.world') { throw 'rules must be pure' }
Write-Output 'RegionalEffectiveSeatRules source guard passed'
