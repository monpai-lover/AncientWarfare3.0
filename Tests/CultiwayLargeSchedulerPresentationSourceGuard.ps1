. (Join-Path $PSScriptRoot `
    'CultiwayLargeSchedulerSourceGuardCommon.ps1')

$snapshot = Read-Source `
    'Code/core/performance/AWActorPresentationSnapshot.cs'
$actors = Read-Source `
    'Code/core/performance/AWActorPresentationRenderer.cs'
$worldObjects = Read-Source `
    'Code/core/performance/AWWorldObjectPresentationRenderer.cs'
$overlays = Read-Source `
    'Code/core/performance/AWActorPresentationOverlays.cs'
$transient = Read-Source `
    'Code/core/performance/AWActorTransientPresentationFrame.cs'
$commands = Read-Source `
    'Code/core/performance/AWPresentationCommandQueue.cs'
$visibility = Read-Source `
    'Code/core/performance/AWPresentationVisibility.cs'
$clock = Read-Source `
    'Code/core/performance/AWStatusPresentationAnimationClock.cs'
$rate = Read-Source `
    'Code/core/performance/AWWorldTimeRateTracker.cs'
$interpolator = Read-Source `
    'Code/core/performance/AWPresentationInterpolator.cs'
$insideBoat = Read-Source `
    'Code/core/performance/AWInsideBoatActorIndex.cs'

function Compress-Source([string]$Text) {
    return [regex]::Replace($Text, '\s+', '')
}

function Get-SourceRegion([string]$Name, [string]$Text,
    [string]$StartNeedle, [string]$EndNeedle) {
    $startIndex = $Text.IndexOf(
        $StartNeedle, [System.StringComparison]::Ordinal)
    $endIndex = $Text.IndexOf(
        $EndNeedle, [System.StringComparison]::Ordinal)
    if ($startIndex -lt 0 -or $endIndex -le $startIndex) {
        $script:GuardFailures.Add(
            "${Name}: source region '$StartNeedle' -> '$EndNeedle' is missing")
        return ''
    }

    return $Text.Substring($startIndex, $endIndex - $startIndex)
}

function Test-SnapshotReferenceReset([string]$SnapshotSource) {
    $reset = Compress-Source (Get-SourceRegion `
        'snapshot reset' $SnapshotSource `
        'internal void Reset()' `
        'private void CopyStableDataFrom(')
    $clearReferenceBuffers = Compress-Source (Get-SourceRegion `
        'snapshot reference buffer clearing' $SnapshotSource `
        'private void ClearReferenceBuffers()' `
        'private void CopyStableDataFrom(')

    Require-Before 'snapshot clears references before zeroing counts' `
        $reset 'ClearReferenceBuffers();' 'Count=0;'
    foreach ($buffer in @(
        'samples',
        'statuses',
        'statusFrames',
        'lights',
        'buildings',
        'stockpileResources',
        'buildingLights',
        'worldLights',
        'fires',
        'projectiles',
        'resourceThrows')) {
        Require-Text "snapshot clears full $buffer buffer" `
            $clearReferenceBuffers `
            "Array.Clear($buffer,0,$buffer.Length);"
    }
}

function Test-SnapshotReferenceResetMutations([string]$SnapshotSource) {
    foreach ($buffer in @('samples', 'buildings', 'projectiles')) {
        $pattern = [regex]::new(
            "Array\.Clear\(\s*$buffer\s*,\s*0\s*,\s*$buffer\.Length\s*\);")
        $mutated = $pattern.Replace($SnapshotSource, '', 1)
        if ($mutated -eq $SnapshotSource) {
            $script:GuardFailures.Add(
                "snapshot reset mutation did not remove $buffer clearing")
            continue
        }

        $failureStart = $script:GuardFailures.Count
        Test-SnapshotReferenceReset $mutated
        $mutationFailures = @(
            $script:GuardFailures | Select-Object -Skip $failureStart)
        while ($script:GuardFailures.Count -gt $failureStart) {
            $script:GuardFailures.RemoveAt(
                $script:GuardFailures.Count - 1)
        }
        if (-not ($mutationFailures | Where-Object {
                    $_ -like "*$buffer*"
                })) {
            $script:GuardFailures.Add(
                "snapshot reset guard accepted removal of $buffer clearing")
        }
    }
}

function Test-PresentationSources(
    [string]$SnapshotSource,
    [string]$ActorSource,
    [string]$WorldObjectSource,
    [string]$OverlaySource,
    [string]$TransientSource,
    [string]$CommandSource,
    [string]$VisibilitySource,
    [string]$ClockSource,
    [string]$RateSource,
    [string]$InterpolatorSource,
    [string]$InsideBoatSource) {
    $snapshotCompact = Compress-Source $SnapshotSource
    $worldMatch = Compress-Source (Get-SourceRegion `
        'snapshot world match' $SnapshotSource `
        'internal bool MatchesCurrentWorld =>' `
        'internal AWActorPresentationSnapshot()')
    $current = Compress-Source (Get-SourceRegion `
        'current snapshot' $SnapshotSource `
        'internal static AWActorPresentationSnapshot Current' `
        'internal static bool HasPublishedSnapshot')
    $hasPublished = Compress-Source (Get-SourceRegion `
        'published snapshot' $SnapshotSource `
        'internal static bool HasPublishedSnapshot' `
        'internal static void RequestCapture()')
    $acquireLatest = Compress-Source (Get-SourceRegion `
        'acquire latest snapshot' $SnapshotSource `
        'internal static AWActorPresentationSnapshot AcquireLatest()' `
        'internal static bool TryGetCurrent(')
    $tryGetCurrent = Compress-Source (Get-SourceRegion `
        'get current actor' $SnapshotSource `
        'internal static bool TryGetCurrent(' `
        'internal static void Reset()')
    $publishWriter = Compress-Source (Get-SourceRegion `
        'publish writer' $SnapshotSource `
        'private static void PublishWriter(' `
        'private static void ResetSlotOwnership()')
    $resetOwnership = Compress-Source (Get-SourceRegion `
        'reset slot ownership' $SnapshotSource `
        'private static void ResetSlotOwnership()' `
        'private static void RecordCaptureDuration(')
    $actorRenderer = Compress-Source (Get-SourceRegion `
        'actor renderer' $ActorSource `
        'internal static bool TryPrepare(' `
        'internal static bool TryUseBaseVisibleCount(')
    $worldRenderer = Compress-Source (Get-SourceRegion `
        'world renderer' $WorldObjectSource `
        'internal static bool TryPrepareBuildings(' `
        'internal static bool TryGetPresentationState(')

    Test-SnapshotReferenceReset $SnapshotSource

    @(
        @('snapshot manager', $snapshotCompact,
            'internalstaticclassAWActorPresentationSnapshots'),
        @('three-slot snapshot ownership', $snapshotCompact,
            'privateconstintSlotCount=3;'),
        @('writer snapshot slot', $snapshotCompact,
            'privatestaticintwriterIndex;'),
        @('ready snapshot slot', $snapshotCompact,
            'privatestaticintreadyIndex=-1;'),
        @('render snapshot slot', $snapshotCompact,
            'privatestaticintrenderIndex=-1;'),
        @('free snapshot slot ownership', $snapshotCompact,
            'privatestaticreadonlyStack<int>freeSlots=new(SlotCount);'),
        @('snapshot ownership publication', $snapshotCompact,
            'PublishWriter(requestGeneration,writer.Count);'),
        @('capture request', $snapshotCompact, 'RequestCapture()'),
        @('captured world seed', $snapshotCompact,
            'WorldSeedId=AWSimulationTime.BoundWorldSeedId;'),
        @('exact snapshot world identity', $worldMatch,
            'internalboolMatchesCurrentWorld=>AWSimulationTime.IsBound&&WorldGeneration==AWSimulationTime.Generation&&WorldSeedId==AWSimulationTime.BoundWorldSeedId;'),
        @('current validates world identity', $current,
            'returnsnapshot.MatchesCurrentWorld?snapshot:null;'),
        @('published validates world identity', $hasPublished,
            'returnindex>=0&&slots[index].MatchesCurrentWorld;'),
        @('acquire validates world identity', $acquireLatest,
            'returnsnapshot.MatchesCurrentWorld?snapshot:null;'),
        @('current actor reads validated current snapshot', $tryGetCurrent,
            'AWActorPresentationSnapshotsnapshot=Current;'),
        @('acquire transfers ready ownership', $acquireLatest,
            'intpreviousRender=renderIndex;renderIndex=readyIndex;readyIndex=-1;'),
        @('acquire returns old render slot to free stack', $acquireLatest,
            'if(previousRender>=0){freeSlots.Push(previousRender);'),
        @('publish takes the next free writer', $publishWriter,
            'writerIndex=freeSlots.Pop();readyIndex=completedWriter;'),
        @('reset restores all slot ownership', $resetOwnership,
            'freeSlots.Clear();writerIndex=0;readyIndex=-1;renderIndex=-1;freeSlots.Push(2);freeSlots.Push(1);'),
        @('actor renderer validates world identity', $actorRenderer,
            '!snapshot.MatchesCurrentWorld'),
        @('actor renderer validates manager ownership', $actorRenderer,
            '!ReferenceEquals(manager,World.world?.units)'),
        @('world renderer validates world identity', $worldRenderer,
            '!snapshot.MatchesCurrentWorld'),
        @('world renderer validates manager ownership', $worldRenderer,
            '!ReferenceEquals(manager,World.world?.buildings)'),
        @('dynamic capture parallelism', $snapshotCompact,
            'Parallel.For('),
        @('actor prepared snapshot', $ActorSource, 'PreparedSnapshot'),
        @('overlay snapshot render', $OverlaySource, 'TryDrawStatuses('),
        @('transient snapshot render', $TransientSource, 'TryDrawDamage('),
        @('main thread command drain', $CommandSource, 'DrainMainThread()'),
        @('visibility signature', $VisibilitySource, 'GetSignature('),
        @('snapshot animation mode', $ClockSource, 'SetSnapshotMode('),
        @('actual world time rate', $RateSource, 'HasActualSpeed'),
        @('paused authoritative snap', $InterpolatorSource,
            'IsWorldPaused()')
    ) | ForEach-Object {
        Require-Text $_[0] $_[1] $_[2]
    }

    $presentationSources = @(
        $SnapshotSource,
        $ActorSource,
        $WorldObjectSource,
        $OverlaySource,
        $TransientSource,
        $CommandSource,
        $VisibilitySource,
        $ClockSource,
        $RateSource,
        $InterpolatorSource,
        $InsideBoatSource
    ) -join "`n"
    $presentationCompact = Compress-Source $presentationSources
    @(
        @('no Cultiway namespace', 'Cultiway.'),
        @('no Cultiway zone dirty index',
            'ActorZoneMembershipDirtyIndex'),
        @('no Cultiway zone dirty kind', 'ActorZoneDirtyKind'),
        @('no Task.Run', 'Task.Run('),
        @('no extra raw thread', 'newThread('),
        @('no competing worker pool', 'SimulationWorkerPool')
    ) | ForEach-Object {
        Forbid-Text $_[0] $presentationCompact $_[1]
    }
    Require-Text 'AW3 settings adapter' $presentationSources `
        'AWPerformanceSettings'
    Require-Text 'AW3 simulation time adapter' $presentationSources `
        'AWSimulationTime'
}

Test-PresentationSources $snapshot $actors $worldObjects $overlays `
    $transient $commands $visibility $clock $rate $interpolator $insideBoat
if ($script:GuardFailures.Count -eq 0) {
    Test-SnapshotReferenceResetMutations $snapshot
}

Complete-Guard 'presentation guard' `
    'Cultiway large scheduler presentation guard passed.'
