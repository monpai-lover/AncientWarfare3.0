using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.pathfinding;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;
using HarmonyLib;
using ai;
using ai.behaviours;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_GlobalPathfindingPatch
    {
        private const float CalibrationRepeatCooldownSeconds = 0.25f;
        private const string SocializeGoToTargetTaskId = "socialize_go_to_target";
        private static readonly ConcurrentDictionary<long, CalibrationState>
            CalibrationStates = new ConcurrentDictionary<long, CalibrationState>();

        [HarmonyPrepare]
        private static bool Prepare()
        {
            return AWPathfindingRuntimeMode.IsAw3;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.goTo))]
        private static bool GoTo_Prefix(Actor __instance, WorldTile pTile, bool pPathOnWater,
            bool pWalkOnBlocks, bool pWalkOnLava, int pLimitPathfindingRegions,
            ref ExecuteEvent __result)
        {
            if (!PathfindingOwnershipService.ShouldIntercept) return true;
            if (ArmyRtsControllerService.
                    ShouldUseNativeMilitaryPath(__instance))
            {
                bool validNativeEndpoints = false;
                try
                {
                    validNativeEndpoints = __instance?.data != null &&
                        !__instance.isRekt() &&
                        __instance.current_tile?.data != null &&
                        pTile?.data != null &&
                        __instance.current_tile.Type != null &&
                        pTile.Type != null &&
                        __instance.current_tile.region != null &&
                        pTile.region != null;
                }
                catch { }
                if (!validNativeEndpoints)
                {
                    RejectNativeMilitaryMovement(__instance);
                    __result = ExecuteEvent.False;
                    return false;
                }
                if (ArmyRtsControllerService.
                        ShouldBlockLiquidMilitaryMovement(__instance,
                            pTile))
                {
                    try
                    {
                        __instance.stopMovement();
                        __instance.clearOldPath();
                        __instance.clearTileTarget();
                        __instance.beh_tile_target = null;
                    }
                    catch { }
                    __result = ExecuteEvent.False;
                    return false;
                }
                if (AWPathMovementBridge.HasOwnership(__instance))
                    AWPathMovementBridge.Cancel(__instance,
                        AWPathFailureReason.CancelledByNewRequest);
                return true;
            }
            long benchmark = RecentFeatureBenchmark.Begin();
            RuntimePerformanceDiagnostic.ActorRaceScopeToken raceToken =
                RuntimePerformanceDiagnostic.BeginActorRaceScope(__instance);
            try
            {
                __result = AWPathMovementBridge.Submit(__instance, pTile,
                    pPathOnWater, pWalkOnBlocks, pWalkOnLava,
                    pLimitPathfindingRegions);
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.PathSubmitIndex, benchmark);
                RuntimePerformanceDiagnostic.EndActorRaceScope(
                    ActorRacePerformanceMetric.PathSubmit, raceToken);
            }
            return false;
        }

        private static void RejectNativeMilitaryMovement(Actor pActor)
        {
            if (pActor == null) return;
            try
            {
                pActor.stopMovement();
                pActor.clearOldPath();
                pActor.clearTileTarget();
                pActor.beh_tile_target = null;
            }
            catch
            {
                try { pActor.clearOldPath(); }
                catch { }
                try { pActor.clearTileTarget(); }
                catch { }
                try { pActor.beh_tile_target = null; }
                catch { }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.updatePathMovement))]
        private static bool UpdatePathMovement_Prefix(Actor __instance)
        {
            if (!PathfindingOwnershipService.ShouldIntercept) return true;
            if (__instance != null &&
                (__instance.isFollowingLocalPath() || __instance.current_path_global != null)) return true;
            if (!AWPathMovementBridge.HasOwnership(__instance)) return true;
            if (!AWPathMovementBridge.ShouldPollNow(__instance)) return false;
            long benchmark = RecentFeatureBenchmark.Begin();
            long diagnostic = RuntimePerformanceDiagnostic.BeginPathStep();
            RuntimePerformanceDiagnostic.ActorRaceScopeToken raceToken =
                RuntimePerformanceDiagnostic.BeginActorRaceScope(__instance);
            try { AWPathMovementBridge.Update(__instance); }
            finally
            {
                RuntimePerformanceDiagnostic.EndPathStep(diagnostic);
                RuntimePerformanceDiagnostic.EndActorRaceScope(
                    ActorRacePerformanceMetric.PathStep, raceToken);
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.PathMovementIndex, benchmark);
            }
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.isUsingPath))]
        private static void IsUsingPath_Postfix(Actor __instance, ref bool __result)
        {
            if (!PathfindingOwnershipService.ShouldIntercept || __result ||
                __instance?.tile_target == null) return;
            __result = AWPathMovementBridge.HasOwnership(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.Dispose))]
        private static void Dispose_Prefix(Actor __instance)
        {
            if (__instance?.data == null) return;
            CalibrationStates.TryRemove(__instance.data.id, out _);
            if (!PathfindingOwnershipService.ShouldIntercept) return;
            AWPathMovementBridge.Cancel(__instance, AWPathFailureReason.CancelledByNewRequest);
        }

        [HarmonyTranspiler]
        [HarmonyBefore("inmny.cultiway")]
        [HarmonyPatch(typeof(Actor), nameof(Actor.u10_checkSmoothMovement))]
        private static IEnumerable<CodeInstruction> SmoothMovement_Transpiler(
            IEnumerable<CodeInstruction> pInstructions)
        {
            var checkCalibrate = AccessTools.Method(typeof(Actor), "checkCalibrateTargetPosition");
            var updateMovement = AccessTools.Method(typeof(Actor), "updateMovement",
                new[] { typeof(float), typeof(float) });
            var safeCalibration = AccessTools.Method(typeof(AW_GlobalPathfindingPatch),
                nameof(CheckCalibrateTargetPositionSafe));
            var directMovement = AccessTools.Method(typeof(AW_GlobalPathfindingPatch),
                nameof(UpdateMovementDirect));
            if (checkCalibrate == null || updateMovement == null || safeCalibration == null ||
                directMovement == null)
                throw new MissingMethodException("Actor smooth movement method missing");

            int calibrationMatches = 0;
            int movementMatches = 0;
            foreach (CodeInstruction instruction in pInstructions)
            {
                if (instruction.Calls(checkCalibrate))
                {
                    calibrationMatches++;
                    var replacement = new CodeInstruction(OpCodes.Call, safeCalibration);
                    replacement.labels.AddRange(instruction.labels);
                    replacement.blocks.AddRange(instruction.blocks);
                    yield return replacement;
                    continue;
                }

                if (instruction.Calls(updateMovement))
                {
                    movementMatches++;
                    var replacement = new CodeInstruction(OpCodes.Call, directMovement);
                    replacement.labels.AddRange(instruction.labels);
                    replacement.blocks.AddRange(instruction.blocks);
                    yield return replacement;
                    continue;
                }

                yield return instruction;
            }

            if (calibrationMatches != 1 || movementMatches != 1)
                throw new InvalidOperationException(
                    "Actor smooth movement call pattern changed: calibration=" +
                    calibrationMatches + ", movement=" + movementMatches);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CheckCalibrateTargetPositionSafe(Actor actor)
        {
            BaseSimObject target = actor?.beh_actor_target;
            if (target == null || actor.hasRangeAttack()) return;

            BehaviourActionActor action = actor.hasTask()
                ? actor.ai?.action
                : null;
            if (action == null || !action.calibrate_target_position) return;

            bool isActorTarget = target.isActor();
            Actor targetActor = target.a;
            WorldTile targetTile = targetActor?.current_tile;
            WorldTile tileTarget = actor.tile_target;
            if (!isActorTarget || targetTile == null || tileTarget == null)
                return;

            float dx = targetTile.x - tileTarget.x;
            float dy = targetTile.y - tileTarget.y;
            float maximumDistance =
                action.check_actor_target_position_distance;
            if (dx * dx + dy * dy <= maximumDistance * maximumDistance)
                return;
            if (actor.ai?.task?.id == SocializeGoToTargetTaskId) return;
            if (ShouldSkipRepeatedCalibration(actor, action, targetActor,
                    targetTile)) return;

            actor.clearPathForCalibration();
            action.startExecute(actor);
        }

        private static bool ShouldSkipRepeatedCalibration(Actor pActor,
            BehaviourActionActor pAction, Actor pTargetActor, WorldTile pTargetTile)
        {
            if (pActor?.data == null || pAction == null || pTargetActor?.data == null ||
                pTargetTile?.data == null) return false;

            long actorId = pActor.data.id;
            long targetId = pTargetActor.data.id;
            int targetTileId = pTargetTile.data.tile_id;
            float now = Time.unscaledTime;
            if (CalibrationStates.TryGetValue(actorId, out CalibrationState state) &&
                state.TargetId == targetId && ReferenceEquals(state.Action, pAction) &&
                now < state.NextAllowedTime)
                return true;

            CalibrationStates[actorId] = new CalibrationState(targetId, targetTileId,
                pAction, now + CalibrationRepeatCooldownSeconds);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "updateMovement", new[] { typeof(float), typeof(float) })]
        private static bool UpdateMovement_Prefix(Actor __instance, float pElapsed,
            float pWalkedDistance = 0f)
        {
            if (!AWPerformanceSettings.EnableFramePriorityScheduler) return true;
            if (!PathfindingOwnershipService.ShouldIntercept) return true;
            if (!AWPathMovementBridge.ShouldUseCustomSmoothMovement(__instance)) return true;
            UpdateCustomSmoothMovement(__instance, pElapsed, pWalkedDistance);
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void UpdateMovementDirect(Actor pActor, float pElapsed,
            float pWalkedDistance)
        {
            if (!TryPrepareMovementTarget(pActor))
            {
                ArmyRtsMovementDiagnostic.LogOutOfBounds(
                    pActor, "updateMovement_direct_after_guard");
                return;
            }
            ArmyRtsMovementDiagnostic.LogOutOfBounds(
                pActor, "updateMovement_direct_before");
            if (!AWPerformanceSettings.EnableFramePriorityScheduler)
            {
                pActor.updateMovement(pElapsed, pWalkedDistance);
                ArmyRtsMovementDiagnostic.LogOutOfBounds(
                    pActor, "updateMovement_direct_after_native");
                return;
            }
            if (PathfindingOwnershipService.ShouldIntercept &&
                AWPathMovementBridge.ShouldUseCustomSmoothMovement(pActor))
            {
                UpdateCustomSmoothMovement(pActor, pElapsed, pWalkedDistance);
                ArmyRtsMovementDiagnostic.LogOutOfBounds(
                    pActor, "updateMovement_direct_after_aw");
                return;
            }

            pActor.updateMovement(pElapsed, pWalkedDistance);
            ArmyRtsMovementDiagnostic.LogOutOfBounds(
                pActor, "updateMovement_direct_after_native");
        }

        private static bool TryPrepareMovementTarget(Actor pActor)
        {
            if (pActor?.data == null) return false;
            ReanchorOutOfBoundsActor(pActor);
            Vector2 next = pActor.next_step_position;
            bool mapReady = MapBox.width > 0 && MapBox.height > 0;
            if (AWPathLifecycleRules.IsValidMovementTarget(next.x, next.y) &&
                (!mapReady || AWPathLifecycleRules.IsInsideMap(next.x, next.y,
                    MapBox.width, MapBox.height)))
                return true;

            // updateMovement has no sentinel or map-boundary guard. When the
            // vanilla path cursor has not supplied a step yet, advance the
            // cursor once; otherwise clear the stale moving flag before the
            // empty vector can be consumed as a world position.
            try
            {
                if (pActor.isFollowingLocalPath() ||
                    pActor.current_path_global != null)
                {
                    pActor.updatePathMovement();
                }
                else if (pActor.is_moving)
                {
                    pActor.stopMovement();
                }
            }
            catch
            {
                try { pActor.stopMovement(); }
                catch { }
            }
            return false;
        }

        private static void ReanchorOutOfBoundsActor(Actor pActor)
        {
            try
            {
                Vector2 current = pActor.current_position;
                if (AWPathLifecycleRules.IsInsideMap(current.x, current.y,
                        MapBox.width, MapBox.height))
                    return;
                WorldTile tile = pActor.current_tile;
                if (tile != null &&
                    AWPathLifecycleRules.IsInsideMap(tile.x + 0.5f,
                        tile.y + 0.5f, MapBox.width, MapBox.height))
                {
                    pActor.current_position = tile.posV3;
                    ArmyRtsMovementDiagnostic.Log(
                        "movement", "position_reanchored", pActor,
                        "from=" + current.x.ToString("0.###") + "," +
                            current.y.ToString("0.###"));
                    return;
                }
                pActor.stopMovement();
            }
            catch
            {
                try { pActor.stopMovement(); }
                catch { }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void UpdateCustomSmoothMovement(Actor pActor, float pElapsed,
            float pWalkedDistance)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            long diagnostic = RuntimePerformanceDiagnostic.BeginPathSmooth();
            RuntimePerformanceDiagnostic.ActorRaceScopeToken raceToken =
                RuntimePerformanceDiagnostic.BeginActorRaceScope(pActor);
            try
            {
                AWPathMovementBridge.UpdateSmoothMovement(pActor, pElapsed,
                    pWalkedDistance);
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndPathSmooth(diagnostic, pActor);
                RuntimePerformanceDiagnostic.EndActorRaceScope(
                    ActorRacePerformanceMetric.PathSmooth, raceToken);
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.PathMovementIndex, benchmark);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearWorld_Postfix()
        {
            CalibrationStates.Clear();
            AWPathMovementBridge.Clear();
            AWPathfindingBootstrap.ClearWorld();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WorldTile), nameof(WorldTile.setTileTypes),
            new[] { typeof(TileType), typeof(TopTileType), typeof(bool) })]
        private static void SetTileTypes_Postfix(WorldTile __instance)
        {
            AWPathfindingBootstrap.Cache.MarkDirty(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WorldTile), nameof(WorldTile.setTileType),
            new[] { typeof(TileType), typeof(bool) })]
        private static void SetTileType_Postfix(WorldTile __instance)
        {
            AWPathfindingBootstrap.Cache.MarkDirty(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WorldTile), nameof(WorldTile.setTopTileType),
            new[] { typeof(TopTileType), typeof(bool) })]
        private static void SetTopTileType_Postfix(WorldTile __instance)
        {
            AWPathfindingBootstrap.Cache.MarkDirty(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WorldTile), nameof(WorldTile.startFire),
            new[] { typeof(bool) })]
        private static void StartFire_Postfix(WorldTile __instance)
        {
            AWPathfindingBootstrap.Cache.MarkDirty(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WorldTile), nameof(WorldTile.stopFire))]
        private static void StopFire_Postfix(WorldTile __instance)
        {
            AWPathfindingBootstrap.Cache.MarkDirty(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Building), "setState", new[] { typeof(BuildingState) })]
        private static void SetBuildingState_Postfix(Building __instance)
        {
            if (__instance == null) return;
            AWPathfindingBootstrap.Cache.MarkDirty(__instance.current_tile);
            foreach (WorldTile tile in __instance.tiles)
                AWPathfindingBootstrap.Cache.MarkDirty(tile);
        }

        private readonly struct CalibrationState
        {
            public CalibrationState(long pTargetId, int pTargetTileId,
                BehaviourActionActor pAction, float pNextAllowedTime)
            {
                TargetId = pTargetId;
                TargetTileId = pTargetTileId;
                Action = pAction;
                NextAllowedTime = pNextAllowedTime;
            }

            public long TargetId { get; }
            public int TargetTileId { get; }
            public BehaviourActionActor Action { get; }
            public float NextAllowedTime { get; }
        }
    }
}
