$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content (Join-Path $root 'Code/core/lineage/WarScoreService.cs') -Raw
$bridge = Get-Content (Join-Path $root 'Code/core/lineage/WarScoreRuntimeBridge.cs') -Raw
if ($service -notmatch 'TrySettleTotalOccupation\s*\(' -or
    $service -notmatch 'total_occupation:') { throw 'total occupation settlement missing' }
if ($service -notmatch 'MaximumScore' -or
    $service -notmatch 'HasEvent\(') { throw 'canonical score/idempotency missing' }
if ($bridge -notmatch 'TrySettleTotalOccupation\(pWar, runtime\)') { throw 'occupation callback not wired' }
Write-Output 'War score total occupation source guard passed.'
