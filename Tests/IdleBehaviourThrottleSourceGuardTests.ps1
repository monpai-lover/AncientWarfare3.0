param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Read-Source([string]$relativePath) {
    return Get-Content -Raw -LiteralPath (Join-Path $root $relativePath)
}

$gate = Read-Source 'Code/core/performance/AWIdleBehaviourThrottleGate.cs'
$service = Read-Source 'Code/core/performance/AWIdleBehaviourThrottleService.cs'
$patch = Read-Source 'Code/patch/AW_IdleBehaviourThrottlePatch.cs'

if (-not $gate.Contains('Dictionary<long, ActorCooldownState>')) {
    throw 'Idle throttle state must be indexed directly by actor id.'
}

$removeStart = $gate.IndexOf('public void RemoveActor(long actorId)',
    [StringComparison]::Ordinal)
$clearStart = $gate.IndexOf('public void Clear()', $removeStart,
    [StringComparison]::Ordinal)
if ($removeStart -lt 0 -or $clearStart -le $removeStart) {
    throw 'Cannot locate the idle throttle RemoveActor boundary.'
}
$removeBody = $gate.Substring($removeStart, $clearStart - $removeStart)
if (-not $removeBody.Contains('_actorCooldowns.Remove(actorId);')) {
    throw 'RemoveActor must directly remove the actor-id dictionary entry.'
}
if ($removeBody.Contains('foreach') -or $removeBody.Contains('.Keys')) {
    throw 'RemoveActor must not scan idle throttle keys.'
}

if (-not $service.Contains('public static void ClearRuntime()') -or
    -not $service.Contains('Gate.Clear();')) {
    throw 'Idle throttle service must expose runtime clearing.'
}
if (-not $patch.Contains(
        '[HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]') -or
    -not $patch.Contains('AWIdleBehaviourThrottleService.ClearRuntime();')) {
    throw 'MapBox.clearWorld must clear idle throttle runtime state.'
}

Write-Output 'IdleBehaviourThrottleSourceGuardTests: PASS'
