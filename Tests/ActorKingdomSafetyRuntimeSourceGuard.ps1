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

function Get-HarmonyAttributeGroups([string]$source) {
    return @([regex]::Matches($source,
        '(?is)(?:\s*\[[^\[\]]*\]\s*)+') | Where-Object {
            $_.Value -match '\[\s*Harmony'
        })
}

function Test-HarmonyPatchTarget([string]$attributes, [string]$typeName,
    [string]$methodName) {
    $type = [regex]::Escape($typeName)
    $method = [regex]::Escape($methodName)
    $hasType = $attributes -match
        "(?is)HarmonyPatch\s*\(\s*typeof\s*\(\s*$type\s*\)"
    $hasMethod = $attributes -match
        "(?is)HarmonyPatch\s*\([^\]]*(?:`"$method`"|nameof\s*\(\s*$type\s*\.\s*$method\s*\))"
    return $hasType -and $hasMethod
}

function Get-BracedBlock([string]$source, [int]$start,
    [string]$description) {
    $body = [regex]::Match($source.Substring($start),
        '(?s)\{(?>[^{}]+|(?<brace>\{)|(?<-brace>\}))*' +
        '(?(brace)(?!))\}')
    if (-not $body.Success) {
        throw "unbalanced $description body"
    }
    return $body
}

function Get-HarmonyMethodBlocks([string]$source) {
    $groups = @(Get-HarmonyAttributeGroups $source)
    $classScopes = @()
    for ($i = 0; $i -lt $groups.Count; $i++) {
        $searchEnd = if ($i + 1 -lt $groups.Count) {
            $groups[$i + 1].Index
        }
        else {
            $source.Length
        }
        $declarationStart = $groups[$i].Index + $groups[$i].Length
        $declaration = [regex]::Match($source.Substring(
                $declarationStart, $searchEnd - $declarationStart),
            '(?ms)\bclass\s+\w+[^{}]*\{')
        if ($declaration.Success) {
            $bodyStart = $declarationStart + $declaration.Index +
                $declaration.Value.LastIndexOf('{')
            $body = Get-BracedBlock $source $bodyStart 'Harmony class'
            $classScopes += [pscustomobject]@{
                Attributes = $groups[$i].Value
                Start = $bodyStart
                End = $bodyStart + $body.Length
            }
        }
    }

    $blocks = @()
    for ($i = 0; $i -lt $groups.Count; $i++) {
        $searchEnd = if ($i + 1 -lt $groups.Count) {
            $groups[$i + 1].Index
        }
        else {
            $source.Length
        }
        $declarationStart = $groups[$i].Index + $groups[$i].Length
        $declarationLength = $searchEnd - $declarationStart
        $declaration = [regex]::Match(
            $source.Substring($declarationStart, $declarationLength),
            '(?ms)\b(?:private|protected|internal|public)\s+(?:static\s+)?' +
            '(?:[\w<>,.?\[\]]+\s+)+(?<name>\w+)\s*\([^;{}]*\)\s*\{')
        if (-not $declaration.Success) {
            continue
        }
        $bodyStart = $declarationStart + $declaration.Index +
            $declaration.Value.LastIndexOf('{')
        $body = Get-BracedBlock $source $bodyStart `
            "Harmony method $($declaration.Groups['name'].Value)"
        $start = $groups[$i].Index
        $end = $bodyStart + $body.Length
        $classAttributes = @($classScopes | Where-Object {
                $_.Start -lt $start -and $_.End -gt $end
            } | Select-Object -Last 1).Attributes
        $blocks += [pscustomobject]@{
            Attributes = "$classAttributes`n$($groups[$i].Value)"
            Name = $declaration.Groups['name'].Value
            Source = $source.Substring($start, $end - $start)
        }
    }
    return $blocks
}

function Find-ActorSafetyPatchViolation([string]$source) {
    foreach ($block in @(Get-HarmonyMethodBlocks $source)) {
        if (Test-HarmonyPatchTarget $block.Attributes 'UnitLayer' `
                'UpdateDirty') {
            return 'UnitLayer.UpdateDirty global Actor isolation hook'
        }
        if (Test-HarmonyPatchTarget $block.Attributes 'SimObjectsZones' `
                'checkUnits') {
            return 'SimObjectsZones.checkUnits global Actor isolation hook'
        }

        $isFinalizer = $block.Attributes -match
            '(?is)\[\s*HarmonyFinalizer\b'
        $isSuppliedActorBoundary =
            (Test-HarmonyPatchTarget $block.Attributes 'SimObjectsZones' `
                'addUnit') -or
            (Test-HarmonyPatchTarget $block.Attributes 'City' `
                'updateConquest')
        $swallowsNullReference =
            $block.Source -match '\bNullReferenceException\b' -and
            ($block.Source -match '\breturn\s+null\s*;' -or
             $block.Source -match
                '\?\s*(?:null|default(?:\s*\(\s*Exception\s*\))?)\s*:')
        if (-not $swallowsNullReference -and
            $block.Source -match '\bNullReferenceException\b') {
            $method = [regex]::Escape($block.Name)
            $signature = [regex]::Match($block.Source,
                "(?s)\b$method\s*\((?<parameters>[^)]*)\)")
            $exception = [regex]::Match(
                $signature.Groups['parameters'].Value,
                '\b(?:System\.)?Exception\s+(?<name>\w+)')
            if ($exception.Success) {
                $name = [regex]::Escape($exception.Groups['name'].Value)
                $swallowsNullReference = $block.Source -match
                    "\b$name\s*=\s*null\s*;"
            }
        }
        if ($isFinalizer -and $isSuppliedActorBoundary -and
            $swallowsNullReference) {
            return 'supplied-Actor boundary finalizer swallows NullReferenceException'
        }
    }
    return $null
}

function Require-SafeActorPatch([string]$message, [string]$source) {
    $violation = Find-ActorSafetyPatchViolation $source
    if ($null -ne $violation) {
        throw "$message (forbidden: $violation)"
    }
}

function Get-HarmonyMethodBlock([string]$source, [string]$methodName) {
    foreach ($block in @(Get-HarmonyMethodBlocks $source)) {
        if ($block.Name -eq $methodName) {
            return $block.Source
        }
    }
    throw "missing Harmony method block: $methodName"
}

function Require-OnlyQueuedActor([string]$message, [string]$source,
    [string]$actorExpression) {
    $calls = [regex]::Matches($source,
        'ActorKingdomSafetyService\.QueueRepair\s*\(\s*(?<actor>[^\)]+)\s*\)')
    if ($calls.Count -eq 0) {
        throw "$message (missing QueueRepair call)"
    }
    foreach ($call in $calls) {
        if ($call.Groups['actor'].Value.Trim() -ne $actorExpression) {
            throw "$message (queued: $($call.Groups['actor'].Value.Trim()))"
        }
    }
}

function Require-FixtureRejected([string]$name, [string]$source) {
    if ($null -eq (Find-ActorSafetyPatchViolation $source)) {
        throw "actor safety guard fixture was not rejected: $name"
    }
}

function Require-FixtureAllowed([string]$name, [string]$source) {
    $violation = Find-ActorSafetyPatchViolation $source
    if ($null -ne $violation) {
        throw "actor safety guard fixture was rejected: $name ($violation)"
    }
}

Require-FixtureRejected 'split nameof UnitLayer hook' @'
[HarmonyPatch(typeof(UnitLayer))]
[HarmonyPatch(nameof(UnitLayer.UpdateDirty))]
[HarmonyPrefix]
private static void HiddenRenderScan() { }
'@
Require-FixtureRejected 'interleaved Harmony attribute UnitLayer hook' @'
[HarmonyPatch(typeof(UnitLayer))]
[HarmonyBefore("other.mod")]
[HarmonyPatch(nameof(UnitLayer.UpdateDirty))]
[HarmonyPrefix]
private static void HiddenRenderScan() { }
'@
Require-FixtureRejected 'spaced string UnitLayer hook' @'
[HarmonyPatch( typeof ( UnitLayer ) , "UpdateDirty" )]
[HarmonyPrefix]
private static void HiddenRenderScan() { }
'@
Require-FixtureRejected 'combined nameof zone scan hook' @'
[HarmonyPrefix]
[HarmonyPatch(typeof(SimObjectsZones), nameof(SimObjectsZones.checkUnits))]
private static void HiddenZoneScan() { }
'@
Require-FixtureRejected 'class-level UnitLayer target hook' @'
[HarmonyPatch(typeof(UnitLayer))]
internal static class HiddenUnitLayerPatch
{
    [HarmonyPatch(nameof(UnitLayer.UpdateDirty))]
    [HarmonyPrefix]
    private static void HiddenRenderScan() { }
}
'@
Require-FixtureRejected 'class-level SimObjectsZones target hook' @'
[HarmonyPatch(typeof(SimObjectsZones))]
internal static class HiddenZonesPatch
{
    [HarmonyPatch(nameof(SimObjectsZones.checkUnits))]
    [HarmonyPrefix]
    private static void HiddenZoneScan() { }
}
'@
Require-FixtureRejected 'addUnit NullReferenceException swallowing finalizer' @'
[HarmonyFinalizer]
[HarmonyPatch(typeof(SimObjectsZones), nameof(SimObjectsZones.addUnit))]
private static Exception UnsafeFinalizer(Exception error)
{
    return error is NullReferenceException ? null : error;
}
'@
Require-FixtureRejected 'split conquest NullReferenceException finalizer' @'
[HarmonyPatch(typeof(City))]
[HarmonyPatch("updateConquest")]
[HarmonyFinalizer]
private static Exception UnsafeFinalizer(Exception error)
{
    if (error is NullReferenceException) return null;
    return error;
}
'@
Require-FixtureRejected 'conquest finalizer clears NullReferenceException' @'
[HarmonyPatch(typeof(City), nameof(City.updateConquest))]
[HarmonyFinalizer]
private static Exception UnsafeFinalizer(Exception error)
{
    if (error is NullReferenceException) error = null;
    return error;
}
'@
Require-FixtureRejected 'addUnit finalizer returns default for NRE' @'
[HarmonyFinalizer]
[HarmonyPatch(typeof(SimObjectsZones), nameof(SimObjectsZones.addUnit))]
private static Exception UnsafeFinalizer(Exception error)
{
    return error is NullReferenceException ? default : error;
}
'@
Require-FixtureRejected 'conquest finalizer returns default Exception for NRE' @'
[HarmonyFinalizer]
[HarmonyPatch(typeof(City), nameof(City.updateConquest))]
private static Exception UnsafeFinalizer(Exception error)
{
    return error is NullReferenceException ? default(Exception) : error;
}
'@
Require-FixtureAllowed 'pass-through supplied-Actor boundary finalizer' @'
[HarmonyFinalizer]
[HarmonyPatch(typeof(SimObjectsZones), nameof(SimObjectsZones.addUnit))]
private static Exception SafeFinalizer(Exception error)
{
    return error;
}
'@
Require-FixtureAllowed 'unrelated NullReferenceException finalizer' @'
[HarmonyFinalizer]
[HarmonyPatch(typeof(Actor), nameof(Actor.isAllowedToLookForEnemies))]
private static Exception UnrelatedFinalizer(Exception error)
{
    return error is NullReferenceException ? null : error;
}
'@
Require-FixtureAllowed 'pass-through finalizer ignores following helper' @'
[HarmonyFinalizer]
[HarmonyPatch(typeof(SimObjectsZones), nameof(SimObjectsZones.addUnit))]
private static Exception SafeFinalizer(Exception error)
{
    return error;
}

private static Exception UnrelatedHelper(Exception error)
{
    if (error is NullReferenceException) return null;
    return error;
}
'@

$scopedPrefixFixture = @'
[HarmonyPrefix]
[HarmonyPatch(typeof(SimObjectsZones), nameof(SimObjectsZones.addUnit))]
private static bool ScopedPrefix(Actor pActor)
{
    return false;
}

private static void UnrelatedHelper(Actor otherActor)
{
    ActorKingdomSafetyRules.CanEnterVanillaZoneProcessing(true);
    ActorKingdomSafetyService.QueueRepair(pActor);
}
'@
$scopedPrefixBlock = Get-HarmonyMethodBlock $scopedPrefixFixture 'ScopedPrefix'
if ($scopedPrefixBlock -match 'CanEnterVanillaZoneProcessing' -or
    $scopedPrefixBlock -match 'QueueRepair') {
    throw 'Harmony method block included a following non-Harmony helper'
}

Require-Absent 'actor safety must not own global-list isolation state' `
    $serviceSource 'ActorListIsolationState'
Require-Absent 'actor safety must not scan and filter the global Actor list' `
    $serviceSource 'FilterRuntimeActors'
Require-Absent 'actor safety must not restore the global Actor list' `
    $serviceSource 'RestoreRuntimeActors'
Require-Absent 'actor safety service must not read the global Actor list' `
    $serviceSource 'getSimpleList()'
Require-SafeActorPatch 'actor safety patch contains unsafe global behavior' `
    $patchSource

$loadBlock = Get-HarmonyMethodBlock $patchSource 'ActorLoad_Postfix'
Require-Present 'actor load validates the Actor returned by vanilla' `
    $loadBlock 'RepairLoadedActor(__result)'
Require-OnlyQueuedActor 'actor load queues only the Actor returned by vanilla' `
    $loadBlock '__result'

$enemyBlock = Get-HarmonyMethodBlock $patchSource 'ActorEnemyCheck_Prefix'
Require-Present 'enemy checks validate the supplied Actor' `
    $enemyBlock '__instance?.kingdom?.asset != null'
Require-OnlyQueuedActor 'enemy checks queue only the supplied Actor' `
    $enemyBlock '__instance'

$addUnitBlock = Get-HarmonyMethodBlock $patchSource `
    'SimObjectsZonesAddUnit_Prefix'
Require-Present 'zone insertion validates the supplied Actor' `
    $addUnitBlock 'CanEnterVanillaZoneProcessing('
Require-OnlyQueuedActor 'zone insertion queues only the supplied Actor' `
    $addUnitBlock 'pActor'

$conquestBlock = Get-HarmonyMethodBlock $patchSource `
    'CityUpdateConquest_Prefix'
Require-Present 'conquest validates the supplied Actor' `
    $conquestBlock 'CanEnterVanillaZoneProcessing('
Require-OnlyQueuedActor 'conquest queues only the supplied Actor' `
    $conquestBlock 'pActor'

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
