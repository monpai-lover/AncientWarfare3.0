using System;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;
using HarmonyLib;
using ai.behaviours;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_HistoricalSchoolPatch
    {
        private readonly struct ActorSetCityState
        {
            public ActorSetCityState(bool pOriginalAllowed, bool pHasActiveMembership,
                City pOldCity)
            {
                OriginalAllowed = pOriginalAllowed;
                HasActiveMembership = pHasActiveMembership;
                OldCity = pOldCity;
            }

            public bool OriginalAllowed { get; }
            public bool HasActiveMembership { get; }
            public City OldCity { get; }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.updateAge))]
        private static void KingdomUpdateAge_Postfix()
        {
            HistoricalSchoolRuntime.EnqueueWorldYear();
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
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.newCityEvent))]
        private static void CityNewCityEvent_Postfix(City __instance)
        {
            InvalidateSchoolCityCaches(__instance);
            HistoricalSchoolRuntime.RefreshLivingXiaCity(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "setKingdom")]
        private static void CitySetKingdom_Postfix(
            City __instance,
            bool pFromLoad)
        {
            InvalidateSchoolCityCaches(__instance);
            if (!pFromLoad) HistoricalSchoolRuntime.RefreshLivingXiaCity(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.destroyCity))]
        private static void CityDestroyCity_Prefix(City __instance)
        {
            long cityId = __instance?.data?.id ?? -1L;
            HistoricalSchoolVenueService.InvalidateCity(cityId);
            HistoricalSchoolRecruitCandidateCache.InvalidateCity(cityId);
            HistoricalSchoolRuntimeIndex.Instance.SetLivingXiaCity(cityId, false);
        }

        private static void InvalidateSchoolCityCaches(City pCity)
        {
            long cityId = pCity?.data?.id ?? -1L;
            HistoricalSchoolVenueService.InvalidateCity(cityId);
            HistoricalSchoolRecruitCandidateCache.InvalidateCity(cityId);
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
        private static bool ActorSetCity_Prefix(Actor __instance, City pCity,
            out ActorSetCityState __state)
        {
            bool allowed = HistoricalAffiliationService.CanJoinCity(__instance, pCity);
            City oldCity = __instance?.city;
            bool activeMember = false;
            if (allowed && oldCity != pCity && __instance?.data != null)
                activeMember = SchoolMembershipService.GetActive(__instance.data.id) != null;
            __state = new ActorSetCityState(allowed, activeMember, oldCity);
            return allowed;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "setCity", new[] { typeof(City) })]
        private static void ActorSetCity_Postfix(Actor __instance, ActorSetCityState __state)
        {
            long oldCityId = __state.OldCity?.data?.id ?? -1L;
            long newCityId = __instance?.city?.data?.id ?? -1L;
            if (!SchoolResidenceInvalidationRules.ShouldInvalidateActiveMemberMove(
                    __state.OriginalAllowed, __state.HasActiveMembership, oldCityId,
                    newCityId)) return;
            HistoricalAffiliationService.NotifyActiveMemberCityChanged(__state.OldCity,
                __instance.city);
            HistoricalSchoolActivityQueue.CancelActor(__instance, pRestoreActor: true);
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
