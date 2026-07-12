using AncientWarfare3.core.pathfinding;
using HarmonyLib;
using ai;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_GlobalPathfindingPatch
    {
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
            if (!PathfindingOwnershipService.ShouldIntercept || __instance?.data == null) return;
            AWPathMovementBridge.Cancel(__instance, AWPathFailureReason.CancelledByNewRequest);
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
    }
}
