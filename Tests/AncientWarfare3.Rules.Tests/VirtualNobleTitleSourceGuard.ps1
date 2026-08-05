$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
$path = Join-Path $root 'Code/core/lineage/VirtualNobleTitleService.cs'
if (-not (Test-Path $path)) { throw "missing virtual title service" }
$source = Get-Content -Raw $path
foreach ($needle in @(
    'TryGrant(',
    'BeginTransaction',
    'OnActorDying(',
    'GetPrimaryTitle(',
    'ClearRuntime('
)) {
    if (-not $source.Contains($needle)) { throw "missing service contract: $needle" }
}
foreach ($required in @(
    'CeremonialTitleResolver.ResolveArchive',
    'PRIMARY_CEREMONIAL_TITLE'
)) {
    $all = (Get-Content -Raw (Join-Path $root 'Code/core/lineage/CeremonialTitleResolver.cs')) +
           (Get-Content -Raw (Join-Path $root 'Code/core/lineage/LineageArchiveWriter.cs'))
    if (-not $all.Contains($required)) { throw "missing archive title projection: $required" }
}
Write-Output 'virtual noble title service source guard passed'
