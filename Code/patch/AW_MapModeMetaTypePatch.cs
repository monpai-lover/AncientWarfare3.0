using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_MapModeMetaTypePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(MetaTypeExtensions), nameof(MetaTypeExtensions.AsString))]
        public static bool AsString_Prefix(MetaType pType, ref string __result)
        {
            if (!AWMapModeMetaTypes.TryAsString(pType, out string result)) return true;
            __result = result;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(MetaTypeAsset), nameof(MetaTypeAsset.selectAndInspect))]
        public static bool SelectAndInspect_Prefix(MetaTypeAsset __instance, NanoObject pNewNanoObject)
        {
            if (pNewNanoObject is not City city ||
                __instance != AWMapModeMetaLibrary.SchoolAsset &&
                __instance != AWMapModeMetaLibrary.ShiLineageAsset &&
                __instance != MetaTypeLibrary.city)
                return true;
            if (ShiLineageMapModeService.IsActive()) ShiLineageMapModeService.SelectCity(city);
            else if (SchoolMapModeService.IsActive()) SchoolMapModeService.SelectCity(city);
            else return true;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(MetaTypeAsset), nameof(MetaTypeAsset.getZoneOptionState))]
        public static bool GetZoneOptionState_Prefix(MetaTypeAsset __instance, ref int __result)
        {
            if (__instance?.option_asset != null) return true;
            if (!AWMapModeMetaTypes.IsAwMetaId(__instance?.id)) return true;
            __result = 0;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Zones), nameof(Zones.getCurrentMapBorderMode))]
        public static bool GetCurrentMapBorderMode_Prefix(bool pCheckOnlyOption, ref MetaType __result)
        {
            if (TryGetActiveAwMapMode(pCheckOnlyOption, ref __result)) return false;

            if (Zones.showCultureZones(pCheckOnlyOption))
            {
                __result = MetaType.Culture;
                return false;
            }
            if (Zones.showKingdomZones(pCheckOnlyOption))
            {
                __result = MetaType.Kingdom;
                return false;
            }
            if (Zones.showClanZones(pCheckOnlyOption))
            {
                __result = MetaType.Clan;
                return false;
            }
            if (Zones.showAllianceZones(pCheckOnlyOption))
            {
                __result = MetaType.Alliance;
                return false;
            }
            if (Zones.showCityZones(pCheckOnlyOption))
            {
                __result = MetaType.City;
                return false;
            }
            if (Zones.showSpeciesZones(pCheckOnlyOption))
            {
                __result = MetaType.Subspecies;
                return false;
            }
            if (Zones.showFamiliesZones(pCheckOnlyOption))
            {
                __result = MetaType.Family;
                return false;
            }
            if (Zones.showLanguagesZones(pCheckOnlyOption))
            {
                __result = MetaType.Language;
                return false;
            }
            if (Zones.showReligionZones(pCheckOnlyOption))
            {
                __result = MetaType.Religion;
                return false;
            }
            if (Zones.showArmyZones(pCheckOnlyOption))
            {
                __result = MetaType.Army;
                return false;
            }

            __result = MetaType.None;
            return false;
        }

        private static bool TryGetActiveAwMapMode(bool pCheckOnlyOption, ref MetaType pResult)
        {
            if (IsActive(AWMapModeMetaLibrary.HierarchicalVassalAsset,
                    pCheckOnlyOption))
            {
                pResult = AWMapModeMetaTypes.HierarchicalVassal;
                return true;
            }
            if (IsActive(AWMapModeMetaLibrary.ShiLineageAsset,
                    pCheckOnlyOption))
            {
                pResult = AWMapModeMetaTypes.ShiLineage;
                return true;
            }
            if (IsActive(AWMapModeMetaLibrary.FeudatoryAsset,
                    pCheckOnlyOption))
            {
                pResult = AWMapModeMetaTypes.Feudatory;
                return true;
            }
            if (IsActive(AWMapModeMetaLibrary.SchoolAsset, pCheckOnlyOption))
            {
                pResult = AWMapModeMetaTypes.School;
                return true;
            }
            if (IsActive(AWMapModeMetaLibrary.MandateCoreAsset, pCheckOnlyOption))
            {
                pResult = AWMapModeMetaTypes.MandateCore;
                return true;
            }
            if (IsActive(AWMapModeMetaLibrary.MandateDynastyAsset, pCheckOnlyOption))
            {
                pResult = AWMapModeMetaTypes.MandateDynasty;
                return true;
            }
            if (IsActive(AWMapModeMetaLibrary.WarCoreAsset, pCheckOnlyOption))
            {
                pResult = AWMapModeMetaTypes.WarCore;
                return true;
            }
            if (IsActive(AWMapModeMetaLibrary.VassalAsset, pCheckOnlyOption))
            {
                pResult = AWMapModeMetaTypes.Vassal;
                return true;
            }
            if (IsActive(AWMapModeMetaLibrary.TechAsset, pCheckOnlyOption))
            {
                pResult = AWMapModeMetaTypes.Tech;
                return true;
            }
            return false;
        }

        private static bool IsActive(MetaTypeAsset pAsset, bool pCheckOnlyOption)
        {
            try { return pAsset != null && pAsset.isActive(pCheckOnlyOption); }
            catch { return false; }
        }
    }
}
