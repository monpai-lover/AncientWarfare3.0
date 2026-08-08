$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$source = Get-Content -Raw (Join-Path $root 'Code\core\performance\AWIncrementalSimObjectZoneUnits.cs')
if (-not $source.Contains('Environment.UserName')) { throw 'incremental island validation must have developer boundary' }
if (-not $source.Contains('"Inmny"')) { throw 'incremental island validation developer identity is missing' }
if ($source.Contains('if (!Bench.bench_enabled)')) { throw 'benchmark flag must not gate island invariant validation' }
Write-Output 'IncrementalIslandValidationBoundarySourceGuard passed.'
