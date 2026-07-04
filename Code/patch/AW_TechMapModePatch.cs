using AncientWarfare3.core.policy;
using HarmonyLib;
using System;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_TechMapModePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), "Start")]
        public static void MapBoxStart_Postfix()
        {
            TechMapModeService.EnsureLayer();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Zones), nameof(Zones.getMapMetaAsset))]
        public static void ZonesGetMapMetaAsset_Postfix(ref MetaTypeAsset __result)
        {
            if (!TechMapModeService.IsActive()) return;
            __result = MetaType.Kingdom.getAsset();
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

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZoneCalculator), nameof(ZoneCalculator.drawZoneMeta),
            new[] { typeof(TileZone), typeof(MetaTypeAsset), typeof(MetaZoneGetMetaSimple) })]
        public static void ZoneCalculatorDrawZoneMeta_Prefix(MetaTypeAsset pMetaTypeAsset, ref bool __state)
        {
            __state = false;
            if (!TechMapModeService.IsActive()) return;
            if (pMetaTypeAsset?.map_mode != MetaType.Kingdom) return;
            TechMapModeService.BeginZoneColorOverride();
            __state = true;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(ZoneCalculator), nameof(ZoneCalculator.drawZoneMeta),
            new[] { typeof(TileZone), typeof(MetaTypeAsset), typeof(MetaZoneGetMetaSimple) })]
        public static Exception ZoneCalculatorDrawZoneMeta_Finalizer(bool __state, Exception __exception)
        {
            if (__state) TechMapModeService.EndZoneColorOverride();
            return __exception;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.getColor))]
        public static void KingdomGetColor_Postfix(Kingdom __instance, ref ColorAsset __result)
        {
            if (!TechMapModeService.ShouldOverrideKingdomZoneColor(__instance)) return;
            __result = TechMapModeService.GetColor(__instance, __result);
        }

    }
}
