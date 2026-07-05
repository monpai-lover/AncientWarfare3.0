using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_TechMapModePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Zones), nameof(Zones.getMapMetaAsset))]
        public static void ZonesGetMapMetaAsset_Postfix(ref MetaTypeAsset __result)
        {
            if (!TechMapModeService.IsActive()) return;
            __result = AWMapModeMetaLibrary.TechAsset ?? __result;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Zones), nameof(Zones.showMapBorders))]
        public static void ZonesShowMapBorders_Postfix(ref bool __result)
        {
            if (!TechMapModeService.IsActive()) return;
            __result = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PowerButton), "clickSpecial")]
        public static void PowerButtonClickSpecial_Postfix(PowerButton __instance)
        {
            if (__instance == null || __instance.name != TechMapModeService.POWER_ID) return;
            TechMapModeService.DirtyMap();
        }

    }
}
