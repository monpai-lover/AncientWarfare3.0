using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_RoyalGuardPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance, Actor pActor, bool pFromLoad)
        {
            if (pFromLoad) return;
            RoyalGuardService.OnKingChanged(__instance, pActor);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.setLeader))]
        public static void SetLeader_Postfix(Actor pActor, bool pNew)
        {
            if (!pNew) return;
            if (RoyalGuardService.IsRoyalGuard(pActor))
                RoyalGuardService.DismissGuard(pActor, "became_leader");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "die")]
        public static void Die_Prefix(Actor __instance)
        {
            RoyalGuardService.OnGuardDeath(__instance);
        }
    }
}
