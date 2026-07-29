using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.api.multiplayer;
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
            if (AW3MultiplayerReplicaScope.IsApplying) return true;
            return RoyalGuardService.CanAssignArmy(__instance, pObject);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static bool SetKing_Prefix(Kingdom __instance, Actor pActor,
            bool pFromLoad)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return true;
            if (HeirService.IsCurrentHeir(__instance, pActor) &&
                !RoyalGuardService.ReleaseForRegisteredHeir(__instance,
                    pActor, "became_king"))
                return false;
            return RoyalGuardOfficeRules.CanAcceptNewKingship(
                RoyalGuardService.IsRoyalGuard(pActor), pFromLoad);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance, Actor pActor, bool pFromLoad)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (!SetKingPostfixRules.ShouldRun(pFromLoad, pActor != null && __instance?.king == pActor)) return;
            RoyalGuardService.OnKingChanged(__instance, pActor);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(City), nameof(City.setLeader))]
        public static bool SetLeader_Prefix(Actor pActor, bool pNew)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return true;
            return RoyalGuardOfficeRules.CanAcceptNewCityLeadership(
                RoyalGuardService.IsRoyalGuard(pActor), pNew);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "die")]
        public static void Die_Prefix(Actor __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (__instance?.data == null || !__instance.isAlive()) return;
            long diagnostic = RuntimePerformanceDiagnostic.BeginDeathStage(
                ActorDeathPerformanceStage.RoyalGuard);
            try
            {
                RoyalGuardService.OnGuardDeath(__instance);
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDeathStage(
                    ActorDeathPerformanceStage.RoyalGuard, diagnostic);
            }
        }
    }
}
