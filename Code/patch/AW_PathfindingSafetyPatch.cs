using System;
using System.Threading;
using AncientWarfare3.core.lineage;
using HarmonyLib;
using AncientWarfare3.core.pathfinding;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_PathfindingSafetyPatch
    {
        private static long _nextGlobalPathDiagnosticAt;

        [HarmonyPrepare]
        private static bool Prepare()
        {
            return AWPathfindingRuntimeMode.IsAw3;
        }

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
            LogConvertedGlobalPathFailure(pFrom, pTarget);
            return null;
        }

        private static void LogConvertedGlobalPathFailure(
            WorldTile pFrom, WorldTile pTarget)
        {
            long now = DateTime.UtcNow.Ticks;
            long next = Volatile.Read(ref _nextGlobalPathDiagnosticAt);
            if (now < next || Interlocked.CompareExchange(
                    ref _nextGlobalPathDiagnosticAt,
                    now + TimeSpan.TicksPerMinute, next) != next)
                return;
            int fromTileId = pFrom?.data?.tile_id ?? -1;
            int targetTileId = pTarget?.data?.tile_id ?? -1;
            int fromRegionId = pFrom?.region?.id ?? -1;
            int targetRegionId = pTarget?.region?.id ?? -1;
            AncientWarfare3.ModClass.LogInfo(
                "AW3 converted stale RegionPathFinder null failure to NotFound: " +
                "fromTile=" + fromTileId +
                ", targetTile=" + targetTileId +
                ", fromRegion=" + fromRegionId +
                ", targetRegion=" + targetRegionId + ".");
        }

        private static void ClearLastGlobalPath(RegionPathFinder pFinder)
        {
            if (pFinder == null) return;
            pFinder.last_globalPath = null;
        }
    }
}
