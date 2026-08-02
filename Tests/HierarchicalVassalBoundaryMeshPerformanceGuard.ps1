$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

function Read-Source([string] $relativePath) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "missing source: $relativePath"
    }
    return [System.IO.File]::ReadAllText($path)
}

function Require([bool] $condition, [string] $message) {
    if (-not $condition) { throw "performance guard failed: $message" }
}

function Forbid([string] $text, [string] $pattern, [string] $message) {
    Require (-not [regex]::IsMatch($text, $pattern,
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
        [System.Text.RegularExpressions.RegexOptions]::Multiline)) $message
}

$chunk = Read-Source 'Code/core/policy/HierarchicalVassalBoundaryChunkRules.cs'
$worker = Read-Source 'Code/core/policy/HierarchicalVassalBoundaryTopologyWorker.cs'
$models = Read-Source 'Code/core/policy/HierarchicalVassalBoundaryModels.cs'
$capture = Read-Source 'Code/core/policy/HierarchicalVassalBoundarySnapshotCapture.cs'
$tracker = Read-Source 'Code/core/policy/HierarchicalVassalBoundaryDirtyTracker.cs'
$patch = Read-Source 'Code/patch/AW_HierarchicalVassalBoundaryDirtyPatch.cs'
$layer = Read-Source 'Code/core/policy/HierarchicalVassalBoundaryMeshLayer.cs'

# Hot paths must consume bounded snapshots/chunks, never rescan every kingdom.
foreach ($source in @($capture, $tracker, $patch, $layer, $worker)) {
    Forbid $source 'World\s*\.\s*world\s*\.\s*kingdoms' 'full World.world.kingdoms traversal'
    Forbid $source 'foreach\s*\([^\)]*\bin\s+[^\r\n;]*kingdoms\b' 'kingdom collection traversal'
}

# Tile/edge processing only changes dirty keys and existing pooled render pairs.
$tileMethods = [regex]::Match($patch,
    '(?s)internal\s+static\s+void\s+MarkTile\s*\(.*?\n\s*\}',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase).Value
$zoneMethods = [regex]::Match($patch,
    '(?s)internal\s+static\s+void\s+MarkZone\s*\(.*?\n\s*\}',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase).Value
Forbid ($tileMethods + $zoneMethods) 'new\s+GameObject\s*\(' 'per-tile GameObject allocation'
$uploadText = [regex]::Match($layer,
    '(?s)private\s+static\s+bool\s+Upload\s*\(.*?\n\s*\}',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase).Value
$uploadFillText = [regex]::Match($layer,
    '(?s)private\s+static\s+bool\s+UploadFill\s*\(.*?\n\s*\}',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase).Value
$uploadBoundaryText = [regex]::Match($layer,
    '(?s)private\s+static\s+bool\s+UploadBoundary\s*\(.*?\n\s*\}',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase).Value
Forbid $uploadText 'new\s+(GameObject|Mesh|Texture2D)\s*' 'upload path replacement/allocation'
Forbid $uploadFillText 'new\s+(GameObject|Mesh|Texture2D)\s*' 'per-upload fill resource allocation'
Forbid $uploadBoundaryText 'new\s+(GameObject|Mesh|Texture2D)\s*' 'per-upload boundary resource allocation'

# Meshes, textures, and GameObjects are pooled by stable chunk/layer keys.
Require ([regex]::IsMatch($layer,
    'Pairs\.TryGetValue\s*\(\s*pKey\s*,\s*out\s+RenderPair',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'render-pair dictionary coalescing'
Require ([regex]::IsMatch($layer,
    'Heights\.TryGetValue\s*\(\s*pKey\s*,\s*out\s+HeightResource',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'height-resource dictionary coalescing'
Require ([regex]::IsMatch($layer,
    'new\s+Mesh\s*\{',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'pooled mesh construction exists'
Require ([regex]::IsMatch($layer,
    'new\s+Texture2D\s*\(',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'pooled texture construction exists'
Require ([regex]::IsMatch($layer,
    'static\s+readonly\s+Dictionary<RenderKey,\s*RenderPair>\s+Pairs',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'render pairs are dictionary-backed'
Require ([regex]::IsMatch($layer,
    'static\s+readonly\s+Dictionary<BoundaryChunkKey,\s*HeightResource>\s+Heights',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'heights are dictionary-backed'

# A failed upload retries a bounded number of times; it does not replace the map.
Require ([regex]::IsMatch($layer, 'MaximumRetries\s*=\s*2',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'bounded upload retry budget'
$uploadMethod = [regex]::Match($layer,
    '(?s)private\s+static\s+bool\s+Upload\s*\(.*?\n\s*\}',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase).Value
Forbid $uploadMethod 'DestroyResources|ResetWorld\s*\(' 'full-map replacement during upload'

# Capture and presentation budgets are explicit and must remain small.
Require ([regex]::IsMatch($chunk, 'CaptureBudgetPerFrame\s*=\s*2',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'capture budget'
Require ([regex]::IsMatch($chunk, 'UploadBudgetPerFrame\s*=\s*2',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'upload budget'
Require ([regex]::IsMatch($patch,
    'limit\s*=\s*HierarchicalVassalBoundaryChunkRules\.UploadBudgetPerFrame',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'worker drain uses upload budget'
Require ([regex]::IsMatch($layer, 'MaximumUploadsPerFrame\s*=\s*2',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'mesh upload budget'

# Latest-wins coalescing and completion bounds protect pending memory.
Require ([regex]::IsMatch($worker,
    'Dictionary<WorkKey,\s*HierarchicalVassalBoundaryChunkSnapshot>\s*_pending',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'pending coalescing dictionary'
Require ([regex]::IsMatch($worker,
    'Dictionary<WorkKey,\s*long>\s*_latestRevisions',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'revision coalescing dictionary'
Require ([regex]::IsMatch($worker,
    '_pending\.Count\s*>=\s*_worldChunkCount',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'pending world-chunk bound'
Require ([regex]::IsMatch($worker,
    '_completions\.Count\s*>=\s*_worldChunkCount',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'completion world-chunk bound'

# Country and city layers must consume one captured height draft.
Require ([regex]::IsMatch($models,
    'private\s+readonly\s+BoundaryHeightDraft\s+_heightDraft',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'shared height backing field'
Require ([regex]::IsMatch($models,
    'CountryHeightDraft\s*\{\s*get\s*\{\s*return\s+_heightDraft',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'country shared height accessor'
Require ([regex]::IsMatch($models,
    'CityHeightDraft\s*\{\s*get\s*\{\s*return\s+_heightDraft',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) 'city shared height accessor'
Require ([regex]::IsMatch($worker,
    'BoundaryHeightDraft\s+height\s*=.*?BoundaryVassalBoundaryHeightRules\.Pack|BoundaryHeightDraft\s+height\s*=.*?HierarchicalVassalBoundaryHeightRules\.Pack',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
    [System.Text.RegularExpressions.RegexOptions]::Singleline)) 'one height draft per chunk'

Write-Output 'Hierarchical vassal boundary mesh performance source guard passed.'
