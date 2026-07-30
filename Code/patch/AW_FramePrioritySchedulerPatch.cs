using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.performance;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_FramePrioritySchedulerPatch
    {
        private const float SchedulerDiagnosticsIntervalSeconds = 10f;
        private static float _schedulerDiagnosticsElapsed;
        private static bool _ensuringSaveBoundary;
        private static bool _beforeMapBoxUpdateFailed;
        private static bool _afterMapBoxUpdateFailed;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.Update))]
        private static void BeforeMapBoxUpdate(MapBox __instance,
            out long __state)
        {
            __state = 0L;
            if (_beforeMapBoxUpdateFailed) return;
            try
            {
                __state = BeforeMapBoxUpdateCore(__instance);
            }
            catch (Exception error)
            {
                _beforeMapBoxUpdateFailed = true;
                ModClass.LogWarning(
                    "AW frame-priority MapBox prefix failed and was disabled: " +
                    error);
            }
        }

        private static long BeforeMapBoxUpdateCore(MapBox pMap)
        {
            long state = 0L;
            ArmyRtsTransportService.ObserveFrameClock(
                Time.realtimeSinceStartupAsDouble,
                pMap == null || pMap.isPaused());
            AWCooperativeSimulationRunner runner =
                AWCooperativeSimulationRunner.Instance;
            bool measureHost = AWFrameSchedulerRules.ShouldMeasureHost(
                runner.RequiresControl,
                AWPerformanceSettings.EnableSchedulerDiagnostics);
            if (measureHost)
                state = AWFramePriorityGovernor.StartHostMeasurement();
            bool advancePresentationClock =
                AWFrameSchedulerRules.ShouldAdvancePresentationClock(
                    runner.RequiresControl,
                    AW3MultiplayerReplicaScope.IsReplicaSession);
            if (Config.game_loaded && !SmoothLoader.isLoading() &&
                advancePresentationClock)
                AnimationHelper.updateTime(Time.unscaledDeltaTime,
                    Time.unscaledDeltaTime);
            return state;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.Update))]
        private static void AfterMapBoxUpdate(MapBox __instance,
            long __state)
        {
            if (_afterMapBoxUpdateFailed) return;
            try
            {
                AfterMapBoxUpdateCore(__instance, __state);
            }
            catch (Exception error)
            {
                _afterMapBoxUpdateFailed = true;
                ModClass.LogWarning(
                    "AW frame-priority MapBox postfix failed and was disabled: " +
                    error);
            }
        }

        private static void AfterMapBoxUpdateCore(MapBox pMap,
            long pHostMeasurement)
        {
            if (pHostMeasurement != 0L)
                AWFramePriorityGovernor.EndHostMeasurement(pHostMeasurement);
            RefreshControlledPresentation(pMap);
            AWSimulationTickBenchmark.SyncCaptureState();
            TryLogSchedulerDiagnostics();
        }

        private static void RefreshControlledPresentation(MapBox pMap)
        {
            AWCooperativeSimulationRunner runner =
                AWCooperativeSimulationRunner.Instance;
            try
            {
                if (!runner.TryConsumePresentationRefresh(pMap, Time.frameCount))
                    return;
                pMap.zone_calculator.draw(0f);
            }
            catch (Exception error)
            {
                runner.Abort();
                AWFramePriorityGovernor.MarkFault(error);
                ModClass.LogWarning(
                    "AW controlled presentation repaint failed: " + error);
            }
            finally
            {
                runner.ClearPresentationRefreshRequest();
            }
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.Update))]
        private static Exception ClearPresentationAfterMapBoxError(
            MapBox __instance, Exception __exception)
        {
            if (__exception != null)
            {
                AWCooperativeSimulationRunner.Instance
                    .ClearPresentationRefreshRequest();
            }
            return __exception;
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
                runner.Abort();
                AWPresentationInterpolator.Reset();
                return false;
            }

            if (AW3MultiplayerReplicaScope.IsReplicaSession)
            {
                runner.Abort();
                AWPresentationInterpolator.Reset();
            }
            else if (!runner.RequiresControl)
            {
                runner.ReleaseControl();
                __state = true;
                return true;
            }

            try
            {
                runner.RunFrame(__instance);
            }
            catch (Exception error)
            {
                runner.Abort();
                AWFramePriorityGovernor.MarkFault(error);
                Config.paused = true;
                ModClass.LogWarning(
                    "AW frame-priority scheduler failed; game paused: " +
                    error);
            }
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox),
            nameof(MapBox.checkMainSimulationUpdate))]
        private static void RunNativeAuthorityAfterSimulation(bool __state)
        {
            if (!__state) return;
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

        internal static void DrainSimulationToSaveBoundary()
        {
            if (_ensuringSaveBoundary) return;
            if (AWFramePriorityGovernor.IsExecutingSimulationPhase)
                throw new InvalidOperationException(
                    "Cannot save inside an AW scheduler simulation phase.");

            _ensuringSaveBoundary = true;
            try
            {
                AWCooperativeSimulationRunner.Instance.DrainToBoundary();
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
            ResetMapBoxCallbackFailures();
            AWCooperativeSimulationRunner.Instance.Abort();
            AWAuthorityCycleService.Reset();
            AWPresentationInterpolator.Reset();
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void AbortBeforeWorldClear()
        {
            if (!AWAsyncClearWorldGuard.CleanupAllowed) return;
            ResetMapBoxCallbackFailures();
            AWCooperativeSimulationRunner.Instance.Abort();
            AWAuthorityCycleService.Reset();
            AWPresentationInterpolator.Reset();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.finishMakingWorld))]
        private static void ResetAfterWorldCreation()
        {
            ResetMapBoxCallbackFailures();
            AWCooperativeSimulationRunner.Instance.Abort();
            AWAuthorityCycleService.Reset();
            AWPresentationInterpolator.Reset();
            AWFramePriorityGovernor.ResetFault();
        }

        private static void ResetMapBoxCallbackFailures()
        {
            _beforeMapBoxUpdateFailed = false;
            _afterMapBoxUpdateFailed = false;
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
                pElapsed = 0f;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ActorManager),
            nameof(ActorManager.calculateVisibleActors))]
        private static void PreparePresentationFrame()
        {
            AWPresentationInterpolator.PrepareFrame();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.updatePos))]
        private static void SmoothVisibleActorPresentation(Actor __instance,
            ref Vector3 __result)
        {
            AWPresentationInterpolator.Apply(__instance, ref __result);
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
                    yield return new CodeInstruction(OpCodes.Call,
                        filterMethod);
                }
            }

            if (replacements == 0)
                throw new InvalidOperationException(
                    "AW scheduler could not patch MapBox render skip.");
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ActorManager), "fillVisibleObjects")]
        private static IEnumerable<CodeInstruction>
            RefreshVisibilityDuringRendering(
                IEnumerable<CodeInstruction> pInstructions)
        {
            FieldInfo visibleField = AccessTools.Field(typeof(Actor),
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

                var replacement = new CodeInstruction(OpCodes.Call,
                    refreshMethod);
                replacement.labels.AddRange(instruction.labels);
                replacement.blocks.AddRange(instruction.blocks);
                replacements++;
                yield return replacement;
            }

            if (replacements == 0)
                throw new InvalidOperationException(
                    "AW scheduler could not patch actor visibility.");
        }

        private static bool GetPresentationVisibility(Actor pActor)
        {
            if (!AWPerformanceSettings.EnableFramePriorityScheduler)
                return pActor.is_visible;

            bool visible;
            if (pActor.isInMagnet() || pActor.isInsideSomething())
                visible = false;
            else if (MapBox.isRenderGameplay())
                visible = pActor.current_tile != null &&
                          pActor.current_tile.zone != null &&
                          pActor.current_tile.zone.visible;
            else
                visible = pActor.asset.visible_on_minimap;

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
                SchedulerDiagnosticsIntervalSeconds) return;

            _schedulerDiagnosticsElapsed %=
                SchedulerDiagnosticsIntervalSeconds;
            var diagnostics = new StringBuilder("[AW3 FramePriority] ");
            diagnostics.Append(AWFramePriorityGovernor.GetDiagnostics());
            AWSimulationTickBenchmark.AppendReport(diagnostics);
            ModClass.LogInfo(diagnostics.ToString());
        }
    }
}
