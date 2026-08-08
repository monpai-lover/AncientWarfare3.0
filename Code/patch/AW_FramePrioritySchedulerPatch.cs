using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_FramePrioritySchedulerPatch
    {
        private struct MapBoxUpdateScope
        {
            internal long HostMeasurement;
            internal bool Closed;
        }

        private const float SchedulerDiagnosticsIntervalSeconds = 10f;
        private static float _schedulerDiagnosticsElapsed;
        private static bool _pendingAutoSave;
        private static bool _pendingAutoSaveSkipDelete;
        private static bool _pendingAutoSaveForce;
        private static bool _bypassAutoSaveDeferral;
        private static bool _ensuringSaveBoundary;
        private static bool _schedulerLifecycleOwned;

        public static void SpecialPatch()
        {
            MethodInfo criticalMethod = AccessTools.Method(
                typeof(MapBox),
                nameof(MapBox.checkMainSimulationUpdate));
            Patches patchInfo = Harmony.GetPatchInfo(criticalMethod);
            bool installed = patchInfo?.Prefixes.Any(patch =>
                patch.owner == ModClass.GUID) == true;
            if (!installed)
            {
                throw new InvalidOperationException(
                    "AW3 could not take ownership of MapBox.checkMainSimulationUpdate.");
            }

            AWFramePriorityGovernor.MarkCriticalHookInstalled();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(StatusManager), nameof(StatusManager.update))]
        private static void SelectStatusPresentationAnimationClock()
        {
            AWStatusPresentationAnimationClock.SetSnapshotMode(
                AWPerformanceSettings.EnableFramePriorityScheduler &&
                !AW3MultiplayerReplicaScope.IsReplicaSession);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.Update))]
        private static void BeforeMapBoxUpdate(MapBox __instance,
            out MapBoxUpdateScope __state)
        {
            long benchmark = RecentFeatureBenchmark.BeginOutsideFrameStage();
            try
            {
                AWCooperativeSimulationRunner runner =
                    AWCooperativeSimulationRunner.Instance;
                bool replicaSession =
                    AW3MultiplayerReplicaScope.IsReplicaSession;
                if (runner.RequiresControl &&
                    !replicaSession &&
                    Config.game_loaded &&
                    !SmoothLoader.isLoading())
                {
                    _schedulerLifecycleOwned = true;
                }

                if (runner.RequiresControl)
                {
                    EnsureActorReadBoundary("mapbox.frame_begin");
                    EnsureBuildingReadBoundary("mapbox.frame_begin");
                }

                AWPresentationCommandQueue.DrainMainThread();
                __state = new MapBoxUpdateScope();
                ArmyRtsTransportService.ObserveFrameClock(
                    Time.realtimeSinceStartupAsDouble,
                    World.world == null || World.world.isPaused());

                bool measureHost = AWFrameSchedulerRules.ShouldMeasureHost(
                    runner.RequiresControl,
                    AWPerformanceSettings.EnableSchedulerDiagnostics);
                if (measureHost)
                {
                    __state.HostMeasurement =
                        AWFramePriorityGovernor.StartHostMeasurement();
                }

                bool advancePresentationClock =
                    AWFrameSchedulerRules.ShouldAdvancePresentationClock(
                        runner.RequiresControl,
                        replicaSession);
                if (Config.game_loaded &&
                    !SmoothLoader.isLoading() &&
                    AWPerformanceSettings.EnableFramePriorityScheduler &&
                    runner.RequiresControl &&
                    !replicaSession)
                {
                    AWActorPresentationSnapshots.RequestCapture();
                }

                if (Config.game_loaded &&
                    !SmoothLoader.isLoading() &&
                    advancePresentationClock)
                {
                    AnimationHelper.updateTime(
                        Time.unscaledDeltaTime,
                        Time.unscaledDeltaTime);
                }
            }
            finally
            {
                RecentFeatureBenchmark.EndOutsideFrameStage(
                    RecentFeatureBenchmarkRules.SchedulerPrefixIndex,
                    benchmark);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.Update))]
        private static void AfterMapBoxUpdate(MapBox __instance,
            ref MapBoxUpdateScope __state)
        {
            long benchmark = RecentFeatureBenchmark.BeginOutsideFrameStage();
            try
            {
                try
                {
                    AWCooperativeSimulationRunner runner =
                        AWCooperativeSimulationRunner.Instance;
                    if (runner.RequiresControl || runner.ControlledThisFrame)
                    {
                        runner.FinishPresentationFrame();
                    }
                }
                catch (Exception error)
                {
                    HandleBackgroundSimulationFault(error);
                }
                finally
                {
                    CloseHostMeasurement(ref __state);
                }

                RefreshControlledPresentation(__instance);
                AWWorldTimeRateTracker.Update(__instance);
                AWSimulationTickBenchmark.SyncCaptureState();
                TryLogSchedulerDiagnostics();
            }
            finally
            {
                RecentFeatureBenchmark.EndOutsideFrameStage(
                    RecentFeatureBenchmarkRules.SchedulerPostfixIndex,
                    benchmark);
            }
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.Update))]
        private static Exception FinalizeFailedMapBoxUpdate(
            Exception __exception,
            ref MapBoxUpdateScope __state)
        {
            if (__exception == null)
            {
                return null;
            }

            try
            {
                AWCooperativeSimulationRunner.Instance
                    .FinishPresentationFrame();
            }
            catch (Exception cleanupException)
            {
                ModClass.LogWarning(
                    "AW MapBox.Update presentation cleanup failed: " +
                    cleanupException);
            }

            try
            {
                CloseHostMeasurement(ref __state);
            }
            catch (Exception cleanupException)
            {
                ModClass.LogWarning(
                    "AW MapBox.Update host measurement cleanup failed: " +
                    cleanupException);
            }

            try
            {
                ResetSchedulerState(
                    pUnbindSimulationTime: false,
                    pForce: true);
            }
            catch (Exception cleanupException)
            {
                ModClass.LogWarning(
                    "AW MapBox.Update scheduler cleanup failed: " +
                    cleanupException);
            }

            AWFramePriorityGovernor.MarkFault(__exception);
            Config.paused = true;
            ModClass.LogWarning(
                "AW MapBox.Update failed; scheduler stopped and game paused: " +
                __exception);
            return __exception;
        }

        private static void CloseHostMeasurement(
            ref MapBoxUpdateScope pScope)
        {
            if (pScope.Closed)
            {
                return;
            }

            if (pScope.HostMeasurement != 0L)
            {
                AWFramePriorityGovernor.EndHostMeasurement(
                    pScope.HostMeasurement);
            }

            pScope.Closed = true;
        }

        private static void RefreshControlledPresentation(MapBox pMap)
        {
            AWCooperativeSimulationRunner runner =
                AWCooperativeSimulationRunner.Instance;
            try
            {
                if (pMap == null ||
                    !runner.TryConsumePresentationRefresh(
                        pMap,
                        Time.frameCount))
                {
                    return;
                }

                if (pMap.flash_effects != null)
                {
                    pMap.flash_effects.update(0f);
                    pMap.flash_effects.draw(0f);
                }

                pMap.zone_calculator.draw(0f);
            }
            catch (Exception error)
            {
                HandleBackgroundSimulationFault(error);
            }
            finally
            {
                runner.ClearPresentationRefreshRequest();
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox),
            nameof(MapBox.checkMainSimulationUpdate))]
        private static bool TakeOverMainSimulation(MapBox __instance,
            out bool __state)
        {
            __state = false;
            AWCooperativeSimulationRunner runner =
                AWCooperativeSimulationRunner.Instance;
            if (SmoothLoader.isLoading())
            {
                ResetSchedulerState(pUnbindSimulationTime: false);
                return false;
            }

            if (AW3MultiplayerReplicaScope.IsReplicaSession)
            {
                ResetSchedulerState(pUnbindSimulationTime: false);
                return false;
            }

            if (runner.RequiresControl &&
                AWWorldInitializationGate.IsPending(__instance))
            {
                ResetSchedulerState(pUnbindSimulationTime: false);
                return false;
            }

            if (!AWPerformanceSettings.EnableFramePriorityScheduler &&
                !runner.Active)
            {
                ResetSchedulerState(pUnbindSimulationTime: false);
                __state = true;
                return true;
            }

            if (!runner.RequiresControl)
            {
                ResetSchedulerState(pUnbindSimulationTime: false);
                __state = true;
                return true;
            }

            try
            {
                _schedulerLifecycleOwned = true;
                EnsureSimulationTimeBound(__instance);
                runner.RunFrame(__instance,
                    AWPerformanceSettings.EnableFramePriorityScheduler);
            }
            catch (Exception error)
            {
                HandleBackgroundSimulationFault(error);
            }

            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox),
            nameof(MapBox.checkMainSimulationUpdate))]
        private static void RunNativeAuthorityAfterSimulation(bool __state)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
            {
                return;
            }

            if (!__state)
            {
                return;
            }

            try
            {
                AWAuthorityCycleService.ProcessNativeCycle();
            }
            catch (Exception error)
            {
                AWFramePriorityGovernor.MarkFault(error);
                Config.paused = true;
                ModClass.LogWarning(
                    "AW native authority cycle failed; game paused: " +
                    error);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AutoSaveManager),
            nameof(AutoSaveManager.autoSave))]
        private static bool DeferAutoSaveUntilCycleBoundary(
            bool pSkipDelete,
            bool pForce)
        {
            if (_bypassAutoSaveDeferral)
            {
                return true;
            }

            AWCooperativeSimulationRunner runner =
                AWCooperativeSimulationRunner.Instance;
            if (!runner.RequiresControl || runner.IsAtCycleBoundary)
            {
                _pendingAutoSave = false;
                return true;
            }

            QueueDeferredAutoSave(pSkipDelete, pForce);
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AutoSaveManager),
            nameof(AutoSaveManager.update))]
        private static void FlushDeferredAutoSaveAtCycleBoundary()
        {
            AWCooperativeSimulationRunner runner =
                AWCooperativeSimulationRunner.Instance;
            if (!_pendingAutoSave || !runner.IsAtCycleBoundary)
            {
                return;
            }

            bool skipDelete = _pendingAutoSaveSkipDelete;
            bool force = _pendingAutoSaveForce;
            _pendingAutoSave = false;
            _bypassAutoSaveDeferral = true;
            try
            {
                AutoSaveManager.autoSave(skipDelete, force);
            }
            finally
            {
                _bypassAutoSaveDeferral = false;
            }
        }

        private static void QueueDeferredAutoSave(
            bool pSkipDelete,
            bool pForce)
        {
            if (!_pendingAutoSave)
            {
                _pendingAutoSaveSkipDelete = pSkipDelete;
                _pendingAutoSaveForce = pForce;
            }
            else
            {
                _pendingAutoSaveSkipDelete &= pSkipDelete;
                _pendingAutoSaveForce |= pForce;
            }

            _pendingAutoSave = true;
        }

        internal static void DrainSimulationToSaveBoundary()
        {
            if (_ensuringSaveBoundary)
            {
                return;
            }

            if (AWFramePriorityGovernor.IsExecutingSimulationPhase)
            {
                throw new InvalidOperationException(
                    "Cannot save inside an AW scheduler simulation phase.");
            }

            _ensuringSaveBoundary = true;
            try
            {
                AWCooperativeSimulationRunner.Instance
                    .DrainToBoundary();
            }
            finally
            {
                _ensuringSaveBoundary = false;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.loadWorld),
            new[] { typeof(string), typeof(bool) })]
        private static void AbortBeforeWorldLoad()
        {
            ResetSchedulerState(
                pUnbindSimulationTime: true,
                pForce: true);
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void AbortBeforeWorldClear()
        {
            if (!AWAsyncClearWorldGuard.CleanupAllowed)
            {
                return;
            }

            ResetSchedulerState(
                pUnbindSimulationTime: true,
                pForce: true);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.finishMakingWorld))]
        private static void ResetAfterWorldCreation(MapBox __instance)
        {
            ResetSchedulerState(
                pUnbindSimulationTime: true,
                pForce: true);
            if (__instance?.map_stats != null)
            {
                AWSimulationTime.BindWorld(__instance);
            }
        }

        private static void EnsureSimulationTimeBound(MapBox pMap)
        {
            if (!AWSimulationTime.IsBound &&
                pMap?.map_stats != null)
            {
                AWSimulationTime.BindWorld(pMap);
            }
        }

        private static void ResetSchedulerState(
            bool pUnbindSimulationTime,
            bool pForce = false)
        {
            AWCooperativeSimulationRunner runner =
                AWCooperativeSimulationRunner.Instance;
            if (!pForce &&
                !_schedulerLifecycleOwned &&
                !runner.Active)
            {
                runner.ReleaseControl();
                AWStatusPresentationAnimationClock
                    .SetSnapshotMode(false);
                return;
            }

            Exception firstError = null;

            RunCleanup(() => runner.Abort(), ref firstError);
            RunCleanup(() => { AWPresentationCommandQueue.Clear(); },
                ref firstError);
            RunCleanup(() => { AWActorPresentationSnapshots.Reset(); },
                ref firstError);
            RunCleanup(() => { AWActorPresentationRenderer.Reset(); },
                ref firstError);
            RunCleanup(() => { AWWorldObjectPresentationRenderer.Reset(); },
                ref firstError);
            RunCleanup(() => { AWActorTransientPresentationFrame.Reset(); },
                ref firstError);
            RunCleanup(() => { AWPresentationInterpolator.Reset(); },
                ref firstError);
            RunCleanup(() => { AWCursorPresentationLifecycle.Reset(); },
                ref firstError);
            RunCleanup(
                () => AWStatusPresentationAnimationClock
                    .SetSnapshotMode(false),
                ref firstError);
            if (pUnbindSimulationTime)
            {
                RunCleanup(() => { AWSimulationTime.UnbindWorld(); },
                    ref firstError);
            }
            else
            {
                RunCleanup(() => { AWSimulationTime.CancelTick(); },
                    ref firstError);
            }

            RunCleanup(() => { AWAuthorityCycleService.Reset(); },
                ref firstError);
            RunCleanup(() => { AWFramePriorityGovernor.ResetFault(); },
                ref firstError);
            _pendingAutoSave = false;
            _bypassAutoSaveDeferral = false;
            _schedulerLifecycleOwned = false;
            if (firstError != null)
            {
                throw firstError;
            }
        }

        private static void RunCleanup(Action pCleanup,
            ref Exception pFirstError)
        {
            try
            {
                pCleanup();
            }
            catch (Exception error)
            {
                if (pFirstError == null)
                {
                    pFirstError = error;
                }
                else
                {
                    ModClass.LogWarning(
                        "AW secondary scheduler cleanup failure: " + error);
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(DelayedActionsManager),
            nameof(DelayedActionsManager.update))]
        private static void SeparateDelayedActionClocks(ref float pElapsed)
        {
            AWCooperativeSimulationRunner runner =
                AWCooperativeSimulationRunner.Instance;
            if (!runner.IsAdvancingGameDelayedActions &&
                (runner.RequiresControl || runner.ControlledThisFrame))
            {
                pElapsed = 0f;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BuildingManager),
            nameof(BuildingManager.calculateVisibleBuildings))]
        private static bool PrepareBuildingPresentationFrame(
            BuildingManager __instance)
        {
            AWCooperativeSimulationRunner runner =
                AWCooperativeSimulationRunner.Instance;
            if (!AWPerformanceSettings.EnableFramePriorityScheduler &&
                !runner.Active &&
                !runner.HasMutatingPresentationWorkInFlight)
            {
                return true;
            }

            if (runner.HasMutatingPresentationWorkInFlight)
            {
                EnsureBuildingReadBoundary("building.presentation_prepare");
            }

            AWActorPresentationSnapshot snapshot =
                AWActorPresentationSnapshots.AcquireLatest();
            if (AWWorldObjectPresentationRenderer.TryPrepareBuildings(
                    __instance,
                    snapshot))
            {
                return false;
            }

            EnsureBuildingReadBoundary("building.presentation_prepare_retry");
            snapshot = AWActorPresentationSnapshots.AcquireLatest();
            if (AWWorldObjectPresentationRenderer.TryPrepareBuildings(
                    __instance,
                    snapshot))
            {
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ActorManager),
            nameof(ActorManager.calculateVisibleActors))]
        private static bool PreparePresentationFrame(
            ActorManager __instance)
        {
            AWCooperativeSimulationRunner runner =
                AWCooperativeSimulationRunner.Instance;
            if (!AWPerformanceSettings.EnableFramePriorityScheduler &&
                !runner.Active &&
                !runner.HasMutatingPresentationWorkInFlight)
            {
                return true;
            }

            if (runner.HasMutatingPresentationWorkInFlight)
            {
                EnsureActorReadBoundary("actor.presentation_prepare");
            }

            AWActorPresentationSnapshot snapshot =
                AWActorPresentationSnapshots.AcquireLatest();
            AWPresentationInterpolator.PrepareFrame();
            if (AWActorPresentationRenderer.TryPrepare(
                    __instance,
                    snapshot))
            {
                return false;
            }

            EnsureActorReadBoundary("actor.presentation_prepare_retry");
            snapshot = AWActorPresentationSnapshots.AcquireLatest();
            if (AWActorPresentationRenderer.TryPrepare(
                    __instance,
                    snapshot))
            {
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawUnits")]
        private static void UseSnapshotUnitCount(out int __state)
        {
            try
            {
                AWCooperativeSimulationRunner.Instance
                    .TryBeginActorPresentationOverlap();
            }
            catch (Exception error)
            {
                HandleBackgroundSimulationFault(error);
            }

            UseSnapshotBaseVisibleCount(out __state);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawUnits")]
        private static void RestoreUnitCount(int __state)
        {
            RestoreSnapshotBaseVisibleCount(__state);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawBuildings")]
        private static void BeginBuildingPresentationOverlap()
        {
            if (!AWPerformanceSettings.EnableWorldObjectPresentationSnapshots)
            {
                EnsureBuildingReadBoundary("quantum.buildings.native");
                return;
            }

            try
            {
                AWCooperativeSimulationRunner.Instance
                    .TryBeginBuildingPresentationOverlap();
            }
            catch (Exception error)
            {
                HandleBackgroundSimulationFault(error);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary),
            "drawStockpileResources")]
        private static bool DrawSnapshotBuildingStockpiles(
            QuantumSpriteAsset pAsset)
        {
            if (AWWorldObjectPresentationRenderer
                .TryDrawStockpileResources(pAsset))
            {
                return false;
            }

            EnsureBuildingReadBoundary("quantum.building_stockpiles");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary),
            "drawBuildingsLightWindows")]
        private static bool DrawSnapshotBuildingLightWindows(
            QuantumSpriteAsset pAsset)
        {
            if (AWWorldObjectPresentationRenderer
                .TryDrawBuildingLightWindows(pAsset))
            {
                return false;
            }

            EnsureBuildingReadBoundary("quantum.building_light_windows");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "checkBuildingLights")]
        private static bool DrawSnapshotBuildingLights(
            Building pBuilding,
            Color pColor)
        {
            if (AWWorldObjectPresentationRenderer.TryDrawBuildingLights(
                    pBuilding,
                    pColor))
            {
                return false;
            }

            EnsureBuildingReadBoundary("quantum.building_light_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawLightAreas")]
        private static bool DrawSnapshotLightAreas()
        {
            if (AWWorldObjectPresentationRenderer.TryDrawLightAreas())
            {
                return false;
            }

            EnsureLiveObjectReadBoundary("quantum.light_area_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawFires")]
        private static bool DrawSnapshotFires(QuantumSpriteAsset pAsset)
        {
            if (AWWorldObjectPresentationRenderer.TryDrawFires(pAsset))
            {
                return false;
            }

            EnsureLiveObjectReadBoundary("quantum.fire_tiles_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawProjectiles")]
        private static bool DrawSnapshotProjectiles(
            QuantumSpriteAsset pAsset)
        {
            if (AWWorldObjectPresentationRenderer.TryDrawProjectiles(pAsset))
                return false;
            EnsureLiveObjectReadBoundary("quantum.projectiles.native");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary),
            "drawProjectileShadows")]
        private static bool DrawSnapshotProjectileShadows(
            QuantumSpriteAsset pAsset)
        {
            if (AWWorldObjectPresentationRenderer.
                    TryDrawProjectileShadows(pAsset)) return false;
            EnsureLiveObjectReadBoundary("quantum.projectiles.native");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawParabolicUnload")]
        private static bool DrawSnapshotResourceThrows(
            QuantumSpriteAsset pAsset)
        {
            if (AWWorldObjectPresentationRenderer.
                    TryDrawResourceThrows(pAsset, shadows: false))
                return false;
            EnsureLiveObjectReadBoundary("quantum.resource_throws.native");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary),
            "drawThrowingItemsShadows")]
        private static bool DrawSnapshotResourceThrowShadows(
            QuantumSpriteAsset pAsset)
        {
            if (AWWorldObjectPresentationRenderer.
                    TryDrawResourceThrows(pAsset, shadows: true))
                return false;
            EnsureLiveObjectReadBoundary("quantum.resource_throws.native");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawUnitItems")]
        private static void UseSnapshotUnitItemCount(out int __state)
        {
            UseSnapshotBaseVisibleCount(out __state);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawUnitItems")]
        private static void RestoreUnitItemCount(int __state)
        {
            RestoreSnapshotBaseVisibleCount(__state);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawShadowsUnit")]
        private static void UseSnapshotUnitShadowCount(out int __state)
        {
            UseSnapshotBaseVisibleCount(out __state);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawShadowsUnit")]
        private static void RestoreUnitShadowCount(int __state)
        {
            RestoreSnapshotBaseVisibleCount(__state);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawUnitsAvatars")]
        private static bool DrawSnapshotUnitAvatars()
        {
            if (AWActorPresentationOverlays.TryDrawAvatars())
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.actor_avatar_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawHealthbars")]
        private static bool DrawSnapshotHealthbars(QuantumSpriteAsset pAsset)
        {
            if (AWActorPresentationOverlays.TryDrawHealthbars(pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.actor_healthbar_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary),
            "drawUnitHappinessIcons")]
        private static bool DrawSnapshotUnitHappinessIcons(
            QuantumSpriteAsset pAsset)
        {
            if (AWActorPresentationOverlays.TryDrawHappinessIcons(pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.actor_happiness_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawUnitTaskIcons")]
        private static bool DrawSnapshotUnitTaskIcons(
            QuantumSpriteAsset pAsset)
        {
            if (AWActorPresentationOverlays.TryDrawTaskIcons(pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.actor_task_icon_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawUnitMetas")]
        private static bool DrawSnapshotUnitMetas(QuantumSpriteAsset pAsset)
        {
            if (AWActorPresentationOverlays.TryDrawUnitMetas(pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.actor_meta_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "checkUnitLight")]
        private static bool DrawSnapshotUnitLights(
            Actor pActor,
            Color pColor)
        {
            if (AWActorPresentationOverlays.TryDrawUnitLights(
                    pActor,
                    pColor))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.unit_light_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary),
            "drawUnitsEffectDamage")]
        private static bool DrawSnapshotActorDamageEffects(
            QuantumSpriteAsset pAsset)
        {
            if (AWActorTransientPresentationFrame.TryDrawDamage(pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.actor_damage_effect_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary),
            "drawUnitsEffectHighlight")]
        private static bool DrawSnapshotActorHighlightEffects(
            QuantumSpriteAsset pAsset)
        {
            if (AWActorTransientPresentationFrame.TryDrawHighlights(pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.actor_highlight_effect_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary),
            "drawCursorAttackRecharge")]
        private static bool DrawSnapshotControlledActorRecharge(
            QuantumSpriteAsset pAsset)
        {
            if (AWActorTransientPresentationFrame
                .TryDrawControlledRecharge(pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.controlled_actor_recharge_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary),
            "drawCursorTargetSubspecies")]
        private static bool DrawSnapshotCursorSubspeciesTarget(
            QuantumSpriteAsset pAsset)
        {
            if (AWActorTransientPresentationFrame
                .TryDrawCursorSubspecies(pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.cursor_subspecies_target_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawPlots")]
        private static bool DrawSnapshotPlotActorIcons(
            QuantumSpriteAsset pAsset)
        {
            if (AWActorTransientPresentationFrame.TryDrawPlots(pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.plot_actor_icons_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawPlotRemovals")]
        private static bool DrawSnapshotPlotActorRemovalIcons(
            QuantumSpriteAsset pAsset)
        {
            if (AWActorTransientPresentationFrame.TryDrawPlotRemovals(
                    pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.plot_actor_removals_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawMagnetUnits")]
        private static bool DrawSnapshotMagnetActorIcons(
            QuantumSpriteAsset pAsset)
        {
            if (AWActorTransientPresentationFrame.TryDrawMagnetUnits(pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.magnet_units_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), "updateDebugGroupSystem")]
        private static void GuardActorDebugRendering()
        {
            if (!RequiresLiveDebugTextBoundary())
            {
                return;
            }

            EnsureActorReadBoundary("mapbox.debug_render");
            EnsureBuildingReadBoundary("mapbox.debug_render");
        }

        private static bool RequiresLiveDebugTextBoundary()
        {
            return DebugConfig.isOn(DebugOption.OverlaySoundsAttached) ||
                   DebugConfig.isOn(DebugOption.OverlayBoatTransport) ||
                   DebugConfig.isOn(DebugOption.OverlayActorCivs) ||
                   DebugConfig.isOn(DebugOption.OverlayCursorActor) ||
                   DebugConfig.isOn(
                       DebugOption.OverlayActorGroupLeaderOnly) ||
                   DebugConfig.isOn(
                       DebugOption.OverlayActorFavoritesOnly) ||
                   DebugConfig.isOn(DebugOption.OverlayActorMobs) ||
                   DebugConfig.isOn(DebugOption.OverlayTrees) ||
                   DebugConfig.isOn(DebugOption.OverlayPlants) ||
                   DebugConfig.isOn(DebugOption.OverlayCivBuildings) ||
                   DebugConfig.isOn(DebugOption.OverlayOtherBuildings) ||
                   DebugConfig.isOn(DebugOption.OverlayArmies) ||
                   DebugConfig.isOn(DebugOption.OverlayCity) ||
                   DebugConfig.isOn(DebugOption.OverlayCityTasks) ||
                   DebugConfig.isOn(DebugOption.OverlayKingdom);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary),
            "drawUnexploredAugmentationSprite")]
        private static bool DrawSnapshotUnexploredAugmentations(
            QuantumSpriteAsset __0)
        {
            if (AWActorPresentationOverlays
                .TryDrawUnexploredAugmentations(__0))
            {
                return false;
            }

            EnsureActorReadBoundary(
                "quantum.actor_unexplored_augmentation_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawUnitBanners")]
        private static bool DrawSnapshotUnitBanners(
            QuantumSpriteAsset pAsset)
        {
            if (AWActorPresentationOverlays.TryDrawBanners(pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.actor_banner_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawFavoritesMap")]
        private static bool DrawSnapshotFavoritesMap(
            QuantumSpriteAsset pAsset)
        {
            if (AWActorPresentationOverlays.TryDrawFavoritesMap(pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.actor_favorite_map_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawFavoritesGame")]
        private static bool DrawSnapshotFavoritesGame(
            QuantumSpriteAsset pAsset)
        {
            if (AWActorPresentationOverlays.TryDrawFavoritesGame(pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.actor_favorite_game_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawSelectedUnits")]
        private static bool DrawSnapshotSelectedUnits(
            QuantumSpriteAsset pAsset)
        {
            if (AWActorPresentationOverlays.TryDrawSelectedUnits(pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.actor_selected_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary),
            "drawUnitsToBeSelectedBySquareTool")]
        private static bool DrawSnapshotSquareSelectionUnits(
            QuantumSpriteAsset pAsset)
        {
            if (AWActorPresentationOverlays.TryDrawSquareSelectionUnits(
                    pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary(
                "quantum.actor_square_selection_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawSocialize")]
        private static bool DrawSnapshotSocialize(
            QuantumSpriteAsset pAsset)
        {
            if (AWActorPresentationOverlays.TryDrawSocialize(pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.actor_socialize_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawJustAte")]
        private static bool DrawSnapshotJustAte(QuantumSpriteAsset pAsset)
        {
            if (AWActorPresentationOverlays.TryDrawJustAte(pAsset))
            {
                return false;
            }

            EnsureActorReadBoundary("quantum.actor_just_ate_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawStatusEffects")]
        private static bool DrawSnapshotStatuses(
            QuantumSpriteAsset pAsset)
        {
            AWActorPresentationSnapshot actorSnapshot =
                AWActorPresentationRenderer.PreparedSnapshot;
            if (actorSnapshot != null &&
                ReferenceEquals(
                    actorSnapshot,
                    AWWorldObjectPresentationRenderer.PreparedSnapshot))
            {
                AWActorPresentationOverlays.TryDrawStatuses(pAsset);
                AWWorldObjectPresentationRenderer
                    .TryDrawBuildingStatuses(pAsset);
                return false;
            }

            EnsureActorReadBoundary("quantum.actor_status_fallback");
            EnsureBuildingReadBoundary("quantum.building_status_fallback");
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.updatePos))]
        private static bool UseSnapshotActorPresentationPosition(
            Actor __instance,
            ref Vector3 __result)
        {
            if (!AWPresentationInterpolator.TryApply(
                    __instance,
                    out Vector3 position))
            {
                if (AWPerformanceSettings.EnableFramePriorityScheduler)
                {
                    EnsureActorReadBoundary("actor.position_fallback");
                }

                return true;
            }

            __result = position;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.updateRotation))]
        private static bool UseSnapshotActorPresentationRotation(
            Actor __instance,
            ref Vector3 __result)
        {
            if (!AWActorPresentationRenderer.TryGetPreparedSample(
                    __instance,
                    out AWActorPresentationSample sample))
            {
                if (AWPerformanceSettings.EnableFramePriorityScheduler)
                {
                    EnsureActorReadBoundary("actor.rotation_fallback");
                }

                return true;
            }

            __result = sample.Rotation;
            return false;
        }

        private static void UseSnapshotBaseVisibleCount(
            out int pPreviousCount)
        {
            ActorManager manager = World.world?.units;
            if (!AWActorPresentationRenderer.TryUseBaseVisibleCount(
                    manager,
                    out pPreviousCount))
            {
                pPreviousCount = -1;
            }
        }

        private static void RestoreSnapshotBaseVisibleCount(
            int pPreviousCount)
        {
            if (pPreviousCount >= 0)
            {
                AWActorPresentationRenderer.RestoreVisibleCount(
                    World.world?.units,
                    pPreviousCount);
            }
        }

        private static void EnsureActorReadBoundary(string pReason)
        {
            try
            {
                AWCooperativeSimulationRunner.Instance
                    .EnsureActorReadBoundary(pReason);
            }
            catch (Exception error)
            {
                HandleBackgroundSimulationFault(error);
            }
        }

        private static void EnsureBuildingReadBoundary(string pReason)
        {
            try
            {
                AWCooperativeSimulationRunner.Instance
                    .EnsureBuildingReadBoundary(pReason);
            }
            catch (Exception error)
            {
                HandleBackgroundSimulationFault(error);
            }
        }

        internal static void EnsureLiveObjectReadBoundary(string pReason)
        {
            EnsureActorReadBoundary(pReason);
            EnsureBuildingReadBoundary(pReason);
        }

        private static void HandleBackgroundSimulationFault(
            Exception pError)
        {
            try
            {
                ResetSchedulerState(
                    pUnbindSimulationTime: false,
                    pForce: true);
            }
            catch (Exception cleanupException)
            {
                ModClass.LogWarning(
                    "AW scheduler cleanup failed after background fault: " +
                    cleanupException);
            }

            AWFramePriorityGovernor.MarkFault(pError);
            Config.paused = true;
            ModClass.LogWarning(
                "AW background simulation/presentation boundary failed; " +
                "game paused: " + pError);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.Update))]
        private static IEnumerable<CodeInstruction>
            KeepEveryRenderFrameAtHighSpeed(
                IEnumerable<CodeInstruction> pInstructions)
        {
            FieldInfo renderSkipField = AccessTools.Field(
                typeof(WorldTimeScaleAsset),
                nameof(WorldTimeScaleAsset.render_skip));
            MethodInfo filterMethod = AccessTools.Method(
                typeof(AW_FramePrioritySchedulerPatch),
                nameof(FilterRenderSkip));
            int replacements = 0;

            foreach (CodeInstruction instruction in pInstructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldfld &&
                    Equals(instruction.operand, renderSkipField))
                {
                    replacements++;
                    yield return new CodeInstruction(
                        OpCodes.Call,
                        filterMethod);
                }
            }

            if (replacements == 0)
            {
                throw new InvalidOperationException(
                    "AW scheduler could not patch MapBox render skip.");
            }
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ActorManager), "fillVisibleObjects")]
        private static IEnumerable<CodeInstruction>
            RefreshVisibilityDuringRendering(
                IEnumerable<CodeInstruction> pInstructions)
        {
            FieldInfo visibleField = AccessTools.Field(
                typeof(Actor),
                nameof(Actor.is_visible));
            MethodInfo refreshMethod = AccessTools.Method(
                typeof(AW_FramePrioritySchedulerPatch),
                nameof(GetPresentationVisibility));
            int replacements = 0;

            foreach (CodeInstruction instruction in pInstructions)
            {
                if (instruction.opcode != OpCodes.Ldfld ||
                    !Equals(instruction.operand, visibleField))
                {
                    yield return instruction;
                    continue;
                }

                var replacement = new CodeInstruction(
                    OpCodes.Call,
                    refreshMethod);
                replacement.labels.AddRange(instruction.labels);
                replacement.blocks.AddRange(instruction.blocks);
                replacements++;
                yield return replacement;
            }

            if (replacements == 0)
            {
                throw new InvalidOperationException(
                    "AW scheduler could not patch actor visibility.");
            }
        }

        private static bool GetPresentationVisibility(Actor pActor)
        {
            if (!AWPerformanceSettings.EnableFramePriorityScheduler)
            {
                return pActor.is_visible;
            }

            bool visible;
            if (pActor.isInMagnet() || pActor.isInsideSomething())
            {
                visible = false;
            }
            else if (MapBox.isRenderGameplay())
            {
                visible = pActor.current_tile != null &&
                          pActor.current_tile.zone != null &&
                          pActor.current_tile.zone.visible;
            }
            else
            {
                visible = pActor.asset.visible_on_minimap;
            }

            pActor.is_visible = visible;
            return visible;
        }

        private static bool FilterRenderSkip(bool pConfiguredRenderSkip)
        {
            AWCooperativeSimulationRunner runner =
                AWCooperativeSimulationRunner.Instance;
            return runner.RequiresControl || runner.ControlledThisFrame
                ? false
                : pConfiguredRenderSkip;
        }

        private static void TryLogSchedulerDiagnostics()
        {
            if (!AWPerformanceSettings.EnableSchedulerDiagnostics)
            {
                _schedulerDiagnosticsElapsed = 0f;
                return;
            }

            _schedulerDiagnosticsElapsed +=
                Mathf.Max(0f, Time.unscaledDeltaTime);
            if (_schedulerDiagnosticsElapsed <
                SchedulerDiagnosticsIntervalSeconds)
            {
                return;
            }

            _schedulerDiagnosticsElapsed %=
                SchedulerDiagnosticsIntervalSeconds;
            var diagnostics = new StringBuilder("[AW3 FramePriority] ");
            diagnostics.Append(AWFramePriorityGovernor.GetDiagnostics());
            AWSimulationTickBenchmark.AppendReport(diagnostics);
            ModClass.LogInfo(diagnostics.ToString());
        }
    }

    [HarmonyPatch]
    internal static class AW_FramePriorityDebugRenderBoundaryPatch
    {
        private static readonly string[] LiveObjectReaders =
        {
            "drawMoney",
            "drawUnitAttackRange",
            "drawUnitSize",
            "debugDrawArrowsUnitAttackTargets",
            "debugDrawArrowsUnitBehTarget",
            "debugDrawArrowsUnitNavigationTargets",
            "debugDrawArrowsUnitHeight",
            "debugDrawArrowsUnitNavigationPath",
            "debugDrawArrowsUnitNextStepTile",
            "debugDrawArrowsUnitNextStepPosition",
            "debugDrawArrowsUnitCurrentPosition",
            "debugDrawArrowsBoatPassengers",
            "debugDrawArrowsPassengerTaxiRequestTargets",
            "debugDrawArrowsBuildingResidents",
            "debugDrawArrowsLovers",
            "debugDrawFavoriteFoods",
            "debugDrawKingdomIcons",
            "debugDrawHoldingFoods",
            "debugDrawGodFingerTiles",
            "debugDrawDragonAttackTiles",
            "drawSwimTargets",
            "debugDrawDeadUnits",
            "debugCityZoneRange",
            "debugEnemyFinder"
        };

        private static IEnumerable<MethodBase> TargetMethods()
        {
            for (int i = 0; i < LiveObjectReaders.Length; i++)
            {
                MethodInfo method = AccessTools.Method(
                    typeof(QuantumSpriteLibrary),
                    LiveObjectReaders[i],
                    new[] { typeof(QuantumSpriteAsset) });
                if (method != null)
                {
                    yield return method;
                }
            }
        }

        [HarmonyPrefix]
        private static void BeforeDebugLiveObjectRead()
        {
            AW_FramePrioritySchedulerPatch.EnsureLiveObjectReadBoundary("quantum.debug_live_objects");
        }
    }
}
