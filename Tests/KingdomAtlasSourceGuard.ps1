$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$files = @(
  (Join-Path $root 'Code\core\atlas\KingdomAtlasHistoryService.cs'),
  (Join-Path $root 'Code\core\atlas\KingdomAtlasRasterizer.cs'),
  (Join-Path $root 'Code\core\atlas\KingdomAtlasArtifactWriter.cs'),
  (Join-Path $root 'Code\patch\AW_ChroniclePatch.cs')
)
$forbidden = @('World\.world', 'ArmyRts', 'MapModeService', 'current_tile', 'getColor\(')
foreach ($file in $files) {
  $text = Get-Content -Raw $file
  if ($file -like '*AW_ChroniclePatch.cs') { continue }
  foreach ($pattern in $forbidden) {
    if ($text -match $pattern) { throw "Kingdom atlas generator references live runtime state: $file ($pattern)" }
  }
}
$chroniclePatch = Get-Content -Raw (Join-Path $root 'Code\patch\AW_ChroniclePatch.cs')
if ($chroniclePatch -notmatch 'DestroyCity_Prefix') {
  throw 'Kingdom atlas must capture city geometry before destruction.'
}
if ($chroniclePatch -notmatch 'CityAddZone_Postfix') {
  throw 'Kingdom atlas must capture geometry after city zones are added.'
}
if ($chroniclePatch -notmatch 'CaptureCityGeometry\(') {
  throw 'Chronicle patch must call the atlas geometry archive hook.'
}
$window = Get-Content -Raw (Join-Path $root 'Code\ui\windows\KingdomAtlasWindow.cs')
if ($window -notmatch 'StartCoroutine\(' -or
    $window -notmatch 'GenerateRoutine') {
  throw 'Kingdom atlas generation must yield between node renders.'
}
if ($window -notmatch 'RenderBitmapLabels' -or
    $window -notmatch 'ExternalLabelRenderer') {
  throw 'Exported atlas labels must use the map-mode font renderer.'
}
Write-Output 'Kingdom atlas source guard passed.'
