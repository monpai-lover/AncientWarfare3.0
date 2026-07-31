$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot `
    'CultiwayLargeSchedulerSourceGuardCommon.ps1')

$runnerPath =
    'Code/core/performance/AWCooperativeWorldMaintenanceRunner.cs'
$metaVersionPath =
    'Code/core/performance/AWActorMetaPartitionVersion.cs'
$dirtyIndexPath =
    'Code/core/performance/AWDirtyMetaActorIndex.cs'
$actorPatchPath =
    'Code/patch/AW_ActorMetaPartitionPatch.cs'
$dirtyPatchPath =
    'Code/patch/AW_DirtyMetaActorIndexPatch.cs'

$runner = Read-Source $runnerPath
$metaVersion = Read-Source $metaVersionPath
$dirtyIndex = Read-Source $dirtyIndexPath
$actorPatch = Read-Source $actorPatchPath
$dirtyPatch = Read-Source $dirtyPatchPath

function Compress-Source([string]$Text) {
    return [regex]::Replace($Text, '\s+', '')
}

function Test-MaintenanceSources(
    [string]$Runner,
    [string]$MetaVersion,
    [string]$DirtyIndex,
    [string]$ActorPatch,
    [string]$DirtyPatch) {
    $runnerCompact = Compress-Source $Runner
    $metaCompact = Compress-Source $MetaVersion
    $indexCompact = Compress-Source $DirtyIndex
    $actorPatchCompact = Compress-Source $ActorPatch
    $dirtyPatchCompact = Compress-Source $DirtyPatch
    $allProduction = @(
        $Runner,
        $MetaVersion,
        $DirtyIndex,
        $ActorPatch,
        $DirtyPatch) -join "`n"

    @(
        @('exact maintenance stage order',
            'privateenumMaintenanceStage{Idle,BuildingZones,CheckListsBefore,UnitContainer,BuildingContainer,SimObjectZones,PrepareActorsStart,PrepareActors,PrepareActorsIncremental,DirtyActorIndex,DirtyManagersStart,DirtyManagers,DirtyManagersParallel,DirtyMetaObjectsFirst,DestroyMetaObjects,DestroyObjects,CheckListsAfter,UnitDestroyStart,UnitDestroy,BuildingDestroyStart,BuildingDestroy,HousesStart,HousesBuildings,HousesActorsStart,HousesActors,DirtyMetaObjectsSecond,AnythingChanged,Complete}'),
        @('incremental actor stage',
            'PrepareActorsIncremental,'),
        @('dirty actor index stage',
            'DirtyActorIndex,'),
        @('parallel dirty manager stage',
            'DirtyManagersParallel,'),
        @('actor structural version',
            'AWActorMetaPartitionVersion.GetStructuralVersion(_world.units.version)'),
        @('actor partition version comparison',
            '_preparedActorPartitionVersion!=AWActorMetaPartitionVersion.Version'),
        @('unchanged partitions skip rebuild',
            'if(!actorPartitionsDirty){AdvanceToDirtyManagers();return;}'),
        @('consume dirty actor set',
            'AWActorMetaPartitionVersion.ConsumeDirtyActors(_dirtyActorPartitions)'),
        @('structure-stable changes choose incremental path',
            'if(!actorStructureDirty){_stage=MaintenanceStage.PrepareActorsIncremental;return;}'),
        @('structural changes snapshot manager actors',
            '_actors.Clear();_actors.AddRange(_world.units.getSimpleList());'),
        @('full partition rebuild',
            'RebuildActorMetaPartitions();'),
        @('full actor rank rebuild',
            'RebuildActorMetaIndices();'),
        @('incremental partition apply',
            'ApplyActorMetaPartitionChanges();'),
        @('dirty manager count',
            '_dirtyMetaManagerCount>=3'),
        @('dirty manager-specific benchmark phase',
            'GetDirtyManagerPhaseName(_metaManagers[_index])'),
        @('dirty actor index prepare',
            'AWDirtyMetaActorIndex.Prepare(_metaManagers,_aliveActors,_bufferedAliveActorCount,_dyingActors,_bufferedDyingActorCount);'),
        @('sequential dirty scope end',
            'AWDirtyMetaActorIndex.End();_stage=MaintenanceStage.DirtyMetaObjectsFirst;'),
        @('parallel dirty scope end',
            'RunDirtyManagersParallel();AWDirtyMetaActorIndex.End();_stage=MaintenanceStage.DirtyMetaObjectsFirst;'),
        @('abort dirty scope end',
            'publicvoidAbort(){AWDirtyMetaActorIndex.End();'),
        @('generation comparison',
            '_preparedWorldGeneration!=worldGeneration'),
        @('generation clears actor versions',
            'AWActorMetaPartitionVersion.Clear();'),
        @('generation clears dirty buffers',
            'AWDirtyMetaActorIndex.ClearWorldState();'),
        @('anything-changed frame read',
            'intframe=UnityEngine.Time.frameCount;'),
        @('anything-changed frame gate',
            'if(_lastAnythingChangedFrame!=frame)'),
        @('anything-changed frame commit',
            '_lastAnythingChangedFrame=frame;')
    ) | ForEach-Object {
        Require-Text $_[0] $runnerCompact $_[1]
    }

    @(
        @('meta version type',
            'internalstaticclassAWActorMetaPartitionVersion'),
        @('meta structural adjustment',
            'returnunchecked(pManagerVersion-Volatile.Read(ref_aliveManagerVersionBumps));'),
        @('meta dirty actor set',
            'privatestaticreadonlyHashSet<Actor>DirtyActors=newHashSet<Actor>();'),
        @('meta dirty actor consume',
            'internalstaticintConsumeDirtyActors(List<Actor>pTarget)'),
        @('alive false version compensation',
            'if(!pNextAlive)Interlocked.Increment(ref_aliveManagerVersionBumps);'),
        @('alive partition notification',
            'MarkPartitionChange(pActor);'),
        @('kingdom wild transition notification',
            'pPreviousKingdom.wild!=pNextKingdom.wild'),
        @('meta clear resets dirty set',
            'DirtyActors.Clear();'),
        @('meta clear resets version',
            '_version=0;'),
        @('meta clear resets alive compensation',
            '_aliveManagerVersionBumps=0;')
    ) | ForEach-Object {
        Require-Text $_[0] $metaCompact $_[1]
    }

    @(
        @('dirty index kind count', 'privateconstintKindCount=11;'),
        @('dirty index strict prepare reset',
            'internalstaticvoidPrepare('),
        @('dirty index starts by ending old scope',
            '){End();intenabledMask=0;'),
        @('dirty index activates after classification',
            'Volatile.Write(ref_activeMask,enabledMask);'),
        @('dirty index deactivates first',
            'internalstaticvoidEnd(){Volatile.Write(ref_activeMask,0);'),
        @('dirty index world clear',
            'internalstaticvoidClearWorldState()'),
        @('dirty index clears actor buffer references',
            'Array.Clear(ActorBuffers[kind],0,ActorBuffers[kind].Length);'),
        @('dirty index foreground parallel option',
            'MaxDegreeOfParallelism=AWPerformanceSettings.ForegroundParallelism'),
        @('dirty index parallel classification',
            'Parallel.For(0,_workItemCount,ParallelOptions,ClassifyWorkItemAction);'),
        @('kingdom boat semantics',
            'if(pActor.asset.is_boat)'),
        @('plot cancellation semantics',
            'pManager.cancelPlot(plot);')
    ) | ForEach-Object {
        Require-Text $_[0] $indexCompact $_[1]
    }

    Require-Count 'dirty index has one parallel loop' $indexCompact `
        'Parallel.For(' 1
    Require-Count 'runner has three parallel loops' $runnerCompact `
        'Parallel.For(' 3
    Require-Count 'runner parallel loops share options' $runnerCompact `
        ',_parallelOptions,' 3
    Require-Text 'runner foreground parallel option' $runnerCompact `
        'MaxDegreeOfParallelism=AWPerformanceSettings.ForegroundParallelism'

    @(
        'SubspeciesManager',
        'FamilyManager',
        'ArmyManager',
        'LanguageManager',
        'ReligionManager',
        'CityManager',
        'ClanManager',
        'KingdomManager',
        'WildKingdomsManager',
        'CultureManager',
        'PlotManager'
    ) | ForEach-Object {
        Require-Text "dirty index manager $_" $indexCompact `
            "case${_}:"
        Require-Text "dirty manager patch $_" $dirtyPatchCompact `
            "typeof(${_}),`"updateDirtyUnits`""
    }

    Require-Count 'eleven dirty update patches' $dirtyPatchCompact `
        '"updateDirtyUnits"' 11
    Require-Count 'eleven dirty TryApply prefixes' $dirtyPatchCompact `
        'return!AWDirtyMetaActorIndex.TryApply(__instance);' 11
    Require-Count 'eleven dirty index overloads' $indexCompact `
        'internalstaticboolTryApply(' 11

    Require-Count 'actor patch has one setAlive prefix' $actorPatchCompact `
        '[HarmonyPrefix,HarmonyPatch(typeof(Actor),nameof(Actor.setAlive))]' 1
    Require-Count 'actor patch has one setAlive postfix' $actorPatchCompact `
        '[HarmonyPostfix,HarmonyPatch(typeof(Actor),nameof(Actor.setAlive))]' 1
    Require-Count 'actor patch has one setKingdom prefix' $actorPatchCompact `
        '[HarmonyPrefix,HarmonyPatch(typeof(Actor),"setKingdom")]' 1
    Require-Count 'actor patch only targets three methods' $actorPatchCompact `
        'HarmonyPatch(typeof(Actor),' 3
    Require-Text 'actor alive prefix captures state' $actorPatchCompact `
        '__state=__instance.isAlive();'
    Require-Text 'actor alive postfix notifies version' $actorPatchCompact `
        'AWActorMetaPartitionVersion.MarkAliveCall(__instance,__state,pValue);'
    Require-Text 'actor kingdom prefix notifies version' $actorPatchCompact `
        'AWActorMetaPartitionVersion.MarkKingdomChange(__instance,pKingdomToSet);'

    Require-Before 'incremental stage precedes dirty index' $runnerCompact `
        'PrepareActorsIncremental,' 'DirtyActorIndex,'
    Require-Before 'dirty index precedes manager start' $runnerCompact `
        'DirtyActorIndex,' 'DirtyManagersStart,'
    Require-Before 'manager start precedes manager modes' $runnerCompact `
        'DirtyManagersStart,' 'DirtyManagers,'
    Require-Before 'sequential managers precede parallel managers' `
        $runnerCompact 'DirtyManagers,' 'DirtyManagersParallel,'

    foreach ($forbidden in @(
        'GeoRegion',
        'WorldboxGame',
        'SimulationWorkerPool',
        'Cultiway',
        'Task.Run(',
        'new Thread(')) {
        Forbid-Text "maintenance production forbids $forbidden" `
            $allProduction $forbidden
    }
}

function Get-MaintenanceFailures(
    [string]$Runner,
    [string]$MetaVersion,
    [string]$DirtyIndex,
    [string]$ActorPatch,
    [string]$DirtyPatch) {
    $previousFailures = $script:GuardFailures
    $script:GuardFailures =
        [System.Collections.Generic.List[string]]::new()
    try {
        Test-MaintenanceSources $Runner $MetaVersion $DirtyIndex `
            $ActorPatch $DirtyPatch
        return @($script:GuardFailures.ToArray())
    }
    finally {
        $script:GuardFailures = $previousFailures
    }
}

$baselineFailures = @(Get-MaintenanceFailures $runner $metaVersion `
    $dirtyIndex $actorPatch $dirtyPatch)
foreach ($failure in $baselineFailures) {
    $script:GuardFailures.Add($failure)
}

if ($baselineFailures.Count -eq 0) {
    $mutations = @(
        [pscustomobject]@{
            Name = 'delete_incremental_stage'
            Runner = $runner.Replace('PrepareActorsIncremental,', '')
            Meta = $metaVersion
            Index = $dirtyIndex
            ActorPatch = $actorPatch
            DirtyPatch = $dirtyPatch
            Expected = 'incremental actor stage'
        }
        [pscustomobject]@{
            Name = 'delete_plot_patch'
            Runner = $runner
            Meta = $metaVersion
            Index = $dirtyIndex
            ActorPatch = $actorPatch
            DirtyPatch = $dirtyPatch.Replace('typeof(PlotManager)',
                'typeof(CultureManager)')
            Expected = 'dirty manager patch PlotManager'
        }
        [pscustomobject]@{
            Name = 'replace_parallel_with_pool'
            Runner = $runner.Replace('Parallel.For(',
                'SimulationWorkerPool.RunIndexed(')
            Meta = $metaVersion
            Index = $dirtyIndex
            ActorPatch = $actorPatch
            DirtyPatch = $dirtyPatch
            Expected =
                'maintenance production forbids SimulationWorkerPool'
        }
        [pscustomobject]@{
            Name = 'delete_frame_dedup'
            Runner = $runner.Replace(
                'if (_lastAnythingChangedFrame != frame)', 'if (true)')
            Meta = $metaVersion
            Index = $dirtyIndex
            ActorPatch = $actorPatch
            DirtyPatch = $dirtyPatch
            Expected = 'anything-changed frame gate'
        }
    )

    foreach ($mutation in $mutations) {
        $mutatedFailures = @(Get-MaintenanceFailures `
            $mutation.Runner $mutation.Meta $mutation.Index `
            $mutation.ActorPatch $mutation.DirtyPatch)
        $expectedFailure = @($mutatedFailures | Where-Object {
            $_ -like "*$($mutation.Expected)*"
        })
        if ($expectedFailure.Count -eq 0) {
            $script:GuardFailures.Add(
                'mutation self-test was not rejected by expected rule: ' +
                "$($mutation.Name) -> $($mutation.Expected)")
        }
    }
}

Complete-Guard 'maintenance guard' `
    'Cultiway large scheduler maintenance guard passed.'
