using System;
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

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Actor), "die",
            new[] { typeof(bool), typeof(AttackType), typeof(bool), typeof(bool) })]
        private static Exception ActorDie_Finalizer(Actor __instance, bool pDestroy,
            Exception __exception)
        {
            try
            {
                if (__instance?.data != null && !__instance.isAlive())
                    SchoolMembershipService.OnDeath(__instance, pDestroy);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("School death finalizer failed: " + error.Message);
            }
            return __exception;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), "Update")]
        private static void MapBoxUpdate_Prefix()
        {
            if (!Config.game_loaded || SmoothLoader.isLoading()) return;
            SchoolMembershipService.ProcessDeathRetries();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ActorManager), "destroyObject")]
        private static bool ActorManagerDestroyObject_Prefix(Actor pActor)
        {
            if (HistoricalSchoolDescentService.ShouldDeferDestroy(pActor)) return false;
            return !SchoolMembershipService.ShouldDeferDestroy(pActor);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void MapBoxClearWorld_Prefix()
        {
            HistoricalSchoolRuntime.ClearRuntime();
            SchoolMembershipService.ClearRuntime();
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
