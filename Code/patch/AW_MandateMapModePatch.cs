using System.Collections.Generic;
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_MandateMapModePatch
    {
        private static readonly HashSet<string> AvailableMarkerPaths = new HashSet<string>();
        private static readonly HashSet<string> MissingMarkerPaths = new HashSet<string>();

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Zones), nameof(Zones.getMapMetaAsset))]
        public static void ZonesGetMapMetaAsset_Postfix(ref MetaTypeAsset __result)
        {
            if (FeudatoryMapModeService.IsActive())
            {
                __result = AWMapModeMetaLibrary.FeudatoryAsset ?? __result;
                return;
            }
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
            if (!FeudatoryMapModeService.IsActive() &&
                !MandateDynastyMapModeService.IsActive() &&
                !MandateCoreMapModeService.IsActive()) return;
            __result = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PowerButton), "clickSpecial")]
        public static void PowerButtonClickSpecial_Postfix(PowerButton __instance)
        {
            if (__instance == null) return;
            if (__instance.name == MandateDynastyMapModeService.POWER_ID) MandateDynastyMapModeService.DirtyMap();
            if (__instance.name == MandateCoreMapModeService.POWER_ID) MandateCoreMapModeService.DirtyMap();
            if (__instance.name == FeudatoryMapModeService.POWER_ID)
                FeudatoryMapModeService.DirtyMap();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(NameplateText), "showSpecies", new[] { typeof(string) })]
        public static void NameplateTextShowSpecies_Prefix(NameplateText __instance, ref string pPath)
        {
            if (__instance == null || __instance.is_mini ||
                !(__instance.nano_object is Kingdom kingdom)) return;

            string markerPath = MandateMapMarkerService.GetMarkerIcon(kingdom);
            if (!string.IsNullOrEmpty(markerPath) && IsMarkerAvailable(markerPath))
                pPath = markerPath;
        }

        private static bool IsMarkerAvailable(string pPath)
        {
            if (AvailableMarkerPaths.Contains(pPath)) return true;
            if (MissingMarkerPaths.Contains(pPath)) return false;

            if (SpriteTextureLoader.getSprite(pPath) != null)
            {
                AvailableMarkerPaths.Add(pPath);
                return true;
            }

            MissingMarkerPaths.Add(pPath);
            return false;
        }
    }
}
