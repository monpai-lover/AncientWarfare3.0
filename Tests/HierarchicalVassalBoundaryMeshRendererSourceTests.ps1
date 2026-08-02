$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
$layerPath = Join-Path $repo 'Code/core/policy/HierarchicalVassalBoundaryMeshLayer.cs'
$facadePath = Join-Path $repo 'Code/core/policy/HierarchicalVassalMapModeBoundaryLayer.cs'
if (-not [IO.File]::Exists($layerPath)) {
    throw 'mesh renderer layer source is missing'
}

$layer = [IO.File]::ReadAllText($layerPath)
$facade = [IO.File]::ReadAllText($facadePath)

$required = @(
    'MeshFilter',
    'MeshRenderer',
    'MarkDynamic',
    'TextureFormat.R8',
    'TextureFormat.Alpha8',
    'FilterMode.Bilinear',
    'TextureWrapMode.Clamp',
    'LoadRawTextureData',
    'Apply(false, false)',
    'MaterialPropertyBlock',
    '_CameraWorldPerPixel',
    'MaximumUploadsPerFrame = 2',
    'Mesh.Clear(false)',
    'SetVertices',
    'SetColors',
    'SetUVs',
    'SetTriangles',
    'TryAcceptCompletion',
    'TryGetHeight',
    'AcceptedRevisions',
    'RetryCounts',
    'NeutralTexture',
    'Pairs.TryGetValue',
    'Heights.TryGetValue',
    '1f / Mathf.Max(1, pDraft.Width)',
    '1f / Mathf.Max(1, pDraft.Height)',
    'SetMinimapHidden'
)
foreach ($needle in $required) {
    if (-not $layer.Contains($needle)) {
        throw "mesh renderer layer missing required contract: $needle"
    }
}

if ($layer -match '\bLineRenderer\b') {
    throw 'mesh renderer layer must not use LineRenderer'
}
if ($facade -match '\bLineRenderer\b') {
    throw 'compatibility facade must not retain LineRenderer'
}
foreach ($method in @('ProcessFrame', 'Reset', 'SetMinimapHidden')) {
    if (-not [regex]::IsMatch($facade,
            ('internal\s+static\s+void\s+' + $method + '\s*\('))) {
        throw "compatibility facade missing method: $method"
    }
}
if ($layer -match 'new\s+GameObject\s*\([^\)]*(?:Tile|Edge|Line)') {
    throw 'mesh renderer layer must not allocate per tile/edge objects'
}

Write-Output 'Hierarchical vassal boundary mesh renderer source tests passed.'
