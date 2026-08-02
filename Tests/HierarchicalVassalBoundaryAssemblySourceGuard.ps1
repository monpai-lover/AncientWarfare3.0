$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$required = @(
    'Code/core/policy/HierarchicalVassalBoundaryDirtyTracker.cs',
    'Code/core/policy/HierarchicalVassalBoundaryMeshDraftRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryTopologyRules.cs',
    'Code/core/policy/HierarchicalVassalBoundaryTopologyWorker.cs',
    'Code/patch/AW_HierarchicalVassalBoundaryDirtyPatch.cs'
)

foreach ($relativePath in $required) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required boundary source is missing: $relativePath"
    }
}

$service = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/core/policy/HierarchicalVassalMapModeService.cs')
if ($service -notmatch 'MeshAuthorityActive') {
    throw 'HierarchicalVassalMapModeService must expose MeshAuthorityActive when the boundary renderer is present.'
}

$metaLibrary = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/core/policy/AWMapModeMetaLibrary.cs')
if ($metaLibrary -notmatch 'MeshAuthorityActive') {
    throw 'AWMapModeMetaLibrary must use the same boundary renderer authority gate.'
}

Write-Output 'Hierarchical vassal boundary assembly source guard passed.'
