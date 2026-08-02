$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    $path = Join-Path $repo $relativePath
    if (-not [IO.File]::Exists($path)) {
        $failures.Add("missing source file: $relativePath")
        return ''
    }
    return [IO.File]::ReadAllText($path)
}

function Require-Text([string]$name, [string]$source,
    [string]$needle, [string]$message) {
    if (-not $source.Contains($needle)) {
        $failures.Add("${name}: $message")
    }
}

function Require-Regex([string]$name, [string]$source,
    [string]$pattern, [string]$message) {
    if (-not [regex]::IsMatch($source, $pattern)) {
        $failures.Add("${name}: $message")
    }
}

function Forbid-Regex([string]$name, [string]$source,
    [string]$pattern, [string]$message) {
    if ([regex]::IsMatch($source, $pattern)) {
        $failures.Add("${name}: $message")
    }
}

function Forbid-Text([string]$name, [string]$source,
    [string]$needle, [string]$message) {
    if ($source.Contains($needle)) {
        $failures.Add("${name}: $message")
    }
}

$tracker = Read-Source `
    'Code/core/policy/HierarchicalVassalBoundaryDirtyTracker.cs'
$capture = Read-Source `
    'Code/core/policy/HierarchicalVassalBoundarySnapshotCapture.cs'
$capturedModel = Read-Source `
    'Code/core/policy/HierarchicalVassalBoundaryChunkSnapshot.cs'
$snapshot = Read-Source `
    'Code/core/policy/HierarchicalVassalMapModeSnapshot.cs'
$service = Read-Source `
    'Code/core/policy/HierarchicalVassalMapModeService.cs'
$chunkRules = Read-Source `
    'Code/core/policy/HierarchicalVassalBoundaryChunkRules.cs'
$worker = Read-Source `
    'Code/core/policy/HierarchicalVassalBoundaryTopologyWorker.cs'
$projectVersion = Read-Source `
    'Tools/HierarchicalVassalBoundaryShader/ProjectSettings/ProjectVersion.txt'
$fillShader = Read-Source `
    'Tools/HierarchicalVassalBoundaryShader/Assets/Shaders/AW3HierarchicalVassalFill.shader'
$boundaryShader = Read-Source `
    'Tools/HierarchicalVassalBoundaryShader/Assets/Shaders/AW3HierarchicalVassalBoundary.shader'
$bundleBuilder = Read-Source `
    'Tools/HierarchicalVassalBoundaryShader/Assets/Editor/AW3BoundaryBundleBuilder.cs'
$materialLibrary = Read-Source `
    'Code/core/policy/HierarchicalVassalBoundaryMaterialLibrary.cs'
$dirtyPatch = Read-Source `
    'Code/patch/AW_HierarchicalVassalBoundaryDirtyPatch.cs'
$deferredPatch = Read-Source `
    'Code/patch/AW_DeferredRuntimeWorkPatch.cs'
$labelPatch = Read-Source `
    'Code/patch/AW_HierarchicalVassalMapLabelPatch.cs'
$minimapPatch = Read-Source `
    'Code/patch/AW_HierarchicalVassalMapMinimapPatch.cs'
$metaLibrary = Read-Source `
    'Code/core/policy/AWMapModeMetaLibrary.cs'
$vassalService = Read-Source `
    'Code/core/lineage/VassalService.cs'
$dirtyTracker = Read-Source `
    'Code/core/policy/HierarchicalVassalBoundaryDirtyTracker.cs'

Require-Text 'Unity project version' $projectVersion 'm_EditorVersion: 2022.3.60f1' `
    'must match the installed WorldBox Unity player editor version'
Require-Text 'fill shader' $fillShader 'Shader "AW3/HierarchicalVassal/Fill"' `
    'must expose the bundled fill shader name'
Require-Text 'fill shader' $fillShader '_OverlayAlpha' `
    'must expose overlay alpha control'
Require-Text 'fill shader' $fillShader '_EdgeSoftness' `
    'must expose edge feather softness'
Require-Text 'fill shader' $fillShader 'fwidth' `
    'must derive edge feather width from screen derivatives'
Require-Text 'fill shader' $fillShader 'smoothstep' `
    'must antialias fill edges with smoothstep'
Require-Text 'fill shader' $fillShader '_HeightTex_TexelSize' `
    'must use texture texel size for center differences'
Require-Text 'fill shader' $fillShader '_HeightUvScaleOffset' `
    'must expose height UV scale/offset'
Require-Text 'fill shader' $fillShader '_ReliefStrength' `
    'must expose height relief strength'
Require-Text 'fill shader' $fillShader '_MapLightDirection' `
    'must expose map light direction'
Require-Text 'fill shader' $fillShader 'Blend SrcAlpha OneMinusSrcAlpha' `
    'must use transparent alpha blending'
Require-Text 'fill shader' $fillShader 'tex2D(_HeightTex' `
    'must sample the single-channel height texture'
Require-Text 'fill shader' $fillShader 'heightPlus' `
    'must calculate a centered height difference'
Forbid-Regex 'fill shader' $fillShader `
    '(?m)vertex\.(?:vertex|position|xyz)\s*[+\-]=' `
    'height relief must not displace vertices'

Require-Text 'boundary shader' $boundaryShader `
    'Shader "AW3/HierarchicalVassal/Boundary"' `
    'must expose the bundled boundary shader name'
Require-Text 'boundary shader' $boundaryShader '_LeftColor' `
    'must expose left-side color'
Require-Text 'boundary shader' $boundaryShader '_RightColor' `
    'must expose right-side color'
Require-Text 'boundary shader' $boundaryShader '_CameraWorldPerPixel' `
    'must use camera world-pixel scale for edge AA'
Require-Text 'boundary shader' $boundaryShader '_DarkOutline' `
    'must expose center dark-line strength'
Require-Text 'boundary shader' $boundaryShader '_EdgeSoftness' `
    'must expose edge softness'
Require-Text 'boundary shader' $boundaryShader '_HeightTex_TexelSize' `
    'must use shared height texture texel size'
Require-Text 'boundary shader' $boundaryShader '_HeightUvScaleOffset' `
    'must expose shared height UV scale/offset'
Require-Text 'boundary shader' $boundaryShader '_ReliefStrength' `
    'must expose shared height relief strength'
Require-Text 'boundary shader' $boundaryShader '_MapLightDirection' `
    'must expose shared map light direction'
Require-Text 'boundary shader' $boundaryShader 'tex2D(_HeightTex' `
    'must sample shared height texture'
Require-Text 'boundary shader' $boundaryShader 'heightPlus' `
    'must calculate centered height differences'
Require-Text 'boundary shader' $boundaryShader 'fwidth' `
    'must use derivative antialiasing'
Require-Text 'boundary shader' $boundaryShader 'smoothstep' `
    'must use smoothstep antialiasing'
Require-Text 'boundary shader' $boundaryShader 'uv0.x' `
    'must read signed edge distance from UV0'
Require-Text 'boundary shader' $boundaryShader 'uv1.x' `
    'must read boundary tier from UV1'
Require-Text 'boundary shader' $boundaryShader 'coast' `
    'must handle water-side coastline alpha'
Require-Text 'boundary shader' $boundaryShader 'Queue"="Transparent-100"' `
    'must render before labels and click overlays'
Require-Text 'boundary shader' $boundaryShader 'ZWrite Off' `
    'must not write depth over labels'

Require-Text 'bundle builder' $bundleBuilder 'BuildWindows' `
    'must expose a Windows bundle build entry point'
Require-Text 'bundle builder' $bundleBuilder 'GameResources/assetbundles' `
    'must write into the mod GameResources assetbundles directory'
Require-Text 'bundle builder' $bundleBuilder `
    'aw3_hierarchical_vassal_boundary' `
    'must use the stable boundary bundle name'
Require-Text 'bundle builder' $bundleBuilder 'ChunkBasedCompression' `
    'must use chunk-based compression'
Require-Text 'bundle builder' $bundleBuilder 'StandaloneWindows64' `
    'must target Windows 64-bit'
Require-Text 'bundle builder' $bundleBuilder `
    'AW3/HierarchicalVassal/Fill' `
    'must include the fill shader asset'
Require-Text 'bundle builder' $bundleBuilder `
    'AW3/HierarchicalVassal/Boundary' `
    'must include the boundary shader asset'

Require-Text 'material library' $materialLibrary `
    'ModClass.Instance.GetDeclaration().FolderPath' `
    'must resolve the bundle from the mod declaration folder'
Require-Text 'material library' $materialLibrary `
    'aw3_hierarchical_vassal_boundary' `
    'must load the boundary bundle'
Require-Text 'material library' $materialLibrary `
    'AW3/HierarchicalVassal/Fill' `
    'must load the fill shader by bundled name'
Require-Text 'material library' $materialLibrary `
    'AW3/HierarchicalVassal/Boundary' `
    'must load the boundary shader by bundled name'
Require-Text 'material library' $materialLibrary 'Unload(false)' `
    'must unload bundle assets while retaining loaded shaders'
Forbid-Text 'material library' $materialLibrary 'Unload(true)' `
    'must never unload loaded shader assets'
Require-Text 'material library' $materialLibrary 'Shader.Find("Sprites/Default")' `
    'must provide a Sprites/Default fallback shader'
Require-Text 'material library' $materialLibrary '_ReliefStrength' `
    'must disable height relief on fallback'
Require-Text 'material library' $materialLibrary 'LogWarning' `
    'must report fallback failure'
Require-Text 'material library' $materialLibrary '_warningWritten' `
    'must emit only one fallback warning'
Forbid-Regex 'material library' $materialLibrary `
    '\b(?:Instantiate|Clone)\s*\(' `
    'must reuse shared materials instead of cloning per chunk'

Require-Text 'chunk rules' $chunkRules 'CaptureBudgetPerFrame = 2' `
    'capture budget must remain exactly two chunks per frame'
Require-Text 'dirty tracker' $tracker `
    'Dictionary<BoundaryChunkKey, long>' `
    'must keep the latest monotonic revision for every chunk'
Require-Text 'dirty tracker' $tracker 'Queue<BoundaryChunkKey>' `
    'must drain dirty chunks in FIFO order'
Require-Text 'dirty tracker' $tracker 'HashSet<BoundaryChunkKey>' `
    'must keep queue keys unique while revisions advance'
Require-Text 'dirty tracker' $tracker 'DirtyNeighborhood(' `
    'tile, zone, and kingdom marks must expand through the 3x3 neighborhood'
Require-Regex 'dirty tracker' $tracker `
    'public\s+void\s+MarkTile\s*\(' `
    'must expose tile dirty routing'
Require-Regex 'dirty tracker' $tracker `
    'public\s+void\s+MarkZone\s*\(' `
    'must expose zone dirty routing'
Require-Regex 'dirty tracker' $tracker `
    'public\s+void\s+MarkKingdom\s*\(' `
    'must expose kingdom dirty routing'
Require-Text 'dirty tracker' $tracker '_queued.Add(' `
    'duplicate marks must update revisions without adding duplicate queue nodes'

Require-Text 'snapshot capture' $capture `
    'HierarchicalVassalBoundaryChunkRules.CaptureBudgetPerFrame' `
    'main-thread capture must use the fixed two-chunk budget'
Require-Text 'snapshot capture' $capture 'new BoundaryCellFacts[' `
    'capture must allocate copied primitive cell facts'
Require-Text 'snapshot capture' $capture 'new BoundaryCellRaster(' `
    'captured facts must be isolated behind the immutable raster copy'
Require-Regex 'snapshot capture' $capture `
    '\(byte\)Mathf\.Clamp\(\s*[^,]*\.Height\s*,\s*0\s*,\s*255\s*\)' `
    'WorldTile.Height must be clamped into its native byte range'
Require-Text 'snapshot capture' $capture 'InvalidCell(' `
    'world-edge halo cells must be represented explicitly as invalid facts'
Require-Text 'snapshot capture' $capture `
    'HierarchicalVassalBoundaryChunkRules.Fingerprint(' `
    'capture and audit must use the tested pure fingerprint'
Require-Text 'chunk rules' $chunkRules 'cell.Height' `
    'the terrain fingerprint must include native height'
Require-Text 'snapshot capture' $capture 'AuditOneChunkPerSimulationCycle' `
    'round-robin auditing must expose a one-chunk simulation-cycle operation'
Require-Text 'snapshot capture' $capture '_auditCursor' `
    'auditing must advance through chunks round-robin'
Require-Text 'snapshot capture' $capture '_auditFingerprints' `
    'auditing must compare against the prior compact chunk fingerprint'
Require-Text 'snapshot capture' $capture 'AssertMainThread(' `
    'all live WorldBox capture must be guarded to the main thread'
Require-Text 'snapshot capture' $capture 'ResetWorld(' `
    'capture state must be invalidated across world resets'
Require-Text 'snapshot capture' $capture 'TryValidateWorld(' `
    'capture must reject invalid or reversed world bounds'
Require-Text 'snapshot capture' $capture 'CaptureRaster(' `
    'capture and audit must share one complete-halo raster capture path'
if (([regex]::Matches($capture,
        'CaptureRaster\(')).Count -lt 3) {
    $failures.Add('snapshot capture: capture and audit must both call the shared CaptureRaster method')
}
Forbid-Regex 'snapshot capture' $capture '\bFingerprintInterior\s*\(' `
    'audit cannot fingerprint a smaller range than ordinary capture'
Require-Text 'snapshot capture' $capture `
    'HierarchicalVassalBoundaryChunkRules.HasAuditChange(' `
    'unchanged and first-observation audit behavior must use tested pure rules'

Require-Text 'map snapshot' $snapshot 'TryGetBoundaryCellFacts(' `
    'the display snapshot must expose primitive hierarchy/color lookup'
Require-Text 'map snapshot' $snapshot 'HierarchyColorIdentity' `
    'global canonical color assignment inputs must be retained as primitives'
Require-Text 'map snapshot' $snapshot 'HierarchyColorEdge' `
    'global canonical color assignment must receive adjacency inputs'
Require-Text 'map snapshot' $snapshot '_readOnlyBoundaryFactsByZone' `
    'boundary fact lookup must cache its readonly dictionary wrapper'
Require-Text 'map snapshot' $snapshot '_readOnlyBoundaryColorIdentities' `
    'canonical identity input must cache its readonly wrapper'
Require-Text 'map snapshot' $snapshot '_readOnlyBoundaryColorEdges' `
    'canonical edge input must cache its readonly wrapper'
Forbid-Regex 'map snapshot getters' $snapshot `
    '=>\s*(?:new\s+ReadOnlyDictionary|[^;\r\n]*\.AsReadOnly\s*\()' `
    'snapshot getters cannot allocate readonly wrappers'

Require-Text 'map snapshot service' $service 'MapBoundaryZone(' `
    'the production snapshot builder must populate primitive boundary facts'
Require-Text 'map snapshot service' $service 'SetBoundaryColorInputs(' `
    'the production snapshot builder must publish canonical color inputs'
Require-Text 'map snapshot service' $service 'HierarchyColorIdentity' `
    'the production builder must create hierarchy color identities'
Require-Text 'map snapshot service' $service 'HierarchyColorEdge' `
    'the production builder must create real adjacency color edges'
Require-Regex 'map snapshot service' $service `
    '\.neighbours\b' `
    'canonical colors must use real zone adjacency'

$modelStart = $capturedModel.IndexOf(
    'public sealed class HierarchicalVassalBoundaryChunkSnapshot')
$modelEnd = $capturedModel.LastIndexOf('}')
if ($modelStart -lt 0 -or $modelEnd -le $modelStart) {
    $failures.Add('captured boundary snapshot: cannot isolate data model region')
    $capturedModelRegion = ''
} else {
    $capturedModelRegion = $capturedModel.Substring(
        $modelStart, $modelEnd - $modelStart)
}
Forbid-Regex 'captured boundary snapshot' $capturedModelRegion `
    '(?m)\b(?:public|internal|protected|private)\s+(?:(?:readonly|static)\s+)*(?:(?:IReadOnlyList|IReadOnlyCollection|List|Dictionary|Queue|HashSet)<\s*)?(?:World|Kingdom|City|TileZone|WorldTile|UnityEngine(?:\.[A-Za-z_][A-Za-z0-9_]*)?|object)\b(?:\s*\[\s*\])?' `
    'captured snapshot data fields and properties must use primitive/pure types only'

Require-Text 'topology worker' $worker 'new Thread(' `
    'must own one dedicated worker thread'
if (([regex]::Matches($worker, 'new\s+Thread\s*\(')).Count -ne 1) {
    $failures.Add('topology worker: must create exactly one dedicated worker thread')
}
Require-Text 'topology worker' $worker 'AutoResetEvent' `
    'must signal bounded pending work without polling'
Require-Text 'topology worker' $worker 'Dictionary<WorkKey,' `
    'must coalesce pending requests by generation and chunk key'
Require-Text 'topology worker' $worker 'Dictionary<WorkKey, long>' `
    'must retain the latest accepted revision across pending and completion states'
Require-Text 'topology worker' $worker '_pending.Count >= _worldChunkCount' `
    'pending queue must reject distinct keys at saturation'
Require-Text 'topology worker' $worker '_completions.Count >= _worldChunkCount' `
    'completion queue must remain bounded'
Require-Text 'topology worker' $worker '_needsRescan = true' `
    'saturation must preserve one rescan marker'
Require-Text 'topology worker' $worker 'BuildFillAuthoritative(' `
    'worker must use the authoritative color assignment'
Require-Text 'topology worker' $worker 'BuildRibbons(' `
    'worker must build consolidated ribbons'
Forbid-Regex 'topology worker' $worker '\bTask\s*\.\s*Run\s*\(' `
    'worker cannot create unbounded Task.Run work'

# Task 11 runtime ownership and ordering guard. Keep these checks source-level
# so CI can reject a patch that compiles but silently drops a hook or stage.
Require-Text 'runtime dirty patch' $dirtyPatch 'TargetMethods()' `
    'dirty hooks must select overloads through reflection'
Require-Text 'runtime dirty patch' $dirtyPatch 'TileZone' `
    'must route TileZone ownership changes'
Require-Text 'runtime dirty patch' $dirtyPatch 'City.addZone' `
    'must route City.addZone'
Require-Text 'runtime dirty patch' $dirtyPatch 'joinAnotherKingdom' `
    'must route city kingdom transfers'
Require-Text 'runtime dirty patch' $dirtyPatch 'WorldTile.Height' `
    'must route terrain height setter'
Require-Text 'runtime dirty patch' $dirtyPatch 'setTileType' `
    'must route every setTileType overload'
Require-Text 'runtime dirty patch' $dirtyPatch 'setTileTypes' `
    'must route every setTileTypes overload'
Require-Text 'runtime dirty patch' $dirtyPatch 'OldCity' `
    'must capture old city before ownership mutation'
Require-Text 'runtime dirty patch' $dirtyPatch 'OldKingdom' `
    'must capture old kingdom before ownership mutation'
Require-Text 'runtime dirty patch' $dirtyPatch 'OldHeight' `
    'must capture old terrain height before mutation'
Require-Text 'runtime dirty patch' $dirtyPatch 'MarkTile' `
    'must mark changed tile chunks'
Require-Text 'runtime dirty patch' $dirtyPatch 'MarkZone' `
    'must mark changed zone chunks'
Require-Text 'runtime dirty patch' $dirtyPatch 'MarkKingdom' `
    'must mark changed kingdom chunks'
Require-Text 'runtime dirty patch' $dirtyPatch 'AuditOneChunkPerSimulationCycle' `
    'missing reflected lifecycle hooks must use bounded audit fallback'
Require-Text 'runtime dirty patch' $dirtyPatch 'generation' `
    'terrain routing must be gated by renderer world generation'
Require-Regex 'runtime dirty patch' $dirtyPatch `
    'GetMethods\(' `
    'overload discovery must inspect all declared signatures'
Require-Text 'runtime dirty tracker' $dirtyTracker 'DirtyNeighborhood' `
    'dirty routing must retain 3x3 chunk expansion'

Require-Text 'runtime revision stage' $dirtyPatch 'RefreshIfWorldChanged' `
    'world revision events must run before capture'
Require-Text 'runtime deferred ordering' $deferredPatch 'ProcessCapture' `
    'deferred runtime must expose bounded snapshot capture stage'
Require-Text 'runtime deferred ordering' $deferredPatch 'DrainWorker' `
    'deferred runtime must drain topology worker completions before mesh upload'
Require-Text 'runtime deferred ordering' $deferredPatch 'DrainMesh' `
    'deferred runtime must upload a bounded mesh batch before labels'
Require-Regex 'runtime deferred ordering' $deferredPatch `
    'ProcessCapture[\s\S]*DrainWorker[\s\S]*DrainMesh[\s\S]*ProcessLabels' `
    'runtime order must be capture -> worker drain -> mesh drain -> labels'
Require-Text 'runtime labels' $labelPatch 'ProcessLabels' `
    'labels must run in the final ordered stage'
Require-Text 'runtime reset' $labelPatch 'CancelGeneration' `
    'clearWorld must cancel boundary generation before destroying roots'
Require-Text 'runtime reset' $dirtyPatch 'ResetWorld' `
    'world reset must publish a new boundary generation'
Require-Text 'runtime minimap' $minimapPatch 'SetMinimapHidden' `
    'minimap must hide only boundary roots through the facade'
Require-Text 'runtime minimap' $minimapPatch 'fill' `
    'minimap behavior must preserve fill roots'

Require-Text 'legacy suppression' $metaLibrary 'MeshAuthorityActive' `
    'hierarchical asset must query mesh authority before drawZoneMeta'
Require-Text 'legacy suppression' $metaLibrary 'drawZoneMeta' `
    'legacy path remains auditable behind an explicit fallback gate'
Require-Text 'legacy suppression' $metaLibrary 'return;' `
    'mesh authority must return before invoking legacy drawing'
Require-Text 'hierarchy mutation bridge' $vassalService 'BoundaryHierarchyChanged' `
    'vassal relation mutations must notify boundary dirty routing'

$pureFiles = @(
    'Code/core/policy/HierarchicalVassalBoundaryModels.cs',
    'Code/core/policy/HierarchicalVassalBoundaryChunkRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryTopologyRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryRiverRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryCurveRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryPolygonRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryHeightRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryMeshDraftRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryColorRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryChunkSnapshot.cs',
    'Code/core/policy/HierarchicalVassalBoundaryTopologyWorker.cs'
)
foreach ($relativePath in $pureFiles) {
    $source = Read-Source $relativePath
    Forbid-Regex $relativePath $source '\bUnityEngine\b' `
        'pure boundary code cannot reference UnityEngine'
    Forbid-Regex $relativePath $source `
        '(?:new|typeof|\bas\b|\bis\b|<|\()\s*(?:World|Kingdom|City|TileZone|WorldTile)\b|\b(?:World|Kingdom|City|TileZone|WorldTile)\s+[p_][A-Za-z]' `
        'pure boundary code cannot reference live WorldBox types'
}

if ($failures.Count -gt 0) {
    throw "Hierarchical boundary mesh source guard failures:`n - " +
        ($failures -join "`n - ")
}

Write-Output 'Hierarchical vassal boundary mesh source guard passed.'
