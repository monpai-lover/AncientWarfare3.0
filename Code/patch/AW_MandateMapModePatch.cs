using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_MandateMapModePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Zones), nameof(Zones.getMapMetaAsset))]
        public static void ZonesGetMapMetaAsset_Postfix(ref MetaTypeAsset __result)
        {
            if (MandateDynastyMapModeService.IsActive())
            {
                __result = AWMapModeMetaLibrary.MandateDynastyAsset ?? __result;
                return;
            }
            if (MandateCoreMapModeService.IsActive())
                __result = AWMapModeMetaLibrary.MandateCoreAsset ?? __result;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Zones), nameof(Zones.showMapBorders))]
        public static void ZonesShowMapBorders_Postfix(ref bool __result)
        {
            if (!MandateDynastyMapModeService.IsActive() && !MandateCoreMapModeService.IsActive()) return;
            __result = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PowerButton), "clickSpecial")]
        public static void PowerButtonClickSpecial_Postfix(PowerButton __instance)
        {
            if (__instance == null) return;
            if (__instance.name == MandateDynastyMapModeService.POWER_ID) MandateDynastyMapModeService.DirtyMap();
            if (__instance.name == MandateCoreMapModeService.POWER_ID) MandateCoreMapModeService.DirtyMap();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NameplateText), "showTextKingdom")]
        public static void NameplateTextKingdom_Postfix(NameplateText __instance, Kingdom pMetaObject)
        {
            MandateMapMarkerService.ApplyNameplate(__instance, pMetaObject);
        }
    }
}
