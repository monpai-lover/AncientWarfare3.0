using System.Collections.Generic;
using AncientWarfare3.core.performance;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    // Routes the vanilla SimObjectsZones lifecycle through the already
    // validated incremental/chunk implementations. Every prefix falls back
    // to vanilla when the incremental path cannot prove its world state.
    [HarmonyPatch]
    internal static class AW_SimObjectsZonesPerformancePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), "recalc")]
        private static bool RecalculatePrefix(
            ref bool ____buildings_dirty,
            HashSet<MapChunk> ____dirty_building_chunks,
            List<WorldTile> ____to_clear_tiles)
        {
            bool handled = AWIncrementalSimObjectZoneUnits.TryRecalculate(
                ____buildings_dirty,
                ____dirty_building_chunks,
                ____to_clear_tiles);
            if (handled)
                ____buildings_dirty = false;
            return !handled;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), "clearTileUnits")]
        private static bool ClearTileUnitsPrefix(
            List<WorldTile> ____to_clear_tiles)
        {
            return !AWParallelSimObjectZoneUnits.TryClearTileUnits(
                ____to_clear_tiles);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), "clearChunkObjects")]
        private static bool ClearChunkObjectsPrefix(bool pForceClearBuildings)
        {
            return !AWParallelSimObjectZoneUnits.TryClearChunkObjects(
                pForceClearBuildings);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(IslandsCalculator), "recalcActors")]
        private static bool RecalculateIslandsPrefix(
            IslandsCalculator __instance)
        {
            return !AWParallelSimObjectZoneUnits.TryDeferIslandRebuild(
                __instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), "checkUnits")]
        private static bool CheckUnitsPrefix(
            List<WorldTile> ____to_clear_tiles)
        {
            return !AWParallelSimObjectZoneUnits.TryRebuild(
                ____to_clear_tiles);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SimObjectsZones), "checkUnits")]
        private static void CheckUnitsPostfix()
        {
            AWParallelSimObjectZoneUnits.NotifyUnitMembershipRebuilt();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), nameof(SimObjectsZones.fullClear))]
        private static void FullClearPrefix()
        {
            AWIncrementalSimObjectZoneUnits.Invalidate();
        }
    }
}
