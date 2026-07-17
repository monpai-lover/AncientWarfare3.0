using System;
using AncientWarfare3.core.lineage;
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

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(MapBox), "updateObjectAge")]
        private static void WorldUpdateAge_Postfix()
        {
            if (!Config.game_loaded || SmoothLoader.isLoading()) return;
            AutonomousRestorationService.OnWorldYear();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void MapBoxClearWorld_Prefix()
        {
            AutonomousRestorationService.ClearRuntime();
            RoyalClaimService.ClearRuntime();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "setKingdom", new[] { typeof(Kingdom) })]
        private static void ActorSetKingdom_Postfix(Actor __instance)
        {
            RoyalClaimService.OnActorKingdomChanged(__instance);
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Actor), "die",
            new[] { typeof(bool), typeof(AttackType), typeof(bool), typeof(bool) })]
        private static Exception ActorDie_Finalizer(Actor __instance,
            Exception __exception)
        {
            try
            {
                if (__instance?.data != null && !__instance.isAlive())
                    RoyalClaimService.OnActorDied(__instance);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Royal claim death cleanup failed: " + e.Message);
            }
            return __exception;
        }
    }
}
