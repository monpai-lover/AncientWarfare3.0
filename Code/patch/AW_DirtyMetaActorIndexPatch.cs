using AncientWarfare3.core.performance;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    // 类级 [HarmonyPatch] 必需:PatchClassProcessor 在类上没有这个特性时
    // 直接返回,方法级特性一个都不会被处理(且不报错)。本类长期缺这一行,
    // 所以下面 12 个 prefix 从未执行过。
    //
    // ⚠ 目前本类仍在 ModClass.DormantPatchTypes 里被显式停用 —— 特性补齐只是
    //   为了不再依赖"缺特性"这种隐式关闭。要启用请从那张表里移除,并先实机
    //   验证亚种/家族/军队/宗教等归属是否正确(prefix 返回 true 会完全跳过原版)。
    [HarmonyPatch]
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
