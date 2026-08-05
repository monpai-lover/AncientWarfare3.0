$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$files = @(
  (Join-Path $root 'Code\core\atlas\KingdomAtlasHistoryService.cs'),
  (Join-Path $root 'Code\core\atlas\KingdomAtlasRasterizer.cs'),
  (Join-Path $root 'Code\core\atlas\KingdomAtlasArtifactWriter.cs')
)
$forbidden = @('World\.world', 'ArmyRts', 'MapModeService', 'current_tile', 'getColor\(')
foreach ($file in $files) {
  $text = Get-Content -Raw $file
  foreach ($pattern in $forbidden) {
    if ($text -match $pattern) { throw "Kingdom atlas generator references live runtime state: $file ($pattern)" }
  }
}
Write-Output 'Kingdom atlas source guard passed.'
