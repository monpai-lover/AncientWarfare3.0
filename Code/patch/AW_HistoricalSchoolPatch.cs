using System;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.performance;
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

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Building), nameof(Building.startDestroyBuilding))]
        private static void AcademyStartDestroy_Prefix(Building __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            try
            {
                HistoricalSchoolAcademyLifecycleService.Capture(__instance);
                HistoricalSchoolAcademyConstructionService.RequestRebuild(__instance);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Academy destruction capture failed: " +
                                    error.Message);
            }
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Building), nameof(Building.startMakingRuins))]
        private static void AcademyStartRuin_Prefix(Building __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            try
            {
                HistoricalSchoolAcademyLifecycleService.Capture(__instance);
                HistoricalSchoolAcademyConstructionService.RequestRebuild(__instance);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Academy ruin capture failed: " + error.Message);
            }
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Building), nameof(Building.startRemove))]
        private static void AcademyStartRemove_Prefix(Building __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            try
            {
                HistoricalSchoolAcademyLifecycleService.Capture(__instance);
                HistoricalSchoolAcademyConstructionService.RequestRebuild(__instance);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Academy removal capture failed: " + error.Message);
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Building), nameof(Building.removeBuildingFinal))]
        private static void AcademyRemoveFinal_Postfix(Building __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            try
            {
                HistoricalSchoolAcademyLifecycleService.ConfirmRemoval(__instance);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Academy final removal cleanup failed: " +
                                    error.Message);
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Building), nameof(Building.completeConstruction))]
        private static void AcademyCompleteConstruction_Postfix(Building __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            try
            {
                HistoricalSchoolAcademyRepairService.OnConstructionCompleted(__instance);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Academy construction completion failed: " +
                                    error.Message);
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(MapBox), "updateObjectAge")]
        private static void WorldUpdateAge_Postfix()
        {
            HistoricalSchoolRuntime.EnqueueWorldYear();
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Actor), "die",
            new[] { typeof(bool), typeof(AttackType), typeof(bool), typeof(bool) })]
        private static Exception ActorDie_Finalizer(Actor __instance, bool __state,
            bool pDestroy, Exception __exception)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return __exception;
            if (!ActorDeathInvocationRules.ShouldProcess(__state,
                    __instance?.isAlive() ?? false)) return __exception;
            long diagnostic = RuntimePerformanceDiagnostic.BeginDeathStage(
                ActorDeathPerformanceStage.SchoolDeath);
            try
            {
                if (__instance?.data != null && !__instance.isAlive())
                {
                    try
                    {
                        HistoricalSchoolEducationJourneyService.
                            OnCommittedDeath(__instance);
                    }
                    catch (Exception error)
                    {
                        ModClass.LogWarning(
                            "Education journey death cleanup failed: " +
                            error.Message);
                    }
                    SchoolMembershipService.OnDeath(__instance, pDestroy);
                    CourtService.RequestLocalOfficerDeathReconcile(
                        __instance);
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("School death finalizer failed: " + error.Message);
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDeathStage(
                    ActorDeathPerformanceStage.SchoolDeath, diagnostic);
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

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), "Update")]
        private static void MapBoxUpdate_Prefix()
        {
            long benchmark = RecentFeatureBenchmark.BeginOutsideFrameStage();
            try
            {
                if (!Config.game_loaded || SmoothLoader.isLoading()) return;
                MapBoxFrameStageGuard.Run("school_death_retry",
                    SchoolMembershipService.ProcessDeathRetries);
            }
            finally
            {
                RecentFeatureBenchmark.EndOutsideFrameStage(
                    RecentFeatureBenchmarkRules.SchoolDeathRetryIndex,
                    benchmark);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ActorManager), "destroyObject")]
        private static bool ActorManagerDestroyObject_Prefix(Actor pActor)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return true;
            if (HistoricalSchoolDescentService.ShouldDeferDestroy(pActor)) return false;
            return !SchoolMembershipService.ShouldDeferDestroy(pActor);
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void MapBoxClearWorld_Prefix()
        {
            if (!AWAsyncClearWorldGuard.CleanupAllowed) return;
            HistoricalSchoolRuntime.ClearRuntime();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.newCityEvent))]
        private static void CityNewCityEvent_Postfix(City __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            InvalidateSchoolCityCaches(__instance);
            HistoricalSchoolRuntime.RefreshLivingXiaCity(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "setKingdom")]
        private static void CitySetKingdom_Postfix(
            City __instance,
            bool pFromLoad)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            InvalidateSchoolCityCaches(__instance);
            if (!pFromLoad) HistoricalSchoolRuntime.RefreshLivingXiaCity(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.destroyCity))]
        private static void CityDestroyCity_Prefix(City __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            long cityId = __instance?.data?.id ?? -1L;
            HistoricalSchoolAcademyRepairService.CancelCity(cityId);
            HistoricalSchoolAcademyConstructionService.InvalidateCity(cityId);
            HistoricalSchoolVenueService.ReleaseCityClaims(cityId);
            HistoricalSchoolVenueService.InvalidateCity(cityId);
            SchoolLandmarkService.MarkDirty(cityId);
            HistoricalSchoolRecruitCandidateCache.InvalidateCity(cityId);
            HistoricalSchoolTravelService.InvalidateCityIndex();
            HistoricalSchoolRuntimeIndex.Instance.SetLivingXiaCity(cityId, false);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Building), "makeRuins")]
        private static void AcademyMakeRuins_Prefix(Building __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            HistoricalSchoolAcademyConstructionService.RequestRebuild(__instance);
        }

        private static void InvalidateSchoolCityCaches(City pCity)
        {
            long cityId = pCity?.data?.id ?? -1L;
            HistoricalSchoolAcademyConstructionService.InvalidateCity(cityId);
            HistoricalSchoolVenueService.InvalidateCity(cityId);
            HistoricalSchoolRecruitCandidateCache.InvalidateCity(cityId);
            HistoricalSchoolTravelService.InvalidateCityIndex();
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), nameof(Actor.joinCity))]
        private static bool ActorJoinCity_Prefix(Actor __instance, City pCity)
        {
            if (AW3MultiplayerReplicaScope.IsApplying ||
                SmoothLoader.isLoading() || !Config.game_loaded) return true;
            return HistoricalAffiliationService.CanJoinCity(__instance, pCity);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), nameof(Actor.joinKingdom))]
        private static bool ActorJoinKingdom_Prefix(Actor __instance, Kingdom pKingdom)
        {
            if (AW3MultiplayerReplicaScope.IsApplying ||
                SmoothLoader.isLoading() || !Config.game_loaded) return true;
            return HistoricalAffiliationService.CanJoinKingdom(__instance, pKingdom);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), "setCity", new[] { typeof(City) })]
        private static bool ActorSetCity_Prefix(Actor __instance, City pCity,
            out ActorSetCityState __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying ||
                SmoothLoader.isLoading() || !Config.game_loaded)
            {
                __state = default(ActorSetCityState);
                return true;
            }
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
            if (AW3MultiplayerReplicaScope.IsApplying ||
                SmoothLoader.isLoading() || !Config.game_loaded) return;
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
        private static bool ActorSetKingdom_Prefix(Actor __instance,
            Kingdom pKingdomToSet, out Kingdom __state)
        {
            __state = __instance?.kingdom;
            if (AW3MultiplayerReplicaScope.IsApplying ||
                SmoothLoader.isLoading() || !Config.game_loaded) return true;
            return HistoricalAffiliationService.CanJoinKingdom(__instance, pKingdomToSet);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "setKingdom", new[] { typeof(Kingdom) })]
        private static void ActorSetKingdom_Postfix(Actor __instance,
            Kingdom pKingdomToSet, Kingdom __state)
        {
            if (!OfficerCandidateCatalogRules.ShouldInvalidate(
                    !ReferenceEquals(__state, pKingdomToSet))) return;
            OfficerCandidateCatalog.Invalidate(__state);
            OfficerCandidateCatalog.Invalidate(pKingdomToSet);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BehGoToTileTarget), nameof(BehGoToTileTarget.execute))]
        private static void GoToTileTarget_Postfix(Actor pActor, BehResult __result)
        {
            if (__result != BehResult.Stop || pActor?.data == null ||
                !pActor.isTask(HistoricalSchoolContent.TravelTaskId)) return;
            HistoricalSchoolTravelService.ReportImmediatePathFailure(pActor);
        }
    }
}
