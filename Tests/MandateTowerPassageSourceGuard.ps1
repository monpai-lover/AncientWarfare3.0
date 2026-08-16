$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$service = Get-Content -Raw -Encoding UTF8 (Join-Path $repoRoot `
    'Code/core/lineage/CultiwayStyleCityWallService.cs')
$geometry = Get-Content -Raw -Encoding UTF8 (Join-Path $repoRoot `
    'Code/core/lineage/CultiwayStyleFrontierWallGeometryRules.cs')

foreach ($token in @('pReservedPassages', 'pCarveRoadPassages',
        'new CultiwayFrontierWallGeometryInput(',
        'BuildFrontier(')) {
    if (-not $service.Contains($token)) {
        throw "Frontier wall service is missing $token"
    }
}
foreach ($token in @('ReservedPassages', 'CarveRoadPassages',
        'walls.ExceptWith(pInput.ReservedPassages)')) {
    if (-not $geometry.Contains($token)) {
        throw "Frontier wall geometry is missing $token"
    }
}

Write-Output 'Mandate tower passage source guard passed.'
