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

$tracker = Read-Source `
    'Code/core/policy/HierarchicalVassalBoundaryDirtyTracker.cs'
$capture = Read-Source `
    'Code/core/policy/HierarchicalVassalBoundarySnapshotCapture.cs'
$snapshot = Read-Source `
    'Code/core/policy/HierarchicalVassalMapModeSnapshot.cs'
$service = Read-Source `
    'Code/core/policy/HierarchicalVassalMapModeService.cs'
$chunkRules = Read-Source `
    'Code/core/policy/HierarchicalVassalBoundaryChunkRules.cs'

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

$modelStart = $capture.IndexOf(
    'public sealed class HierarchicalVassalBoundaryChunkSnapshot')
$modelEnd = $capture.IndexOf(
    'internal sealed class HierarchicalVassalBoundarySnapshotCapture',
    $modelStart)
if ($modelStart -lt 0 -or $modelEnd -le $modelStart) {
    $failures.Add('captured boundary snapshot: cannot isolate data model region')
    $capturedModelRegion = ''
} else {
    $capturedModelRegion = $capture.Substring(
        $modelStart, $modelEnd - $modelStart)
}
Forbid-Regex 'captured boundary snapshot' $capturedModelRegion `
    '(?m)\b(?:public|internal|protected|private)\s+(?:(?:readonly|static)\s+)*(?:(?:IReadOnlyList|IReadOnlyCollection|List|Dictionary|Queue|HashSet)<\s*)?(?:World|Kingdom|City|TileZone|WorldTile|UnityEngine(?:\.[A-Za-z_][A-Za-z0-9_]*)?|object)\b(?:\s*\[\s*\])?' `
    'captured snapshot data fields and properties must use primitive/pure types only'

$pureFiles = @(
    'Code/core/policy/HierarchicalVassalBoundaryModels.cs',
    'Code/core/policy/HierarchicalVassalBoundaryChunkRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryTopologyRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryRiverRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryCurveRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryPolygonRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryHeightRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryMeshDraftRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryColorRules.cs'
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
