$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot `
    'CultiwayLargeSchedulerSourceGuardCommon.ps1')

$scheduler = Read-Source `
    'Code/patch/AW_FramePrioritySchedulerPatch.cs'

function Get-MutatedScheduler([string]$Source, [string]$Mutation) {
    switch ($Mutation) {
        'singular_light_area' {
            return $Source.Replace(
                '"drawLightAreas"',
                '"drawLightArea"')
        }
        'replica_return_true' {
            $pattern = [regex]::new(
                '(?s)(if \(AW3MultiplayerReplicaScope\.IsReplicaSession\).*?)return false;')
            return $pattern.Replace(
                $Source,
                '${1}return true;',
                1)
        }
        'delete_bind_world' {
            return $Source.Replace(
                'AWSimulationTime.BindWorld(',
                'AWSimulationTime.MissingBindWorld(')
        }
        'complete_before_save' {
            return $Source.Replace(
                '.DrainToBoundary();',
                '.CompleteBeforeSave();')
        }
        'delete_actor_prepare_boundary' {
            return $Source.Replace(
                'EnsureActorReadBoundary("actor.presentation_prepare_retry");',
                '')
        }
        'delete_disabled_branch' {
            $pattern = [regex]::new(
                '(?s)\s*if \(!AWPerformanceSettings\.EnableFramePriorityScheduler\)\s*\{.*?ResetSchedulerState\(\s*pUnbindSimulationTime:\s*false\s*\);\s*__state = true;\s*return true;\s*\}')
            return $pattern.Replace($Source, '', 1)
        }
        'delete_disabled_reset' {
            $pattern = [regex]::new(
                '(?s)(if \(!AWPerformanceSettings\.EnableFramePriorityScheduler\)\s*\{\s*)ResetSchedulerState\(\s*pUnbindSimulationTime:\s*false\s*\);\s*')
            return $pattern.Replace($Source, '${1}', 1)
        }
        'disabled_force_true' {
            $pattern = [regex]::new(
                '(?s)(if \(!AWPerformanceSettings\.EnableFramePriorityScheduler\).*?ResetSchedulerState\(\s*pUnbindSimulationTime:\s*false)(\s*\);)')
            return $pattern.Replace(
                $Source,
                '${1}, pForce: true${2}',
                1)
        }
        'disabled_after_runframe' {
            $pattern = [regex]::new(
                '(?s)\s*if \(!AWPerformanceSettings\.EnableFramePriorityScheduler\)\s*\{.*?ResetSchedulerState\(\s*pUnbindSimulationTime:\s*false\s*\);\s*__state = true;\s*return true;\s*\}')
            $match = $pattern.Match($Source)
            if (-not $match.Success) {
                return $Source
            }

            $withoutBranch = $pattern.Replace($Source, '', 1)
            return $withoutBranch.Replace(
                'runner.RunFrame(__instance);',
                'runner.RunFrame(__instance);' + $match.Value)
        }
        default {
            throw "unknown lifecycle guard mutation: $Mutation"
        }
    }
}

function Require-Regex([string]$Name, [string]$Text,
    [string]$Pattern) {
    if (-not [regex]::IsMatch(
            $Text,
            $Pattern,
            [System.Text.RegularExpressions.RegexOptions]::Singleline)) {
        $script:GuardFailures.Add("${Name}: pattern not found '$Pattern'")
    }
}

function Require-MethodOrder([string]$Name, [string]$Text,
    [string]$Marker, [string[]]$Needles) {
    $start = $Text.IndexOf(
        $Marker,
        [System.StringComparison]::Ordinal)
    if ($start -lt 0) {
        $script:GuardFailures.Add("${Name}: missing method marker '$Marker'")
        return
    }

    $end = $Text.IndexOf(
        '[Harmony',
        $start + $Marker.Length,
        [System.StringComparison]::Ordinal)
    if ($end -lt 0) {
        $end = $Text.Length
    }
    $section = $Text.Substring($start, $end - $start)
    $cursor = 0
    foreach ($needle in $Needles) {
        $index = $section.IndexOf(
            $needle,
            $cursor,
            [System.StringComparison]::Ordinal)
        if ($index -lt 0) {
            $script:GuardFailures.Add(
                "${Name}: missing or out of order '$needle'")
            return
        }
        $cursor = $index + $needle.Length
    }
}

function Get-LifecycleFailures([string]$SchedulerSource) {
    $previousFailures = $script:GuardFailures
    $script:GuardFailures =
        [System.Collections.Generic.List[string]]::new()
    $scheduler = $SchedulerSource
    try {

@(
    'EnsureActorReadBoundary("mapbox.frame_begin")',
    'EnsureBuildingReadBoundary("mapbox.frame_begin")',
    'AWPresentationCommandQueue.DrainMainThread()',
    'AWActorPresentationSnapshots.RequestCapture()',
    'FinishPresentationFrame()',
    'AWActorPresentationRenderer.TryPrepare(',
    'AWWorldObjectPresentationRenderer.TryPrepareBuildings(',
    'DrainToBoundary()',
    'AWSimulationTime.UnbindWorld()',
    'AW3MultiplayerReplicaScope.IsReplicaSession',
    'AWAuthorityCycleService.ProcessNativeCycle()',
    'pMap.flash_effects.update(0f);',
    'AWCursorPresentationLifecycle.Reset()'
) | ForEach-Object {
    Require-Text 'scheduler lifecycle' $scheduler $_
}

Require-Regex 'controlled flash draw' $scheduler `
    'flash_effects\.draw\(0f\)'
Require-Text 'plural light-area Harmony target' $scheduler `
    '[HarmonyPatch(typeof(QuantumSpriteLibrary), "drawLightAreas")]'
Forbid-Text 'no singular light-area Harmony target' $scheduler `
    '[HarmonyPatch(typeof(QuantumSpriteLibrary), "drawLightArea")]'
Require-Regex 'autosave deferral patch' $scheduler `
    'AutoSaveManager[\s\S]*?autoSave[\s\S]*?pendingAutoSave'
Require-MethodOrder 'AW3 save barrier completion' $scheduler `
    'internal static void DrainSimulationToSaveBoundary' @(
        'DrainToBoundary()'
    )
Forbid-Text 'no obsolete save completion API' $scheduler `
    'CompleteBeforeSave('
Require-Regex 'status snapshot animation clock' $scheduler `
    'AWStatusPresentationAnimationClock\.SetSnapshotMode\('
Require-Regex 'snapshot actor rotation' $scheduler `
    'Actor\.updateRotation[\s\S]*?TryGetPreparedSample\('

Require-MethodOrder 'scheduler disabled releases native ownership once' `
    $scheduler `
    'private static bool TakeOverMainSimulation' @(
        'if (!AWPerformanceSettings.EnableFramePriorityScheduler)',
        'ResetSchedulerState(',
        'pUnbindSimulationTime: false',
        '__state = true;',
        'return true;',
        'if (!runner.RequiresControl)'
    )
Require-Regex 'scheduler disabled uses non-forced reset' $scheduler `
    'if \(!AWPerformanceSettings\.EnableFramePriorityScheduler\)\s*\{\s*ResetSchedulerState\(\s*pUnbindSimulationTime:\s*false\s*\);\s*__state = true;\s*return true;\s*\}'
Require-MethodOrder 'replica abort and presentation reset' $scheduler `
    'private static bool TakeOverMainSimulation' @(
        'AW3MultiplayerReplicaScope.IsReplicaSession',
        'ResetSchedulerState(',
        'return false;',
        'if (!runner.RequiresControl)'
    )
Require-Regex 'replica branch never enables native postfix' $scheduler `
    'if \(AW3MultiplayerReplicaScope\.IsReplicaSession\)\s*\{(?:(?!__state = true;).)*?return false;\s*\}'
Require-MethodOrder 'native authority replica defense' $scheduler `
    'private static void RunNativeAuthorityAfterSimulation' @(
        'if (AW3MultiplayerReplicaScope.IsReplicaSession)',
        'return;',
        'if (!__state)'
    )
Require-Text 'scheduler lifecycle ownership flag' $scheduler `
    '_schedulerLifecycleOwned'
Require-MethodOrder 'ownership excludes replica/native frames' $scheduler `
    'private static void BeforeMapBoxUpdate' @(
        '!replicaSession',
        '_schedulerLifecycleOwned = true;'
    )
Require-MethodOrder 'clean native fast path preserves authority state' `
    $scheduler 'private static void ResetSchedulerState' @(
        'if (!pForce',
        '!_schedulerLifecycleOwned',
        '!runner.Active',
        'runner.ReleaseControl();',
        'return;',
        'runner.Abort()',
        'AWAuthorityCycleService.Reset();'
    )
Require-MethodOrder 'actor preparation disabled fast path' $scheduler `
    'private static bool PreparePresentationFrame' @(
        'if (!AWPerformanceSettings.EnableFramePriorityScheduler)',
        'return true;',
        'runner.HasMutatingPresentationWorkInFlight',
        'EnsureActorReadBoundary("actor.presentation_prepare")',
        'AWActorPresentationSnapshots.AcquireLatest()',
        'AWActorPresentationRenderer.TryPrepare(',
        'EnsureActorReadBoundary("actor.presentation_prepare_retry")',
        'AWActorPresentationSnapshots.AcquireLatest()',
        'AWActorPresentationRenderer.TryPrepare(',
        'return true;'
    )
Require-MethodOrder 'frame capture requires scheduler mode' $scheduler `
    'private static void BeforeMapBoxUpdate' @(
        'AWPerformanceSettings.EnableFramePriorityScheduler',
        'AWActorPresentationSnapshots.RequestCapture()'
    )
Require-MethodOrder 'building preparation disabled fast path' $scheduler `
    'private static bool PrepareBuildingPresentationFrame' @(
        'if (!AWPerformanceSettings.EnableFramePriorityScheduler)',
        'return true;',
        'runner.HasMutatingPresentationWorkInFlight',
        'EnsureBuildingReadBoundary("building.presentation_prepare")',
        'AWActorPresentationSnapshots.AcquireLatest()',
        'AWWorldObjectPresentationRenderer.TryPrepareBuildings(',
        'EnsureBuildingReadBoundary("building.presentation_prepare_retry")',
        'AWActorPresentationSnapshots.AcquireLatest()',
        'AWWorldObjectPresentationRenderer.TryPrepareBuildings(',
        'return true;'
    )

Require-MethodOrder 'world creation rebinds scheduler clock' $scheduler `
    'private static void ResetAfterWorldCreation' @(
        'ResetSchedulerState(',
        'pUnbindSimulationTime: true',
        'pForce: true',
        'if (__instance?.map_stats != null)',
        'AWSimulationTime.BindWorld(__instance);'
    )
Require-MethodOrder 'lazy simulation clock binding is idempotent' `
    $scheduler 'private static void EnsureSimulationTimeBound' @(
        'if (!AWSimulationTime.IsBound',
        'pMap?.map_stats != null',
        'AWSimulationTime.BindWorld(pMap);'
    )
Require-MethodOrder 'simulation clock bound before scheduler admission' `
    $scheduler 'private static bool TakeOverMainSimulation' @(
        'EnsureSimulationTimeBound(__instance);',
        'runner.RunFrame(__instance);'
    )
Require-MethodOrder 'scheduler ownership gate ordering' $scheduler `
    'private static bool TakeOverMainSimulation' @(
        'if (SmoothLoader.isLoading())',
        'if (AW3MultiplayerReplicaScope.IsReplicaSession)',
        'if (!AWPerformanceSettings.EnableFramePriorityScheduler)',
        'EnsureSimulationTimeBound(__instance);',
        'runner.RunFrame(__instance);'
    )
Forbid-Text 'transient frame materializes only inside renderer' $scheduler `
    'AWActorTransientPresentationFrame.Prepare()'

Require-MethodOrder 'cleanup dependency order' $scheduler `
    'private static void ResetSchedulerState' @(
        'runner.Abort()',
        'AWPresentationCommandQueue.Clear();',
        'AWActorPresentationSnapshots.Reset();',
        'AWActorPresentationRenderer.Reset();',
        'AWWorldObjectPresentationRenderer.Reset();',
        'AWActorTransientPresentationFrame.Reset();',
        'AWPresentationInterpolator.Reset();',
        'AWCursorPresentationLifecycle.Reset();',
        'AWSimulationTime.',
        'AWAuthorityCycleService.Reset();',
        'AWFramePriorityGovernor.ResetFault();'
    )
Require-MethodOrder 'original MapBox exception wins' $scheduler `
    'private static Exception FinalizeFailedMapBoxUpdate' @(
        'if (__exception == null)',
        'catch (Exception cleanupException)',
        'return __exception;'
    )
Require-MethodOrder 'background fault cleanup preserves root fault' `
    $scheduler 'private static void HandleBackgroundSimulationFault' @(
        'ResetSchedulerState(',
        'pForce: true',
        'catch (Exception cleanupException)',
        'AWFramePriorityGovernor.MarkFault(pError)'
    )

$fallbacks = @(
    @('building stockpiles', 'DrawSnapshotBuildingStockpiles',
        'EnsureBuildingReadBoundary("quantum.building_stockpiles")'),
    @('building light windows', 'DrawSnapshotBuildingLightWindows',
        'EnsureBuildingReadBoundary("quantum.building_light_windows")'),
    @('building lights', 'DrawSnapshotBuildingLights',
        'EnsureBuildingReadBoundary("quantum.building_light_fallback")'),
    @('actor lights', 'DrawSnapshotUnitLights',
        'EnsureActorReadBoundary("quantum.unit_light_fallback")'),
    @('actor damage effects', 'DrawSnapshotActorDamageEffects',
        'EnsureActorReadBoundary("quantum.actor_damage_effect_fallback")'),
    @('actor highlight effects', 'DrawSnapshotActorHighlightEffects',
        'EnsureActorReadBoundary("quantum.actor_highlight_effect_fallback")'),
    @('controlled recharge', 'DrawSnapshotControlledActorRecharge',
        'EnsureActorReadBoundary("quantum.controlled_actor_recharge_fallback")'),
    @('cursor subspecies target', 'DrawSnapshotCursorSubspeciesTarget',
        'EnsureActorReadBoundary("quantum.cursor_subspecies_target_fallback")'),
    @('plot icons', 'DrawSnapshotPlotActorIcons',
        'EnsureActorReadBoundary("quantum.plot_actor_icons_fallback")'),
    @('plot removal icons', 'DrawSnapshotPlotActorRemovalIcons',
        'EnsureActorReadBoundary("quantum.plot_actor_removals_fallback")'),
    @('magnet icons', 'DrawSnapshotMagnetActorIcons',
        'EnsureActorReadBoundary("quantum.magnet_units_fallback")')
)
foreach ($fallback in $fallbacks) {
    Require-MethodOrder $fallback[0] $scheduler `
        ('private static bool ' + $fallback[1]) @(
            $fallback[2],
            'return true;'
        )
}

Require-MethodOrder 'status overlay fallback' $scheduler `
    'private static bool DrawSnapshotStatuses' @(
        'EnsureActorReadBoundary("quantum.actor_status_fallback")',
        'EnsureBuildingReadBoundary("quantum.building_status_fallback")',
        'return true;'
    )
Require-MethodOrder 'debug rendering boundary' $scheduler `
    'private static void GuardActorDebugRendering' @(
        'EnsureActorReadBoundary("mapbox.debug_render")',
        'EnsureBuildingReadBoundary("mapbox.debug_render")'
    )
Require-Text 'unsupported debug overlays' $scheduler `
    'AW_FramePriorityDebugRenderBoundaryPatch'
Require-Text 'unsupported debug actor/building boundary' $scheduler `
    'EnsureLiveObjectReadBoundary("quantum.debug_live_objects")'

Forbid-Text 'no Cultiway namespace' $scheduler 'Cultiway.'
Forbid-Text 'no Cultiway ECS' $scheduler 'Friflo.'
Forbid-Text 'no Cultiway pathfinding' $scheduler 'PathFinder.Instance'

        return @($script:GuardFailures.ToArray())
    }
    finally {
        $script:GuardFailures = $previousFailures
    }
}

$baselineFailures = @(Get-LifecycleFailures $scheduler)
foreach ($failure in $baselineFailures) {
    $script:GuardFailures.Add($failure)
}

if ($baselineFailures.Count -eq 0) {
    $mutationExpectations = [ordered]@{
        singular_light_area = 'light-area'
        replica_return_true = 'replica'
        delete_bind_world = 'BindWorld'
        complete_before_save = 'save'
        delete_actor_prepare_boundary = 'actor preparation'
        delete_disabled_branch = 'scheduler disabled'
        delete_disabled_reset = 'scheduler disabled'
        disabled_force_true = 'scheduler disabled'
        disabled_after_runframe = 'scheduler disabled'
    }
    foreach ($entry in $mutationExpectations.GetEnumerator()) {
        $mutatedScheduler = Get-MutatedScheduler $scheduler $entry.Key
        if ($mutatedScheduler -eq $scheduler) {
            $script:GuardFailures.Add(
                "mutation self-test did not mutate source: $($entry.Key)")
            continue
        }

        $mutationFailures = @(
            Get-LifecycleFailures $mutatedScheduler)
        $expectedFailure = @(
            $mutationFailures | Where-Object {
                $_ -like "*$($entry.Value)*"
            })
        if ($expectedFailure.Count -eq 0) {
            $script:GuardFailures.Add(
                "mutation self-test was not rejected by expected rule: " +
                "$($entry.Key) -> $($entry.Value)")
        }
    }
}

Complete-Guard 'scheduler lifecycle guard' `
    'Cultiway large scheduler lifecycle guard passed.'
