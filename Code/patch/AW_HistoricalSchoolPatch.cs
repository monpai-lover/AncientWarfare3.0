using AncientWarfare3.core.schools;
using HarmonyLib;
using ai.behaviours;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_HistoricalSchoolPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.updateAge))]
        private static void KingdomUpdateAge_Postfix()
        {
            HistoricalSchoolRuntime.OnWorldYear();
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), "die",
            new[] { typeof(bool), typeof(AttackType), typeof(bool), typeof(bool) })]
        private static void ActorDie_Prefix(Actor __instance)
        {
            SchoolMembershipService.OnDeath(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), nameof(Actor.joinCity))]
        private static bool ActorJoinCity_Prefix(Actor __instance, City pCity)
        {
            return HistoricalAffiliationService.CanJoinCity(__instance, pCity);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), nameof(Actor.joinKingdom))]
        private static bool ActorJoinKingdom_Prefix(Actor __instance, Kingdom pKingdom)
        {
            return HistoricalAffiliationService.CanJoinKingdom(__instance, pKingdom);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), "setCity", new[] { typeof(City) })]
        private static bool ActorSetCity_Prefix(Actor __instance, City pCity)
        {
            return HistoricalAffiliationService.CanJoinCity(__instance, pCity);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), "setKingdom", new[] { typeof(Kingdom) })]
        private static bool ActorSetKingdom_Prefix(Actor __instance, Kingdom pKingdomToSet)
        {
            return HistoricalAffiliationService.CanJoinKingdom(__instance, pKingdomToSet);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BehGoToTileTarget), nameof(BehGoToTileTarget.execute))]
        private static void GoToTileTarget_Postfix(Actor pActor, BehResult __result)
        {
            if (__result == BehResult.Stop)
                HistoricalSchoolTravelService.ReportImmediatePathFailure(pActor);
        }
    }
}
