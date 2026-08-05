$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
$catalog = Get-Content -Raw (Join-Path $root 'Code/api/multiplayer/AW3MultiplayerCatalogModels.cs')
$handler = Get-Content -Raw (Join-Path $root 'Code/core/multiplayer/commands/AW3RecordsCommandHandler.cs')
foreach ($pair in @(
    @($catalog, 'GrantVirtualNobleTitle'),
    @($handler, 'GrantVirtualNobleTitle')
)) {
    if (-not $pair[0].Contains($pair[1])) { throw "missing command contract: $($pair[1])" }
}
Write-Output 'virtual noble title command source guard passed'
