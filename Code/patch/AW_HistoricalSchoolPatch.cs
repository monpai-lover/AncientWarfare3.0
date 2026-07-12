using AncientWarfare3.core.schools;
using HarmonyLib;

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
            HistoricalSchoolDescentService.OnDeath(__instance);
        }
    }
}
