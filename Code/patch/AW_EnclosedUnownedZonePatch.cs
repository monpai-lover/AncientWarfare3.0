using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_EnclosedUnownedZonePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TileZone), "setCity")]
        private static void SetCity_Postfix(TileZone __instance)
        {
            EnclosedUnownedZoneRepairService.
                ObserveOwnershipChange(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.addLoadWorldCallbacks))]
        private static void RegisterWorldLoaded_Postfix()
        {
            MapBox.on_world_loaded -= OnWorldLoaded;
            MapBox.on_world_loaded += OnWorldLoaded;
        }

        private static void OnWorldLoaded()
        {
            MapBox.on_world_loaded -= OnWorldLoaded;
            EnclosedUnownedZoneRepairService.BeginInitialSweep();
        }
    }
}
