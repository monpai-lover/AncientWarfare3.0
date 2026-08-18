using System.Collections.Generic;
using HarmonyLib;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_SimObjectsZonesPatch
    {
        private static int _fallbackFaults;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), "recalc")]
        private static bool Recalculate_Prefix(
            ref bool ____buildings_dirty,
            HashSet<MapChunk> ____dirty_building_chunks,
            List<WorldTile> ____to_clear_tiles)
        {
            try
            {
                bool handled = AWIncrementalSimObjectZoneUnits.TryRecalculate(
                    ____buildings_dirty, ____dirty_building_chunks,
                    ____to_clear_tiles);
                if (handled) ____buildings_dirty = false;
                return !handled;
            }
            catch (System.Exception error)
            {
                FallBackToVanilla(error, "recalc");
                return true;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), "clearTileUnits")]
        private static bool ClearTileUnits_Prefix(List<WorldTile> ____to_clear_tiles)
        {
            try
            {
                return !AWParallelSimObjectZoneUnits.TryClearTileUnits(
                    ____to_clear_tiles);
            }
            catch (System.Exception error)
            {
                FallBackToVanilla(error, "clear_tile_units");
                return true;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), "clearChunkObjects")]
        private static bool ClearChunkObjects_Prefix(bool pForceClearBuildings)
        {
            try
            {
                return !AWParallelSimObjectZoneUnits.TryClearChunkObjects(
                    pForceClearBuildings);
            }
            catch (System.Exception error)
            {
                FallBackToVanilla(error, "clear_chunk_objects");
                return true;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(IslandsCalculator), "recalcActors")]
        private static bool RecalculateIslands_Prefix(IslandsCalculator __instance)
        {
            try
            {
                return !AWParallelSimObjectZoneUnits.TryDeferIslandRebuild(
                    __instance);
            }
            catch (System.Exception error)
            {
                FallBackToVanilla(error, "recalc_islands");
                return true;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), "checkUnits")]
        private static bool CheckUnits_Prefix(List<WorldTile> ____to_clear_tiles,
            out bool __state)
        {
            try
            {
                if (AWParallelSimObjectZoneUnits
                        .TrySkipRedundantCheckUnits() &&
                    !AWActorZoneMembershipDirtyIndex.HasPending())
                {
                    __state = true;
                    return false;
                }

                __state = AWParallelSimObjectZoneUnits.TryRebuild(
                    ____to_clear_tiles);
                return !__state;
            }
            catch (System.Exception error)
            {
                FallBackToVanilla(error, "check_units");
                __state = false;
                return true;
            }
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

        private static void FallBackToVanilla(System.Exception pError,
            string pStage)
        {
            AWIncrementalSimObjectZoneUnits.Invalidate();
            AWParallelSimObjectZoneUnits.Invalidate();
            int failures = System.Threading.Interlocked.Increment(
                ref _fallbackFaults);
            if (failures == 1 || failures % 100 == 0)
            {
                ModClass.LogWarning(
                    "AW spatial optimization fell back to vanilla at " +
                    pStage + " (count=" + failures + "): " + pError);
            }
        }
    }
}
