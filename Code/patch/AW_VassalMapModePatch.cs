using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_VassalMapModePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Zones), nameof(Zones.getMapMetaAsset))]
        public static void ZonesGetMapMetaAsset_Postfix(ref MetaTypeAsset __result)
        {
            if (!VassalMapModeService.IsActive()) return;
            __result = AWMapModeMetaLibrary.VassalAsset ?? __result;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Zones), nameof(Zones.showMapBorders))]
        public static void ZonesShowMapBorders_Postfix(ref bool __result)
        {
            if (!VassalMapModeService.IsActive()) return;
            __result = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PowerButton), "clickSpecial")]
        public static void PowerButtonClickSpecial_Postfix(PowerButton __instance)
        {
            if (__instance == null || __instance.name != VassalMapModeService.POWER_ID) return;
            VassalMapModeService.DirtyMap();
        }

    }
}
