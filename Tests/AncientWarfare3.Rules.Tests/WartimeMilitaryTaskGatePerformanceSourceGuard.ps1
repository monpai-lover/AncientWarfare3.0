$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Read-Source([string] $relativePath) {
    $path = Join-Path $repo $relativePath
    if (-not [IO.File]::Exists($path)) {
        throw "Missing source file: $relativePath"
    }
    return [IO.File]::ReadAllText($path)
}

function Source-Segment([string] $source, [string] $startMarker,
        [string] $endMarker) {
    $start = $source.IndexOf($startMarker, [StringComparison]::Ordinal)
    $end = $source.IndexOf($endMarker, $start + $startMarker.Length,
        [StringComparison]::Ordinal)
    if ($start -lt 0 -or $end -le $start) {
        throw "Cannot locate source segment $startMarker"
    }
    return $source.Substring($start, $end - $start)
}

function Assert-FastPathBeforeActor([string] $name, [string] $source) {
    if ($source -notmatch
            'if\s*\(\s*!WartimeMilitaryTaskRules\s*\.\s*' +
            'ShouldEvaluateMilitaryState\s*\([^)]*\)\s*\)\s*' +
            'return true;') {
        throw "$name must immediately allow non-civilian tasks"
    }
    $fastPath = $source.IndexOf(
        'ShouldEvaluateMilitaryState(', [StringComparison]::Ordinal)
    $actorRead = $source.IndexOf(
        '__instance.ai_object', [StringComparison]::Ordinal)
    if ($fastPath -lt 0 -or $actorRead -le $fastPath) {
        throw "$name must bypass non-civilian tasks before reading ai_object"
    }
}

$rules = Read-Source 'Code/core/lineage/WartimeMilitaryTaskRules.cs'
$gate = Read-Source 'Code/core/lineage/WartimeMilitaryTaskGate.cs'
$patch = Read-Source 'Code/patch/AW_WartimeMilitaryTaskPatch.cs'
$lifecycle = Read-Source `
    'Code/core/lineage/ActiveMilitaryLifecycleService.cs'

if (-not $rules.Contains('ShouldEvaluateMilitaryState(string pTaskId)')) {
    throw 'Wartime task rules must expose the military-state evaluation gate.'
}

$gateFastPath = $gate.IndexOf(
    'ShouldEvaluateMilitaryState(pTaskId)', [StringComparison]::Ordinal)
$gatePredicate = $gate.IndexOf(
    'IsWartimeMilitaryActor(pActor)', [StringComparison]::Ordinal)
if ($gateFastPath -lt 0 -or $gatePredicate -le $gateFastPath) {
    throw 'Non-civilian tasks must return before the military predicate runs.'
}
if ($gate -notmatch
        'if\s*\(\s*!WartimeMilitaryTaskRules\s*\.\s*' +
        'ShouldEvaluateMilitaryState\s*\(\s*pTaskId\s*\)\s*\)\s*' +
        'return true;') {
    throw 'The gate must immediately allow non-civilian tasks.'
}

if ($patch.Contains('Traverse.') -or $patch.Contains('FindActor(') -or
    $patch.Contains('FindTaskId(')) {
    throw 'Actor AI task patches must not use per-call reflection helpers.'
}
if (-not $patch.Contains('__instance.ai_object') -or
    -not $patch.Contains('__instance.task?.id')) {
    throw 'Actor AI task patches must use publicized direct fields.'
}

$setTaskPrefix = Source-Segment $patch `
    'private static bool SetTask_Prefix' '    [HarmonyPatch]'
$updatePrefix = Source-Segment $patch `
    'private static bool Update_Prefix' '    [HarmonyPatch]'
Assert-FastPathBeforeActor 'setTask prefix' $setTaskPrefix
Assert-FastPathBeforeActor 'update prefix' $updatePrefix

$warCheck = Source-Segment $lifecycle `
    'private static bool IsKingdomAtWar' `
    '        private static bool IsCurrentCaptain'
if ($warCheck -notmatch
        'MilitaryEmergencyService\s*\.\s*TryGetActiveWarId\s*\(\s*' +
        'pKingdom\s*,\s*out _\s*\)') {
    throw 'Wartime lifecycle must query the actual-war runtime index.'
}
if ($warCheck.Contains('getWars(') -or $warCheck.Contains('foreach') -or
    $warCheck.Contains('MilitaryEmergencyService.HasAny(')) {
    throw 'Wartime lifecycle must not enumerate wars or use notice semantics.'
}
if (-not $warCheck.Contains('catch { return false; }')) {
    throw 'A missing or failing war index must fail safe to peacetime.'
}

Write-Output 'Wartime military task gate performance source guard passed.'
