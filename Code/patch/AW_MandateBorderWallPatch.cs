using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_MandateBorderWallPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "setKingdom")]
        private static void CitySetKingdom_Prefix(City __instance,
            out Kingdom __state)
        {
            __state = __instance?.kingdom;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "setKingdom")]
        private static void CitySetKingdom_Postfix(City __instance,
            Kingdom pKingdom, Kingdom __state)
        {
            MandateBorderWallRefreshService.ObserveCityOwnershipChange(
                __instance, __state, __instance?.kingdom ?? pKingdom);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(TileZone), "setCity")]
        private static void ZoneSetCity_Prefix(TileZone __instance,
            out City __state)
        {
            __state = __instance?.city;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TileZone), "setCity")]
        private static void ZoneSetCity_Postfix(TileZone __instance,
            City pCity, City __state)
        {
            MandateBorderWallRefreshService.ObserveZoneOwnershipChange(
                __instance, __state, pCity);
        }
    }
}
