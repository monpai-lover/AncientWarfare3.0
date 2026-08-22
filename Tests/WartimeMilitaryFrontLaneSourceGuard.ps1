$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Read-Source([string]$Path) {
    Get-Content -Raw (Join-Path $root $Path)
}

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$post = Read-Source 'Code/core/performance/AWCooperativeActorPostRunner.cs'
$index = Read-Source 'Code/core/performance/ArmyMilitaryMovementPriorityIndex.cs'

Require ($post.Contains(
    'internal static void ProcessMilitaryP0Actor(long actorId,')) `
    'military P0 must expose one shared single-actor execution entry'
Require ($index.Contains('ProcessedFrameByActor')) `
    'military duplicate suppression must be keyed by render frame'
Require ($index.Contains('internal static void BeginFrame(int frameId)')) `
    'the military priority index must expose an explicit render-frame boundary'
Require ($index.Contains('internal static int Count => Entries.Count;')) `
    'the front lane must inspect pending military work without copying actors'
Require (-not $index.Contains('ProcessedThisCycle.Clear();')) `
    'actor-cycle startup must not erase front-lane duplicate tokens'

$nativePathCount = ([regex]::Matches($post,
    'actor\.b5_checkPathMovement\(cycleElapsed\);')).Count
Require ($nativePathCount -eq 8) `
    'the front lane must reuse the existing P0 chain instead of duplicating it'

Write-Output 'Wartime military front lane source guard passed.'
