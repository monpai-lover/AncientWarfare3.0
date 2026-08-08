$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$patch = Get-Content -Raw (Join-Path $root 'Code\patch\AW_VanillaLookupOptimizationPatch.cs')
$freeTile = Get-Content -Raw (Join-Path $root 'Code\core\performance\AWFreeTileSearchIndex.cs')
$requiredPatchSymbols = @('BehFindBuilding','getBuildingsTypeFromChunk','BehFindMeatSource','BehFindTargetForHunter','Finder','BehFindLover','BehTryToSocialize','AWFreeTileSearchIndex.Reset')
foreach ($symbol in $requiredPatchSymbols) {
    if (-not $patch.Contains($symbol)) { throw "vanilla lookup optimization missing $symbol" }
}
if (-not $patch.Contains('AWChunkWindowIndex.Get')) { throw 'vanilla lookup optimization must use AWChunkWindowIndex' }
if (-not $patch.Contains('return true;')) { throw 'vanilla lookup optimization must preserve original fallback' }
if (-not $patch.Contains('HarmonyPriority(Priority.Last)')) { throw 'vanilla lookup optimization must run after AW3 task gates' }
foreach ($symbol in @('TryFind','EnsureCurrentWorld','IsFreeFor')) {
    if (-not $freeTile.Contains($symbol)) { throw "free tile index missing $symbol" }
}
Write-Output 'VanillaLookupOptimizationSourceGuard passed'
