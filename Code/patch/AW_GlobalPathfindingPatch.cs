using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using AncientWarfare3.core.pathfinding;
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

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.goTo))]
        private static bool GoTo_Prefix(Actor __instance, WorldTile pTile, bool pPathOnWater,
            bool pWalkOnBlocks, bool pWalkOnLava, int pLimitPathfindingRegions,
            ref ExecuteEvent __result)
        {
            if (!PathfindingOwnershipService.ShouldIntercept) return true;
            __result = AWPathMovementBridge.Submit(__instance, pTile, pPathOnWater,
                pWalkOnBlocks, pWalkOnLava, pLimitPathfindingRegions);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.updatePathMovement))]
        private static bool UpdatePathMovement_Prefix(Actor __instance)
        {
            if (!PathfindingOwnershipService.ShouldIntercept) return true;
            if (__instance != null &&
                (__instance.isFollowingLocalPath() || __instance.current_path_global != null)) return true;
            AWPathMovementBridge.Update(__instance);
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.isUsingPath))]
        private static void IsUsingPath_Postfix(Actor __instance, ref bool __result)
        {
            if (!PathfindingOwnershipService.ShouldIntercept || __result) return;
            __result = AWPathMovementBridge.IsUsing(__instance);
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
        [HarmonyPatch(typeof(Actor), nameof(Actor.u10_checkSmoothMovement))]
        private static IEnumerable<CodeInstruction> SmoothMovement_Transpiler(
            IEnumerable<CodeInstruction> pInstructions)
        {
            var original = AccessTools.Method(typeof(Actor), "checkCalibrateTargetPosition");
            var safe = AccessTools.Method(typeof(AW_GlobalPathfindingPatch),
                nameof(CheckCalibrateTargetPositionSafe));
            if (original == null || safe == null)
                throw new MissingMethodException("Actor target calibration method missing");

            int matches = 0;
            foreach (CodeInstruction instruction in pInstructions)
            {
                if (!instruction.Calls(original))
                {
                    yield return instruction;
                    continue;
                }

                matches++;
                var replacement = new CodeInstruction(OpCodes.Call, safe);
                replacement.labels.AddRange(instruction.labels);
                replacement.blocks.AddRange(instruction.blocks);
                yield return replacement;
            }

            if (matches != 1)
                throw new InvalidOperationException(
                    "Actor target calibration call pattern changed: " + matches);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CheckCalibrateTargetPositionSafe(Actor actor)
        {
            BaseSimObject target = actor?.beh_actor_target;
            if (target == null || actor.hasRangeAttack()) return;

            BehaviourActionActor action = actor.hasTask() ? actor.ai?.action : null;
            if (action == null || !action.calibrate_target_position) return;

            bool isActorTarget = target.isActor();
            Actor targetActor = target.a;
            WorldTile targetTile = targetActor?.current_tile;
            WorldTile tileTarget = actor.tile_target;
            if (!isActorTarget || targetTile == null || tileTarget == null) return;

            float dx = targetTile.x - tileTarget.x;
            float dy = targetTile.y - tileTarget.y;
            float maximumDistance = action.check_actor_target_position_distance;
            if (dx * dx + dy * dy <= maximumDistance * maximumDistance) return;
            if (actor.ai?.task?.id == SocializeGoToTargetTaskId) return;
            if (ShouldSkipRepeatedCalibration(actor, action, targetActor, targetTile)) return;

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
            if (!PathfindingOwnershipService.ShouldIntercept) return true;
            AWPathMovementBridge.UpdateSmoothMovement(__instance, pElapsed, pWalkedDistance);
            return false;
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
