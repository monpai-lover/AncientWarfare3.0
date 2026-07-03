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
            KingdomPolicyService.OnKingdomYear(__instance);
            VassalAIService.OnKingdomYear(__instance);
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
