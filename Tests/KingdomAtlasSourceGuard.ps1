$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$files = @(
  (Join-Path $root 'Code\core\atlas\KingdomAtlasHistoryService.cs'),
  (Join-Path $root 'Code\core\atlas\KingdomAtlasRasterizer.cs'),
  (Join-Path $root 'Code\core\atlas\KingdomAtlasLiveTerrainService.cs'),
  (Join-Path $root 'Code\core\atlas\KingdomAtlasLiveTerrainRules.cs'),
  (Join-Path $root 'Code\core\atlas\KingdomAtlasArtifactWriter.cs'),
  (Join-Path $root 'Code\core\presentation\ArmyRtsPlanWorldTerrainCapture.cs'),
  (Join-Path $root 'Code\core\atlas\KingdomAtlasGenerationSession.cs'),
  (Join-Path $root 'Code\ui\windows\KingdomAtlasWindow.cs'),
  (Join-Path $root 'Code\patch\AW_ChroniclePatch.cs')
)
foreach ($file in $files) {
  $text = Get-Content -Raw $file
  if ($file -like '*AW_ChroniclePatch.cs') { continue }
}
$history = Get-Content -Raw (Join-Path $root 'Code\core\atlas\KingdomAtlasHistoryService.cs')
foreach ($needle in @('ReplayCityOwnersAt', 'city_found',
        'city_transfer', 'ReadCityTerritorialRows',
        'ReadKingdomTerritorialRows', 'MatchCityTransferRows',
        'consumedKingdomEventIds')) {
  if (-not $history.Contains($needle)) {
    throw "Historical atlas reconstruction is missing: $needle"
  }
}
$forbiddenHistoryPaths = @(
  'KingdomAtlasZoneArchiveService',
  'TryReadZoneArchive',
  'KingdomAtlasZoneSnapshot'
)
foreach ($needle in $forbiddenHistoryPaths) {
  if ($history.Contains($needle)) {
    throw "Historical atlas reconstruction still reads runtime tile geometry: $needle"
  }
}
foreach ($pattern in @(
        'ResolveNodeYear\s*\(\s*row\.OldKingdomId\s*,\s*descriptor\.WorldTime\s*\)',
        'ResolveNodeYear\s*\(\s*row\.NewKingdomId\s*,\s*descriptor\.WorldTime\s*\)',
        'oldChronicleYear\.Year',
        'newChronicleYear\.Year')) {
  if ($history -notmatch $pattern) {
    throw "Kingdom atlas chronicles do not use participant-specific years: $pattern"
  }
}
if (-not $history.Contains('BuildKingdomSnapshots(row, relationSnapshot)')) {
  throw 'Kingdom atlas kingdom snapshots must use only the node relation snapshot.'
}
$liveTerrain = Get-Content -Raw (Join-Path $root 'Code\core\atlas\KingdomAtlasLiveTerrainService.cs')
if (-not $liveTerrain.Contains('ProjectHistoricalOwners')) {
  throw 'Live terrain must project historical city ownership before rendering.'
}
if (-not $liveTerrain.Contains('pTerrain.Water')) {
  throw 'Live terrain must exclude water before projecting historical city ownership.'
}
if ($liveTerrain.Contains('pTerrain.OwnerIds')) {
  throw 'Live terrain must not use current kingdom owners for a historical node.'
}
$session = Get-Content -Raw (Join-Path $root 'Code\core\atlas\KingdomAtlasGenerationSession.cs')
if (-not $session.Contains('KingdomAtlasLiveTerrainService.Render') -or
    -not $session.Contains('ArmyRtsPlanWorldTerrainSnapshot')) {
  throw 'Kingdom atlas generation must render the frozen terrain canvas with historical owners.'
}
if (-not $session.Contains('ExternalLabelRenderer')) {
  throw 'Kingdom atlas generation bypasses the bitmap label renderer.'
}
if (-not $session.Contains('_result.OutputDirectory = _outputDirectory') -or
    -not $session.Contains('_result.GifPath = gifPath')) {
  throw 'Kingdom atlas generation does not return its actual export paths.'
}
$window = Get-Content -Raw (Join-Path $root 'Code\ui\windows\KingdomAtlasWindow.cs')
if (-not $window.Contains('KingdomAtlasLiveTerrainService.Render') -or
    -not $window.Contains('ArmyRtsPlanWorldTerrainSnapshot')) {
  throw 'Kingdom atlas preview must render historical owners over a frozen terrain canvas.'
}
if (-not $window.Contains('EnsureTerrain')) {
  throw 'Kingdom atlas preview must lazily capture terrain only when preview or generation is requested.'
}
if (-not $window.Contains('TryLoadCachedPreviewPng')) {
  throw 'Kingdom atlas preview does not attempt to load its dedicated cache.'
}
if (-not $window.Contains('CachePreviewPng')) {
  throw 'Kingdom atlas preview does not persist a rendered raster.'
}
if (-not $window.Contains('KingdomAtlasRaster display = RenderBitmapLabels(node, raster)') -or
    -not $window.Contains('SetRasterTexture(display)')) {
  throw 'Kingdom atlas previews must cache the raster with country labels baked in.'
}
if (-not $window.Contains('camera.cullingMask = 1 << AtlasRenderLayer') -or
    -not $window.Contains('canvasObject.layer = AtlasRenderLayer') -or
    -not $window.Contains('labelObject.layer = AtlasRenderLayer')) {
  throw 'Kingdom atlas off-screen camera must be isolated from the live world.'
}
if (-not $window.Contains('"KingdomAtlasExportBackground"') -or
    -not $window.Contains('backgroundObject.transform.SetParent(canvasObject.transform') -or
    $window.Contains('RawImage image = canvasObject.AddComponent<RawImage>()')) {
  throw 'Kingdom atlas background must be a full-size child of the render canvas.'
}
if (-not $window.Contains('_forcePreviewRender') -or
    -not $window.Contains('_forcePreviewRender = true')) {
  throw 'Changing the atlas font must bypass the cached raster once.'
}
if (-not $window.Contains('AWFontDropdown.Create')) {
  throw 'Kingdom atlas font control is not the shared dropdown.'
}
if ($window.Contains('CycleMapFont')) {
  throw 'Kingdom atlas still exposes a cycling font button.'
}
if (-not $window.Contains('aw_kingdom_atlas_export_path') -or
    -not $window.Contains('result.OutputDirectory')) {
  throw 'Kingdom atlas UI does not display the actual export path.'
}
foreach ($needle in @('aw_kingdom_atlas_vassal_gained',
        'aw_kingdom_atlas_vassal_lost')) {
  if (-not $window.Contains($needle)) {
    throw "Kingdom atlas relation node localization is missing: $needle"
  }
}
$artifact = Get-Content -Raw (Join-Path $root 'Code\core\atlas\KingdomAtlasArtifactWriter.cs')
if (-not $artifact.Contains('historical-city-chronicle-v14-territory-scaled-labels')) {
  throw 'Atlas artifact version was not invalidated for territory-scaled labels.'
}
if (-not $artifact.Contains('TryLoadCachedPng')) {
  throw 'Atlas artifact writer does not expose the generated PNG cache to preview.'
}
$chroniclePatch = Get-Content -Raw (Join-Path $root 'Code\patch\AW_ChroniclePatch.cs')
$destroyPrefixMatch = [regex]::Match(
  $chroniclePatch,
  '(?s)DestroyCity_Prefix\s*\([^)]*\)\s*\{(?<body>.*?)\n\s*\}')
if ($destroyPrefixMatch.Success -and
    $destroyPrefixMatch.Groups['body'].Value -match
      'KingdomAtlas|Artifact|Geometry|World\.world|foreach|Save') {
  throw 'City destruction must not scan or persist atlas geometry at runtime.'
}
if ($chroniclePatch -notmatch 'CityAddZone_Postfix') {
  throw 'City zone additions must still invalidate the live map geometry.'
}
$cityAddZoneMatch = [regex]::Match(
  $chroniclePatch,
  '(?s)CityAddZone_Postfix\s*\([^)]*\)\s*\{(?<body>.*?)\n\s*\}')
if (-not $cityAddZoneMatch.Success) {
  throw 'CityAddZone_Postfix body could not be inspected.'
}
$cityAddZoneBody = $cityAddZoneMatch.Groups['body'].Value
if ($cityAddZoneBody.Contains('CaptureCityGeometry') -or
    $cityAddZoneBody.Contains('CaptureCityEvent')) {
  throw 'City.addZone must not synchronously archive full atlas geometry.'
}
if (-not $cityAddZoneBody.Contains('MarkCityGeometryDirty') -and
    -not $cityAddZoneBody.Contains('MarkCityZoneGeometryDirty')) {
  throw 'City.addZone must keep the live map geometry invalidation.'
}
$chronicleEvents = Get-Content -Raw (Join-Path $root `
  'Code\core\lineage\ChronicleEvents.cs')
if ($chronicleEvents.Contains('KingdomAtlasZoneArchiveService')) {
  throw 'City chronicle events must record ownership only, not tile geometry.'
}
$savePatch = Get-Content -Raw (Join-Path $root 'Code\patch\AW_SavePatch.cs')
if ($savePatch.Contains('KingdomAtlasZoneArchiveService')) {
  throw 'Save preparation must not flush an atlas-specific tile archive.'
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
if ($window.Contains('placement.Size * 12f')) {
  throw 'Atlas labels must not treat map-mode world units as a fixed pixel multiplier.'
}
if (-not $window.Contains('CalculateLabelPixelSize') -or
    -not $window.Contains('pRaster.Width')) {
  throw 'Atlas label size must be converted from world units using output resolution.'
}
if (-not $window.Contains('ScaleAtlasCountryLabelForTerritory') -or
    -not $window.Contains('tiles.Count')) {
  throw 'Atlas labels must attenuate fitted size by territory area.'
}
Write-Output 'Kingdom atlas source guard passed.'
