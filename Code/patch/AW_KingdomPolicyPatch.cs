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
            if (__instance?.data == null || __instance.isRekt() || __instance.isNeutral()) return;

            XiaizationService.OnKingdomYear(__instance);
            KingdomPolicyService.OnKingdomYear(__instance);
            CityTechService.OnKingdomYear(__instance);
            CityEconomyService.OnKingdomYear(__instance);
            WarTerritoryService.OnKingdomYear(__instance);
            MandateService.OnKingdomYear(__instance);
            MandateDecisionService.OnKingdomYear(__instance);
            MandateRebelService.OnKingdomYear(__instance);
            ForeignOccupationService.OnKingdomYear(__instance);

            int year = Date.getCurrentYear();
            long id = __instance.id;
            if (KingdomYearSchedulerRules.ShouldRunHeavySystem(year, id, pModulo: 2, pSlot: 0))
            {
                WarPlotRedirectService.OnKingdomYear(__instance);
                WarDecisionAI.OnKingdomYear(__instance);
                VassalAIService.OnKingdomYear(__instance);
            }

            if (KingdomYearSchedulerRules.ShouldRunHeavySystem(year, id, pModulo: 4, pSlot: 2))
            {
                GeneralService.OnKingdomYear(__instance);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "makeOwnKingdom")]
        public static void MakeOwnKingdom_Prefix(City __instance, Actor pActor)
        {
            KingdomPolicyInheritanceService.RememberSplitSource(pActor, __instance?.kingdom);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "makeOwnKingdom")]
        public static void MakeOwnKingdom_Postfix(Kingdom __result, Actor pActor)
        {
            KingdomPolicyInheritanceService.InheritForNewKingdom(__result, pActor);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.makeNewCivKingdom))]
        public static void MakeNewCivKingdom_PolicyPostfix(Kingdom __result, Actor pActor)
        {
            KingdomPolicyInheritanceService.InheritForNewKingdom(__result, pActor);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.getMaxCities))]
        public static void GetMaxCities_Postfix(Kingdom __instance, ref int __result)
        {
            if (__instance?.data == null || __instance.isRekt()) return;
            __result += KingdomTitleService.GetCitiesBonus(KingdomTitleService.GetTitle(__instance));
        }
    }
}
