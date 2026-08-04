using AncientWarfare3.core.performance;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    internal static class AW_DirtyMetaActorIndexPatch
    {
        [HarmonyPrefix, HarmonyPatch(typeof(SubspeciesManager), "updateDirtyUnits")]
        private static bool UpdateSubspeciesUnits(
            SubspeciesManager __instance)
        {
            return !AWDirtyMetaActorIndex.TryApply(__instance);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(FamilyManager), "updateDirtyUnits")]
        private static bool UpdateFamilyUnits(FamilyManager __instance)
        {
            return !AWDirtyMetaActorIndex.TryApply(__instance);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ArmyManager), "updateDirtyUnits")]
        private static bool UpdateArmyUnits(ArmyManager __instance)
        {
            return !AWDirtyMetaActorIndex.TryApply(__instance);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ArmyManager),
            "updateDirtyUnits")]
        private static void UpdateArmyUnits_Postfix(
            ArmyManager __instance)
        {
            ArmyMembershipReconciliationService.EnqueueAll(__instance);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(LanguageManager), "updateDirtyUnits")]
        private static bool UpdateLanguageUnits(LanguageManager __instance)
        {
            return !AWDirtyMetaActorIndex.TryApply(__instance);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ReligionManager), "updateDirtyUnits")]
        private static bool UpdateReligionUnits(ReligionManager __instance)
        {
            return !AWDirtyMetaActorIndex.TryApply(__instance);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(CityManager), "updateDirtyUnits")]
        private static bool UpdateCityUnits(CityManager __instance)
        {
            return !AWDirtyMetaActorIndex.TryApply(__instance);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ClanManager), "updateDirtyUnits")]
        private static bool UpdateClanUnits(ClanManager __instance)
        {
            return !AWDirtyMetaActorIndex.TryApply(__instance);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(KingdomManager), "updateDirtyUnits")]
        private static bool UpdateKingdomUnits(KingdomManager __instance)
        {
            return !AWDirtyMetaActorIndex.TryApply(__instance);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(WildKingdomsManager), "updateDirtyUnits")]
        private static bool UpdateWildKingdomUnits(
            WildKingdomsManager __instance)
        {
            return !AWDirtyMetaActorIndex.TryApply(__instance);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(CultureManager), "updateDirtyUnits")]
        private static bool UpdateCultureUnits(CultureManager __instance)
        {
            return !AWDirtyMetaActorIndex.TryApply(__instance);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlotManager), "updateDirtyUnits")]
        private static bool UpdatePlotUnits(PlotManager __instance)
        {
            return !AWDirtyMetaActorIndex.TryApply(__instance);
        }
    }
}
