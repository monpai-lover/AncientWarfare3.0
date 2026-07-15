using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_RoyalGuardPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), nameof(Actor.setArmy))]
        public static bool SetArmy_Prefix(Actor __instance, Army pObject)
        {
            return RoyalGuardService.CanAssignArmy(__instance, pObject);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance, Actor pActor, bool pFromLoad)
        {
            if (!SetKingPostfixRules.ShouldRun(pFromLoad, pActor != null && __instance?.king == pActor)) return;
            if (RoyalGuardService.IsRoyalGuard(pActor))
                RoyalGuardService.DismissGuard(pActor, "became_king");
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
