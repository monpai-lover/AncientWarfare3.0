using System;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_PathfindingSafetyPatch
    {
        [HarmonyFinalizer]
        [HarmonyPatch(typeof(RegionPathFinder), nameof(RegionPathFinder.getGlobalPath))]
        public static Exception GetGlobalPath_Finalizer(
            RegionPathFinder __instance,
            WorldTile pFrom,
            WorldTile pTarget,
            ref PathFinderResult __result,
            Exception __exception)
        {
            if (!PathfindingSafetyRules.ShouldConvertGlobalPathExceptionToNotFound(
                    __exception,
                    pHasStartTile: pFrom != null,
                    pHasTargetTile: pTarget != null))
                return __exception;

            __result = PathFinderResult.NotFound;
            ClearLastGlobalPath(__instance);
            return null;
        }

        private static void ClearLastGlobalPath(RegionPathFinder pFinder)
        {
            if (pFinder == null) return;
            pFinder.last_globalPath = null;
        }
    }
}
