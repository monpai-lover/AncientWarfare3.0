using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_EnclosedUnownedZonePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(TileZone), "setCity")]
        private static void SetCity_Prefix(TileZone __instance,
            out City __state)
        {
            __state = __instance?.city;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TileZone), "setCity")]
        private static void SetCity_Postfix(TileZone __instance, City pCity,
            City __state)
        {
            EnclosedUnownedZoneRepairService.
                ObserveOwnershipChange(__instance, __state, pCity);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "setKingdom")]
        private static void CitySetKingdom_Postfix(City __instance)
        {
            EnclosedUnownedZoneRepairService.
                ObserveCityKingdomChange(__instance);
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
