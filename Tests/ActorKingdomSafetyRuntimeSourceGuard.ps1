$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$patchSource = Get-Content -Raw (Join-Path $root `
    'Code/patch/AW_ActorKingdomSafetyPatch.cs')
$serviceSource = Get-Content -Raw (Join-Path $root `
    'Code/core/lineage/ActorKingdomSafetyService.cs')

function Require-Present([string]$message, [string]$source,
    [string]$needle) {
    if (-not $source.Contains($needle)) {
        throw "$message (missing: $needle)"
    }
}

function Require-Absent([string]$message, [string]$source,
    [string]$needle) {
    if ($source.Contains($needle)) {
        throw "$message (forbidden: $needle)"
    }
}

function Require-Match([string]$message, [string]$source,
    [string]$pattern) {
    if ($source -notmatch $pattern) {
        throw "$message (pattern: $pattern)"
    }
}

Require-Absent 'actor safety must not own global-list isolation state' `
    $serviceSource 'ActorListIsolationState'
Require-Absent 'actor safety must not scan and filter the global Actor list' `
    $serviceSource 'FilterRuntimeActors'
Require-Absent 'actor safety must not restore the global Actor list' `
    $serviceSource 'RestoreRuntimeActors'
Require-Absent 'actor safety service must not read the global Actor list' `
    $serviceSource 'getSimpleList()'
Require-Absent 'actor safety must not hook the UnitLayer render scan' `
    $patchSource 'HarmonyPatch(typeof(UnitLayer), "UpdateDirty")'
Require-Absent 'actor safety must not hook the global zone scan' `
    $patchSource 'HarmonyPatch(typeof(SimObjectsZones), "checkUnits")'
Require-Absent 'actor safety boundaries must propagate unrelated failures' `
    $patchSource '[HarmonyFinalizer]'

Require-Present 'actor load validates the Actor returned by vanilla' `
    $patchSource 'RepairLoadedActor(__result)'
Require-Present 'actor load queues only the Actor returned by vanilla' `
    $patchSource 'ActorKingdomSafetyService.QueueRepair(__result);'
Require-Present 'enemy checks validate the supplied Actor' `
    $patchSource '__instance?.kingdom?.asset != null'
Require-Present 'enemy checks queue only the supplied Actor' `
    $patchSource 'ActorKingdomSafetyService.QueueRepair(__instance);'
Require-Match 'zone insertion validates and queues only its supplied Actor' `
    $patchSource `
    '(?s)SimObjectsZonesAddUnit_Prefix\(Actor pActor,.*?CanEnterVanillaZoneProcessing\(.*?QueueRepair\(pActor\);.*?return false;'
Require-Match 'conquest validates and queues only its supplied Actor' `
    $patchSource `
    '(?s)CityUpdateConquest_Prefix\(Actor pActor\).*?CanEnterVanillaZoneProcessing\(.*?QueueRepair\(pActor\);.*?return false;'

Require-Present 'actor repair drain remains explicitly bounded' `
    $serviceSource 'private const int DefaultDrainBudget = 32;'
Require-Present 'actor repair drain obeys its supplied budget' `
    $serviceSource 'for (int i = 0; i < pBudget &&'
Require-Present 'actor kingdom repair drain has a named MapBox stage guard' `
    $patchSource `
    'MapBoxFrameStageGuard.Run("actor_kingdom_repair",'
Require-Present 'world reset clears actor kingdom repair state' `
    $patchSource 'ActorKingdomSafetyService.ClearRuntime();'
Require-Present 'world reset empties pending actor repairs' `
    $serviceSource 'while (PendingRepairs.TryDequeue(out _))'
Require-Present 'loaded actor repair contains invalid object reads' `
    $serviceSource 'private static bool TryRepairLoadedActor('
Require-Present 'repair diagnostics cannot dereference invalid actors' `
    $serviceSource 'DescribeActor(actor)'
Require-Present 'stale kingdom references are detached before vanilla repair' `
    $serviceSource 'pActor.kingdom = null;'
Require-Present 'validated kingdom repair bypasses school travel affiliation gates' `
    $serviceSource 'FormalAffiliationTransferScope.Open('
Require-Present 'repair failures retain their concrete runtime cause' `
    $serviceSource 'DescribeFailure(id)'

Write-Output 'Actor kingdom safety runtime source guards passed.'
