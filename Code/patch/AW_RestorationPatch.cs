using System;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_RestorationPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapStats), nameof(MapStats.getNextId))]
        private static bool GetNextId_Prefix(string pType, ref long __result)
        {
            if (!KingdomIdentityContinuityService.TryConsumeKingdomId(pType, out long id)) return true;
            __result = id;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), "updateObjectAge")]
        private static void WorldUpdateAge_Prefix()
        {
            if (!Config.game_loaded || SmoothLoader.isLoading()) return;
            AutonomousRestorationService.OnWorldYear();
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void MapBoxClearWorld_Prefix()
        {
            if (!AWAsyncClearWorldGuard.CleanupAllowed) return;
            AutonomousRestorationService.ClearRuntime();
            RoyalClaimService.ClearRuntime();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "setKingdom", new[] { typeof(Kingdom) })]
        private static void ActorSetKingdom_Postfix(Actor __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            RoyalClaimService.OnActorKingdomChanged(__instance);
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Actor), "die",
            new[] { typeof(bool), typeof(AttackType), typeof(bool), typeof(bool) })]
        private static Exception ActorDie_Finalizer(Actor __instance,
            bool __state, Exception __exception)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return __exception;
            if (!ActorDeathInvocationRules.ShouldProcess(__state,
                    __instance?.isAlive() ?? false)) return __exception;
            long diagnostic = RuntimePerformanceDiagnostic.BeginDeathStage(
                ActorDeathPerformanceStage.RoyalClaim);
            try
            {
                if (__instance?.data != null && !__instance.isAlive())
                    RoyalClaimService.OnActorDied(__instance);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Royal claim death cleanup failed: " + e.Message);
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDeathStage(
                    ActorDeathPerformanceStage.RoyalClaim, diagnostic);
            }
            return __exception;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "die",
            new[] { typeof(bool), typeof(AttackType), typeof(bool), typeof(bool) })]
        private static void ActorDie_Prefix(Actor __instance, out bool __state)
        {
            __state = __instance?.isAlive() ?? false;
        }
    }
}
