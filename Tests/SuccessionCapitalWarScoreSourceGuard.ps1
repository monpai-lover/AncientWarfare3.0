$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$capital = Get-Content (Join-Path $root 'Code/core/lineage/SuccessionCapitalVictoryRules.cs') -Raw
$occupation = Get-Content (Join-Path $root 'Code/core/lineage/WarScoreTotalOccupationRules.cs') -Raw
if ($capital -notmatch 'ResolveWinner\s*\(' -or
    $capital -notmatch 'pOriginalCapitalCityId' -or
    $capital -notmatch 'pRivalCapitalCityId') { throw 'succession capital rule contract missing' }
if ($occupation -notmatch 'TryResolveWinner\s*\(' -or
    $occupation -notmatch 'pAttackerControlsAllDefenderCities' -or
    $occupation -notmatch 'WarScoreSide\.Attackers') { throw 'total occupation rule contract missing' }
Write-Output 'Succession capital and total occupation source guard passed.'
