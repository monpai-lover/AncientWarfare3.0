using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_WarMapModePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), "Start")]
        public static void MapBoxStart_Postfix()
        {
            WarCoreMapModeService.EnsureLayer();
            WarClaimMapModeService.EnsureLayer();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Zones), nameof(Zones.getMapMetaAsset))]
        public static void ZonesGetMapMetaAsset_Postfix(ref MetaTypeAsset __result)
        {
            if (!WarCoreMapModeService.IsActive() && !WarClaimMapModeService.IsActive()) return;
            __result = MetaType.Kingdom.getAsset();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Zones), nameof(Zones.showMapBorders))]
        public static void ZonesShowMapBorders_Postfix(ref bool __result)
        {
            if (!WarCoreMapModeService.IsActive() && !WarClaimMapModeService.IsActive()) return;
            __result = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PowerButton), "clickSpecial")]
        public static void PowerButtonClickSpecial_Postfix(PowerButton __instance)
        {
            if (__instance == null) return;
            if (__instance.name == WarCoreMapModeService.POWER_ID) WarCoreMapModeService.DirtyMap();
            if (__instance.name == WarClaimMapModeService.POWER_ID) WarClaimMapModeService.DirtyMap();
        }
    }
}
