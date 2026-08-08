using System.Collections.Generic;
using HarmonyLib;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_SimObjectsZonesPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), "recalc")]
        private static bool Recalculate_Prefix(
            ref bool ____buildings_dirty,
            HashSet<MapChunk> ____dirty_building_chunks,
            List<WorldTile> ____to_clear_tiles)
        {
            bool handled = AWIncrementalSimObjectZoneUnits.TryRecalculate(
                ____buildings_dirty, ____dirty_building_chunks,
                ____to_clear_tiles);
            if (handled) ____buildings_dirty = false;
            return !handled;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), "clearTileUnits")]
        private static bool ClearTileUnits_Prefix(List<WorldTile> ____to_clear_tiles)
        {
            return !AWParallelSimObjectZoneUnits.TryClearTileUnits(
                ____to_clear_tiles);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), "clearChunkObjects")]
        private static bool ClearChunkObjects_Prefix(bool pForceClearBuildings)
        {
            return !AWParallelSimObjectZoneUnits.TryClearChunkObjects(
                pForceClearBuildings);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(IslandsCalculator), "recalcActors")]
        private static bool RecalculateIslands_Prefix(IslandsCalculator __instance)
        {
            return !AWParallelSimObjectZoneUnits.TryDeferIslandRebuild(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), "checkUnits")]
        private static bool CheckUnits_Prefix(List<WorldTile> ____to_clear_tiles,
            out bool __state)
        {
            if (AWParallelSimObjectZoneUnits.TrySkipRedundantCheckUnits() &&
                !AWActorZoneMembershipDirtyIndex.HasPending())
            {
                __state = true;
                return false;
            }

            __state = AWParallelSimObjectZoneUnits.TryRebuild(____to_clear_tiles);
            return !__state;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SimObjectsZones), "checkUnits")]
        private static void CheckUnits_Postfix(bool __state)
        {
            // A handled full rebuild publishes its membership version before
            // returning. Native checkUnits must not be counted here.
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), "fullClear")]
        private static void FullClear_Prefix()
        {
            AWIncrementalSimObjectZoneUnits.Invalidate();
            AWParallelSimObjectZoneUnits.Invalidate();
        }
    }
}
