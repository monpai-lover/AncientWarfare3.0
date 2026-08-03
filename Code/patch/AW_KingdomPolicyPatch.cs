using AncientWarfare3.core.policy;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_KingdomPolicyPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.updateAge))]
        public static void UpdateAge_Postfix(Kingdom __instance)
        {
            if (__instance?.data == null || __instance.isRekt() ||
                __instance.isNeutral()) return;

            try
            {
                DiplomaticWarDeclarationService.OnKingdomYear(__instance);
            }
            catch (System.Exception error)
            {
                ModClass.LogWarning("War declaration deadline failed: " +
                                    error.Message);
            }
            long benchmark = RecentFeatureBenchmark.Begin();
            try { KingdomAnnualWorkService.Schedule(__instance); }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.KingdomAnnualQueueIndex,
                    benchmark);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "makeOwnKingdom")]
        public static void MakeOwnKingdom_Prefix(City __instance,
            Actor pActor, bool pRebellion, bool pFellApart)
        {
            KingdomPolicyInheritanceService.RememberSplitSource(pActor,
                __instance?.kingdom, pRebellion, pFellApart);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "makeOwnKingdom")]
        public static void MakeOwnKingdom_Postfix(Kingdom __result,
            Actor pActor)
        {
            KingdomPolicyInheritanceService.InheritForNewKingdom(__result,
                pActor);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.makeNewCivKingdom))]
        public static void MakeNewCivKingdom_Postfix(Kingdom __result)
        {
            if (__result?.data == null || __result.isRekt()) return;
            KingdomPolicyService.EnsureInitialized(__result);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.getMaxCities))]
        public static void GetMaxCities_Postfix(Kingdom __instance,
            ref int __result)
        {
            if (__instance?.data == null || __instance.isRekt()) return;
            __result += KingdomTitleService.GetCitiesBonus(
                KingdomTitleService.GetTitle(__instance));
        }
    }
}
