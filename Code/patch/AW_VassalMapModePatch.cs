using System;
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_VassalMapModePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), "Start")]
        public static void MapBoxStart_Postfix()
        {
            VassalMapModeService.HideLegacyLayer();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Zones), nameof(Zones.getMapMetaAsset))]
        public static void ZonesGetMapMetaAsset_Postfix(ref MetaTypeAsset __result)
        {
            if (!VassalMapModeService.IsActive()) return;
            __result = MetaType.Kingdom.getAsset();
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

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZoneCalculator), nameof(ZoneCalculator.drawZoneMeta),
            new[] { typeof(TileZone), typeof(MetaTypeAsset), typeof(MetaZoneGetMetaSimple) })]
        public static void ZoneCalculatorDrawZoneMeta_Prefix(MetaTypeAsset pMetaTypeAsset,
            ref MetaZoneGetMetaSimple pZoneGetDelegate, ref bool __state)
        {
            __state = false;
            if (!VassalMapModeService.IsActive()) return;
            if (pMetaTypeAsset?.map_mode != MetaType.Kingdom) return;
            pZoneGetDelegate = VassalMapModeService.GetRootMetaForZone;
            VassalMapModeService.BeginZoneColorOverride();
            __state = true;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(ZoneCalculator), nameof(ZoneCalculator.drawZoneMeta),
            new[] { typeof(TileZone), typeof(MetaTypeAsset), typeof(MetaZoneGetMetaSimple) })]
        public static Exception ZoneCalculatorDrawZoneMeta_Finalizer(bool __state, Exception __exception)
        {
            if (__state) VassalMapModeService.EndZoneColorOverride();
            return __exception;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.getColor))]
        public static void KingdomGetColor_Postfix(Kingdom __instance, ref ColorAsset __result)
        {
            if (!VassalMapModeService.ShouldOverrideKingdomZoneColor(__instance)) return;
            __result = VassalMapModeService.GetColor(__instance, __result);
        }

    }
}
