$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content (Join-Path $root 'Code/core/lineage/SuccessionDisputeService.cs') -Raw
if ($service -notmatch 'SuccessionCapitalVictoryRules\.ResolveWinner') { throw 'frozen capital resolver not used' }
if ($service -notmatch 'fallbackWinnerKingdomId') { throw 'WarWinner fallback missing' }
if ($service -notmatch 'ResolveFrozenController') { throw 'authoritative controller lookup missing' }
Write-Output 'Succession capital settlement source guard passed.'
