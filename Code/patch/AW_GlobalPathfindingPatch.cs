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
            AWPathfindingBootstrap.Finder?.Cancel(__instance.data.id,
                AWPathFailureReason.CancelledByNewRequest);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearWorld_Postfix()
        {
            AWPathfindingBootstrap.ClearWorld();
        }
    }
}
