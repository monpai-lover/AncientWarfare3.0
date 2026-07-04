using System;
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_MandateMapModePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), "Start")]
        public static void MapBoxStart_Postfix()
        {
            MandateDynastyMapModeService.EnsureLayer();
            MandateCoreMapModeService.EnsureLayer();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Zones), nameof(Zones.getMapMetaAsset))]
        public static void ZonesGetMapMetaAsset_Postfix(ref MetaTypeAsset __result)
        {
            if (!MandateDynastyMapModeService.IsActive() && !MandateCoreMapModeService.IsActive()) return;
            __result = MetaType.Kingdom.getAsset();
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

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZoneCalculator), nameof(ZoneCalculator.drawZoneMeta),
            new[] { typeof(TileZone), typeof(MetaTypeAsset), typeof(MetaZoneGetMetaSimple) })]
        public static void ZoneCalculatorDrawZoneMeta_Prefix(MetaTypeAsset pMetaTypeAsset,
            ref MetaZoneGetMetaSimple pZoneGetDelegate, ref int __state)
        {
            __state = 0;
            if (pMetaTypeAsset?.map_mode != MetaType.Kingdom) return;
            if (MandateDynastyMapModeService.IsActive())
            {
                pZoneGetDelegate = MandateDynastyMapModeService.GetDynastyMetaForZone;
                MandateDynastyMapModeService.BeginZoneColorOverride();
                __state = 1;
                return;
            }
            if (MandateCoreMapModeService.IsActive())
            {
                MandateCoreMapModeService.BeginZoneColorOverride();
                __state = 2;
            }
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(ZoneCalculator), nameof(ZoneCalculator.drawZoneMeta),
            new[] { typeof(TileZone), typeof(MetaTypeAsset), typeof(MetaZoneGetMetaSimple) })]
        public static Exception ZoneCalculatorDrawZoneMeta_Finalizer(int __state, Exception __exception)
        {
            if (__state == 1) MandateDynastyMapModeService.EndZoneColorOverride();
            if (__state == 2) MandateCoreMapModeService.EndZoneColorOverride();
            return __exception;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.getColor))]
        public static void KingdomGetColor_Postfix(Kingdom __instance, ref ColorAsset __result)
        {
            if (MandateDynastyMapModeService.ShouldOverrideKingdomZoneColor(__instance))
            {
                __result = MandateDynastyMapModeService.GetColor(__instance, __result);
                return;
            }
            if (MandateCoreMapModeService.ShouldOverrideKingdomZoneColor(__instance))
                __result = MandateCoreMapModeService.GetColor(__instance, __result);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NameplateText), "showTextKingdom")]
        public static void NameplateTextKingdom_Postfix(NameplateText __instance, Kingdom pMetaObject)
        {
            MandateMapMarkerService.ApplyNameplate(__instance, pMetaObject);
        }
    }
}
