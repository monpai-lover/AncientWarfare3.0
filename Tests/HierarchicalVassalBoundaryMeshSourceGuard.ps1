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
Require-Text 'snapshot capture' $capture 'FingerprintCell(' `
    'the compact audit fingerprint must include complete cell facts'
Require-Regex 'snapshot capture' $capture `
    'FingerprintCell\([\s\S]{0,1000}?\.Height' `
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

Require-Text 'map snapshot' $snapshot 'TryGetBoundaryCellFacts(' `
    'the display snapshot must expose primitive hierarchy/color lookup'
Require-Text 'map snapshot' $snapshot 'HierarchyColorIdentity' `
    'global canonical color assignment inputs must be retained as primitives'
Require-Text 'map snapshot' $snapshot 'HierarchyColorEdge' `
    'global canonical color assignment must receive adjacency inputs'

$capturedModelRegion = $capture
Forbid-Regex 'captured boundary snapshot' $capturedModelRegion `
    'public\s+(?:World|Kingdom|City|TileZone|WorldTile)\b' `
    'captured snapshots must not expose live WorldBox objects'
Forbid-Regex 'captured boundary snapshot' $capturedModelRegion `
    '(?:IReadOnlyList|List|Dictionary|Queue|HashSet)<\s*(?:World|Kingdom|City|TileZone|WorldTile)\b' `
    'captured snapshots must not retain live WorldBox collections'

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
