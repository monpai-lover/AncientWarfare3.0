using System;
using System.Collections.Generic;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal static class TemporaryLevyService
    {
        private sealed class LevyPool
        {
            public readonly HashSet<long> ActorIds = new HashSet<long>();
            public readonly long[] DemobilizationBuffer = new long[TemporaryLevyRules.DemobilizationBatchSize];
            public long ActiveWarId = -1L;
            public string NoticeSignature = "";
        }

        private sealed class RecruitmentYearPlan
        {
            public readonly long KingdomId;
            public readonly int Year;
            public int CompletedWorkItems;
            public int ScannedCandidates;
            public int RecruitedActors;
            public int PreferredCityCursor;

            public RecruitmentYearPlan(long pKingdomId, int pYear)
            {
                KingdomId = pKingdomId;
                Year = pYear;
            }
        }

        private sealed class PreparationRecruitmentPlan
        {
            public readonly long KingdomId;
            public readonly int MonthKey;
            public readonly HashSet<long> VisitedCityIds =
                new HashSet<long>();
            public long CurrentCityId = -1L;
            public int CitySelectionCursor;

            public PreparationRecruitmentPlan(long pKingdomId,
                int pMonthKey)
            {
                KingdomId = pKingdomId;
                MonthKey = pMonthKey;
            }
        }

        private sealed class CasualtyReinforcementPlan
        {
            public readonly long KingdomId;
            public readonly Dictionary<long, int> TargetDemandsByArmy =
                new Dictionary<long, int>();
            public readonly HashSet<long> ExhaustedCandidateCityIds =
                new HashSet<long>();
            public readonly HashSet<long> ExhaustedTargetArmyIds =
                new HashSet<long>();
            public int PendingDemand;
            public int CompletedWorkItems;
            public long PreferredCityId = -1L;
            public long CurrentCityId = -1L;
            public long TargetDemandCursor = -1L;
            public bool ForceEstablishment;
            public bool RealmReserveExhausted;

            public CasualtyReinforcementPlan(long pKingdomId)
            {
                KingdomId = pKingdomId;
            }
        }

        private sealed class CaptainRecoveryPlan
        {
            public readonly long KingdomId;
            public readonly long TargetArmyId;
            public int CompletedWorkItems;
            public long PreferredCityId = -1L;

            public CaptainRecoveryPlan(long pKingdomId, long pTargetArmyId)
            {
                KingdomId = pKingdomId;
                TargetArmyId = pTargetArmyId;
            }
        }

        private enum RecruitmentCandidateRejection
        {
            None,
            NotResident,
            NotLivingAdult,
            WrongProfession,
            ReservePolicy,
            SlavePolicy,
            ProtectedIdentity,
            NativeEligibility,
            AgeLimit,
            Capacity
        }

        private struct RecruitmentScanSummary
        {
            public int AlreadyWarrior;
            public int Ineligible;
            public int Viable;
            public int EnlistFailures;
            public int NotResident;
            public int NotLivingAdult;
            public int WrongProfession;
            public int ReservePolicy;
            public int SlavePolicy;
            public int ProtectedIdentity;
            public int NativeEligibility;
            public int AgeLimit;
            public int Capacity;

            public void RecordRejection(RecruitmentCandidateRejection pReason)
            {
                Ineligible++;
                switch (pReason)
                {
                    case RecruitmentCandidateRejection.NotResident:
                        NotResident++;
                        break;
                    case RecruitmentCandidateRejection.NotLivingAdult:
                        NotLivingAdult++;
                        break;
                    case RecruitmentCandidateRejection.WrongProfession:
                        WrongProfession++;
                        break;
                    case RecruitmentCandidateRejection.ReservePolicy:
                        ReservePolicy++;
                        break;
                    case RecruitmentCandidateRejection.SlavePolicy:
                        SlavePolicy++;
                        break;
                    case RecruitmentCandidateRejection.ProtectedIdentity:
                        ProtectedIdentity++;
                        break;
                    case RecruitmentCandidateRejection.NativeEligibility:
                        NativeEligibility++;
                        break;
                    case RecruitmentCandidateRejection.AgeLimit:
                        AgeLimit++;
                        break;
                    case RecruitmentCandidateRejection.Capacity:
                        Capacity++;
                        break;
                }
            }
        }

        private static readonly Dictionary<long, LevyPool> Pools = new Dictionary<long, LevyPool>();
        private static readonly Dictionary<long, RecruitmentYearPlan> RecruitmentPlans =
            new Dictionary<long, RecruitmentYearPlan>();
        private static readonly Dictionary<long, PreparationRecruitmentPlan>
            PreparationRecruitmentPlans =
                new Dictionary<long, PreparationRecruitmentPlan>();
        private static readonly Dictionary<long, CasualtyReinforcementPlan>
            CasualtyReinforcementPlans =
                new Dictionary<long, CasualtyReinforcementPlan>();
        private static readonly Dictionary<long, CaptainRecoveryPlan>
            CaptainRecoveryPlans = new Dictionary<long, CaptainRecoveryPlan>();
        private static readonly HashSet<long> ActiveActorIds = new HashSet<long>();
        private const int PreparationKingdomsPerAuthorityCycle = 2;
        private static readonly MonthlyAuthorityWorkQueue<Kingdom>
            PreparationMonthlyWork =
                new MonthlyAuthorityWorkQueue<Kingdom>();

        internal static int PendingMonthlyWorkForDiagnostics =>
            PreparationMonthlyWork.PendingCount;

        private static int LastPreparationMonthKey = int.MinValue;

        public static bool IsTemporaryLevy(Actor pActor)
        {
            return pActor?.data != null && ActiveActorIds.Contains(pActor.data.id);
        }

        internal static void RegisterSyntheticLevy(Actor pActor,
            Kingdom pKingdom, City pCity, long pEmergencyId)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                pCity?.data == null) return;
            LevyPool pool = Pool(pKingdom.id);
            pool.ActiveWarId = pEmergencyId;
            pActor.data.set(LineageKeys.TEMPORARY_LEVY, true);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_KINGDOM_ID,
                pKingdom.id);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_NOTICE_SIGNATURE,
                string.Empty);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_ORIGINAL_CITY_ID,
                pCity.id);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_WAR_ID,
                pEmergencyId);
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
            pool.ActorIds.Add(pActor.data.id);
            ActiveActorIds.Add(pActor.data.id);
        }

        internal static bool CanRegisterReserve(Kingdom pKingdom, City pCity,
            Actor pActor)
        {
            if (pActor?.data == null || pCity?.data == null ||
                pKingdom?.data == null || pActor.city != pCity ||
                pActor.kingdom != pKingdom || pActor.isRekt() ||
                !pActor.isAlive() || !pActor.isAdult() ||
                pActor.asset?.is_boat == true ||
                !pActor.isProfession(UnitProfession.Unit) ||
                SlaveService.IsRetiredSoldier(pActor) ||
                SlaveService.IsSlave(pActor)) return false;

            bool protectedIdentity = IsProtectedIdentity(pKingdom, pActor,
                pAllowSlave: false);
            bool originalEligible = PassesOriginalEligibilityWithoutCapacity(
                pCity, pActor);
            return TemporaryLevyRules.CanRegisterReserve(originalEligible,
                protectedIdentity, pActor.getAge());
        }

        public static bool TryPromoteExistingLevyCaptain(Army pArmy)
        {
            if (pArmy?.data == null || AWArmyService.IsSpecialArmy(pArmy) ||
                HasOperationalCaptain(pArmy)) return false;
            Kingdom kingdom = AWArmyService.GetIntendedKingdom(pArmy);
            if (kingdom?.data == null || kingdom.isRekt() ||
                !MilitaryEmergencyService.HasAny(kingdom)) return false;

            Actor candidate = null;
            long candidateId = -1L;
            try
            {
                foreach (Actor member in pArmy.getUnits())
                {
                    if (!CanPromoteExistingLevyCaptain(kingdom, pArmy,
                            member)) continue;
                    long memberId = member.data.id;
                    if (!ArmyCaptainContinuityRules.ShouldPreferReplacement(
                            candidateId, memberId)) continue;
                    candidate = member;
                    candidateId = memberId;
                }
            }
            catch { candidate = null; }
            if (candidate?.data == null) return false;

            candidate.data.get(LineageKeys.TEMPORARY_LEVY_KINGDOM_ID,
                out long levyKingdomId, -1L);
            if (levyKingdomId >= 0 && Pools.TryGetValue(levyKingdomId,
                    out LevyPool pool))
            {
                pool.ActorIds.Remove(candidate.data.id);
                if (pool.ActorIds.Count == 0) Pools.Remove(levyKingdomId);
            }
            ClearFields(candidate);
            candidate.data.set(LineageKeys.MILITARY_BIOGRAPHY_ACTIVE, true);
            candidate.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME,
                (float)LineageService.CurTime());
            AWArmyService.SetCaptainIfChanged(pArmy, candidate);
            if (!HasOperationalCaptain(pArmy)) return false;

            WarNoticeService.QueueArmyChanged(kingdom, pArmy,
                pRosterExpanded: false);
            KingdomWarDirectorService.OnArmyChanged(kingdom);
            return true;
        }

        public static void OnActorInvalidated(Actor pActor)
        {
            if (pActor?.data == null) return;
            if (!ActiveActorIds.Remove(pActor.data.id)) return;
            pActor.data.get(LineageKeys.TEMPORARY_LEVY_KINGDOM_ID, out long kingdomId, -1L);
            if (kingdomId >= 0 && Pools.TryGetValue(kingdomId, out LevyPool pool))
            {
                pool.ActorIds.Remove(pActor.data.id);
                if (pool.ActorIds.Count == 0) Pools.Remove(kingdomId);
            }
            ClearFields(pActor);
        }

        public static void OnMilitaryCasualty(Actor pActor)
        {
            if (SyntheticLevySpawnScope.IsActive ||
                pActor?.data == null || !pActor.isWarrior()) return;
            Kingdom kingdom = pActor.kingdom;
            if (kingdom?.data == null || kingdom.isRekt() ||
                !MilitaryEmergencyService.HasAny(kingdom)) return;
            Army sourceArmy = pActor.army;
            bool specialArmy = sourceArmy?.data != null &&
                               AWArmyService.IsSpecialArmy(sourceArmy);
            if (RoyalGuardService.IsRoyalGuard(pActor) ||
                WartimeGarrisonService.IsActive(pActor) ||
                TemporarySlaveVanguardService.IsMember(pActor)) return;
            if (specialArmy) return;

            if (!CasualtyReinforcementPlans.TryGetValue(kingdom.id,
                    out CasualtyReinforcementPlan plan))
            {
                plan = new CasualtyReinforcementPlan(kingdom.id);
                CasualtyReinforcementPlans[kingdom.id] = plan;
            }
            if (TemporaryLevyRules.ShouldDirectCasualtyRecoveryToArmy(
                    sourceArmy?.data != null, specialArmy))
            {
                int current = plan.TargetDemandsByArmy.TryGetValue(
                    sourceArmy.id, out int existing) ? existing : 0;
                plan.TargetDemandsByArmy[sourceArmy.id] =
                    TemporaryLevyRules.AddCasualtyReinforcementDemand(
                        current);
                plan.ForceEstablishment |= TemporaryLevyRules.
                    ShouldForceEmergencyRecoverySlots(
                        forceEstablishment: false,
                        directedReplenishment: true);
            }
            else
                plan.PendingDemand = TemporaryLevyRules.
                    AddCasualtyReinforcementDemand(plan.PendingDemand);
            if (plan.PreferredCityId < 0 && pActor.city?.data != null &&
                pActor.city.kingdom == kingdom)
                plan.PreferredCityId = pActor.city.id;
            ScheduleCasualtyReinforcement(plan);
        }

        public static void RequestOffensiveRecovery(Kingdom pKingdom,
            City pPreferredCity)
        {
            RequestOffensiveRecovery(pKingdom, pPreferredCity,
                TemporaryLevyRules.MaxRecruitsPerWorkItem);
        }

        public static void RequestOffensiveRecovery(Kingdom pKingdom,
            City pPreferredCity, int pDemand)
        {
            RequestOffensiveRecovery(pKingdom, pPreferredCity, pDemand,
                pForceEstablishment: false, pTargetArmy: null);
        }

        public static void RequestOffensiveRecovery(Kingdom pKingdom,
            City pPreferredCity, int pDemand,
            bool pForceEstablishment)
        {
            RequestOffensiveRecovery(pKingdom, pPreferredCity, pDemand,
                pForceEstablishment, pTargetArmy: null);
        }

        public static void RequestOffensiveRecovery(Kingdom pKingdom,
            City pPreferredCity, int pDemand, Army pTargetArmy)
        {
            RequestOffensiveRecovery(pKingdom, pPreferredCity, pDemand,
                pForceEstablishment: false, pTargetArmy);
        }

        private static void RequestOffensiveRecovery(Kingdom pKingdom,
            City pPreferredCity, int pDemand,
            bool pForceEstablishment, Army pTargetArmy)
        {
            bool kingdomLive = pKingdom?.data != null &&
                               !pKingdom.isRekt();
            bool emergencyActive = kingdomLive &&
                MilitaryEmergencyService.HasAny(pKingdom);
            bool restorationCampaign = kingdomLive &&
                AutonomousRestorationService.IsActiveCampaignKingdom(
                    pKingdom);
            bool accepted = kingdomLive && emergencyActive &&
                            !restorationCampaign && pDemand > 0;
            if (TemporaryLevyDiagnosticRules.
                    ShouldWriteRecoveryRequestDiagnostic(
                        AWPerformanceSettings.ArmyRtsDiagnosticsEnabled,
                        pDemand))
            {
                AncientWarfare3.ModClass.LogInfo(
                    "[AW3 RTS levy request] kingdom=" +
                    (pKingdom?.id ?? -1L) + " demand=" +
                    Math.Max(0, pDemand) + " target_army=" +
                    (pTargetArmy?.id ?? -1L) + " preferred_city=" +
                    (pPreferredCity?.id ?? -1L) + " emergency=" +
                    emergencyActive + " restoration=" +
                    restorationCampaign + " accepted=" + accepted);
            }
            if (!accepted) return;

            bool planCreated = !CasualtyReinforcementPlans.TryGetValue(
                pKingdom.id, out CasualtyReinforcementPlan plan);
            if (planCreated)
            {
                plan = new CasualtyReinforcementPlan(pKingdom.id);
                CasualtyReinforcementPlans[pKingdom.id] = plan;
            }
            bool directedReplenishment = pTargetArmy?.data != null;
            if (directedReplenishment)
            {
                int current = plan.TargetDemandsByArmy.TryGetValue(
                    pTargetArmy.id, out int existing) ? existing : 0;
                plan.TargetDemandsByArmy[pTargetArmy.id] =
                    TemporaryLevyRules.MergeDirectedReplenishmentDemand(
                        current, pDemand);
            }
            else
                plan.PendingDemand = TemporaryLevyRules.
                    AddReplenishmentDemand(plan.PendingDemand, pDemand);
            if (TemporaryLevyRules.ShouldResetCasualtyReinforcementProgress(
                    planCreated))
                plan.CompletedWorkItems = 0;
            plan.ForceEstablishment |= TemporaryLevyRules.
                ShouldForceEmergencyRecoverySlots(pForceEstablishment,
                    directedReplenishment);
            if (pPreferredCity?.data != null &&
                pPreferredCity.kingdom == pKingdom &&
                !pPreferredCity.isRekt())
                plan.PreferredCityId = pPreferredCity.id;
            ScheduleCasualtyReinforcement(plan);
        }

        public static bool HasPendingOffensiveRecovery(Kingdom pKingdom)
        {
            return pKingdom?.data != null &&
                   CasualtyReinforcementPlans.TryGetValue(pKingdom.id,
                       out CasualtyReinforcementPlan plan) &&
                   PendingDemand(plan) > 0;
        }

        internal static bool HasConfirmedReserveExhaustion(
            Kingdom pKingdom, Army pTargetArmy)
        {
            if (pKingdom?.data == null || pTargetArmy?.data == null ||
                !CasualtyReinforcementPlans.TryGetValue(pKingdom.id,
                    out CasualtyReinforcementPlan plan) ||
                !plan.ExhaustedTargetArmyIds.Contains(pTargetArmy.id))
                return false;
            return plan.TargetDemandsByArmy.TryGetValue(pTargetArmy.id,
                       out int demand) && demand > 0;
        }

        internal static void RecordConfirmedReserveExhaustion(
            Kingdom pKingdom, Army pTargetArmy, int pRemainingDemand)
        {
            if (pKingdom?.data == null || pTargetArmy?.data == null ||
                pRemainingDemand <= 0) return;
            if (!CasualtyReinforcementPlans.TryGetValue(pKingdom.id,
                    out CasualtyReinforcementPlan plan))
            {
                plan = new CasualtyReinforcementPlan(pKingdom.id);
                CasualtyReinforcementPlans[pKingdom.id] = plan;
            }
            plan.TargetDemandsByArmy[pTargetArmy.id] = pRemainingDemand;
            plan.ExhaustedTargetArmyIds.Add(pTargetArmy.id);
        }

        public static void RequestCaptainRecovery(Kingdom pKingdom,
            Army pArmy)
        {
            if (pKingdom?.data == null || pArmy?.data == null ||
                pKingdom.isRekt() ||
                !MilitaryEmergencyService.HasAny(pKingdom) ||
                AWArmyService.IsSpecialArmy(pArmy) ||
                HasOperationalCaptain(pArmy)) return;
            Kingdom armyKingdom;
            try { armyKingdom = pArmy.getKingdom(); }
            catch { return; }
            if (armyKingdom != pKingdom) return;

            if (!CaptainRecoveryPlans.TryGetValue(pArmy.id,
                    out CaptainRecoveryPlan plan))
            {
                plan = new CaptainRecoveryPlan(pKingdom.id, pArmy.id);
                CaptainRecoveryPlans[pArmy.id] = plan;
            }
            City anchor = AWArmyService.FindAnchorCity(pArmy);
            if (anchor?.data != null && anchor.kingdom == pKingdom &&
                !anchor.isRekt())
                plan.PreferredCityId = anchor.id;
            ScheduleCaptainRecovery(plan);
        }

        public static bool HasActivePool(Kingdom pKingdom)
        {
            return pKingdom?.data != null &&
                   Pools.TryGetValue(pKingdom.id, out LevyPool pool) &&
                   pool.ActorIds.Count > 0;
        }

        public static int ActiveLevyCount(Kingdom pKingdom)
        {
            return pKingdom?.data != null &&
                   Pools.TryGetValue(pKingdom.id, out LevyPool pool)
                ? pool.ActorIds.Count
                : 0;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || pKingdom.isNeutral()) return;
            if (AutonomousRestorationService.IsActiveCampaignKingdom(pKingdom))
            {
                RecruitmentPlans.Remove(pKingdom.id);
                ScheduleDemobilization(pKingdom.id);
                return;
            }
            bool emergencyActive = MilitaryEmergencyService.HasAny(pKingdom);
            bool activeNotice = WarNoticeService.HasActiveNotice(pKingdom);
            bool activeWar = MilitaryEmergencyService.TryGetActiveWarId(
                pKingdom, out _);
            if (!TemporaryLevyRules.ShouldRunAnnualRecruitment(
                    emergencyActive, activeNotice, activeWar))
            {
                if (activeNotice)
                    RecruitmentPlans.Remove(pKingdom.id);
                else
                    ScheduleDemobilization(pKingdom.id);
                return;
            }

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_LAST_YEAR, out int lastYear, int.MinValue);
            if (lastYear == year)
            {
                ResumeRecruitmentYear(pKingdom, year);
                return;
            }
            pKingdom.data.set(LineageKeys.TEMPORARY_LEVY_LAST_YEAR, year);
            ScheduleRecruitmentYear(pKingdom, year);
        }

        public static void ProcessPreparationMonth()
        {
            if (World.world?.kingdoms == null) return;
            int monthKey = TemporaryLevyRules.ToMonthKey(
                Date.getCurrentYear(), Date.getCurrentMonth());
            if (TemporaryLevyRules.ShouldProcessPreparationMonth(monthKey,
                    LastPreparationMonthKey))
            {
                LastPreparationMonthKey = monthKey;
                PreparationMonthlyWork.ScheduleMonth(monthKey,
                    World.world.kingdoms);
            }
            PreparationMonthlyWork.Drain(
                PreparationKingdomsPerAuthorityCycle,
                (queuedMonthKey, kingdom) =>
                {
                    long benchmark = RecentFeatureBenchmark.Begin();
                    try
                    {
                        ProcessPreparationMonth(kingdom, queuedMonthKey);
                    }
                    finally
                    {
                        RecentFeatureBenchmark.End(
                            RecentFeatureBenchmarkRules.MonthPreparationLevyIndex,
                            benchmark);
                    }
                });
        }

        private static void ProcessPreparationMonth(Kingdom pKingdom,
            int pMonthKey)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                pKingdom.isNeutral() ||
                AutonomousRestorationService.IsActiveCampaignKingdom(
                    pKingdom))
            {
                CancelPreparationRecruitment(pKingdom);
                return;
            }

            bool emergencyActive = MilitaryEmergencyService.HasAny(pKingdom);
            bool activeNotice = CityReservePoolService.
                ResolveMobilizationPhase(pKingdom) ==
                ArmyMobilizationPhase.Notice;
            if (!emergencyActive || !activeNotice)
            {
                CancelPreparationRecruitment(pKingdom);
                return;
            }

            if (PreparationRecruitmentPlans.TryGetValue(pKingdom.id,
                    out PreparationRecruitmentPlan activePlan) &&
                activePlan.MonthKey != pMonthKey)
            {
                CancelPreparationRecruitment(pKingdom);
                activePlan = null;
            }

            if (activePlan == null)
                activePlan = RestorePreparationRecruitmentPlan(pKingdom,
                    pMonthKey);
            if (activePlan != null)
            {
                if (!TemporaryLevyRules.ShouldContinuePreparationMonth(
                        emergencyActive, activeNotice,
                        activePlan.VisitedCityIds.Count,
                        pKingdom.cities?.Count ?? 0))
                    CompletePreparationRecruitment(pKingdom);
                else
                    SchedulePreparationRecruitment(activePlan);
                return;
            }

            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_PREPARATION_MONTH,
                out int lastMonthKey, int.MinValue);
            if (!TemporaryLevyRules.ShouldStartPreparationMonth(
                    emergencyActive, activeNotice, pMonthKey,
                    lastMonthKey)) return;

            var plan = new PreparationRecruitmentPlan(pKingdom.id,
                pMonthKey);
            PreparationRecruitmentPlans[pKingdom.id] = plan;
            PersistPreparationRecruitmentPlan(pKingdom, plan);
            SchedulePreparationRecruitment(plan);
        }

        public static void OnWarStarted(War pWar, string pNoticeSignature)
        {
            if (pWar?.data == null) return;
            foreach (Kingdom kingdom in pWar.getAttackers())
            {
                ActivateWar(kingdom, pWar.data.id, pNoticeSignature);
                OnEmergencyChanged(kingdom);
            }
            foreach (Kingdom kingdom in pWar.getDefenders())
            {
                ActivateWar(kingdom, pWar.data.id, pNoticeSignature);
                OnEmergencyChanged(kingdom);
            }
        }

        public static void OnWarEnded(War pWar)
        {
            if (pWar?.data == null) return;
            foreach (Kingdom kingdom in pWar.getAttackers()) OnEmergencyChanged(kingdom);
            foreach (Kingdom kingdom in pWar.getDefenders()) OnEmergencyChanged(kingdom);
        }

        public static void OnKingdomDestroying(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            StandingArmyService.OnKingdomDestroying(pKingdom);
            RecruitmentPlans.Remove(pKingdom.id);
            CancelPreparationRecruitment(pKingdom);
            CasualtyReinforcementPlans.Remove(pKingdom.id);
            RemoveCaptainRecoveryPlansForKingdom(pKingdom.id);
            ScheduleDemobilization(pKingdom.id);
        }

        public static void OnNoticeClosed(long pKingdomId)
        {
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (kingdom?.data == null) return;
            CancelPreparationRecruitment(kingdom);
            OnEmergencyChanged(kingdom);
        }

        public static void OnEmergencyChanged(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            LastPreparationMonthKey = int.MinValue;
            PreparationMonthlyWork.ResetScheduleGate();
            if (AutonomousRestorationService.IsActiveCampaignKingdom(pKingdom))
            {
                RecruitmentPlans.Remove(pKingdom.id);
                CasualtyReinforcementPlans.Remove(pKingdom.id);
                RemoveCaptainRecoveryPlansForKingdom(pKingdom.id);
                ScheduleDemobilization(pKingdom.id);
                return;
            }
            long kingdomId = pKingdom.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("levy_emergency", kingdomId),
                DeferredWorkClass.CriticalRuntime,
                () => ProcessEmergencyChanged(kingdomId));
        }

        private static void ProcessEmergencyChanged(long pKingdomId)
        {
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (kingdom?.data == null) return;
            if (MilitaryEmergencyService.HasAny(kingdom))
                OnKingdomYear(kingdom);
            else
            {
                CasualtyReinforcementPlans.Remove(pKingdomId);
                RemoveCaptainRecoveryPlansForKingdom(pKingdomId);
                ScheduleIfSafe(kingdom);
            }
        }

        public static void RebuildRuntime()
        {
            StandingArmyService.ClearEstablishmentRuntime();
            Pools.Clear();
            RecruitmentPlans.Clear();
            PreparationRecruitmentPlans.Clear();
            CasualtyReinforcementPlans.Clear();
            CaptainRecoveryPlans.Clear();
            ActiveActorIds.Clear();
            LastPreparationMonthKey = int.MinValue;
            PreparationMonthlyWork.Clear();
            if (World.world?.units != null)
            {
                foreach (Actor actor in World.world.units)
                {
                    if (!HasPersistedFlag(actor)) continue;
                    actor.data.get(LineageKeys.TEMPORARY_LEVY_KINGDOM_ID, out long kingdomId, -1L);
                    if (kingdomId < 0)
                    {
                        ClearFields(actor);
                        continue;
                    }
                    ActiveActorIds.Add(actor.data.id);
                    Pool(kingdomId).ActorIds.Add(actor.data.id);
                }
            }

            foreach (long kingdomId in new List<long>(Pools.Keys))
            {
                Kingdom kingdom = ResolveKingdom(kingdomId);
                if (kingdom?.data == null || !MilitaryEmergencyService.HasAny(kingdom))
                    ScheduleDemobilization(kingdomId);
            }
            ResumeActiveRecruitmentPlans();
        }

        public static void ClearRuntime()
        {
            StandingArmyService.ClearEstablishmentRuntime();
            Pools.Clear();
            RecruitmentPlans.Clear();
            PreparationRecruitmentPlans.Clear();
            CasualtyReinforcementPlans.Clear();
            CaptainRecoveryPlans.Clear();
            ActiveActorIds.Clear();
            LastPreparationMonthKey = int.MinValue;
            PreparationMonthlyWork.Clear();
        }

        private static void ScheduleRecruitmentYear(Kingdom pKingdom, int pYear)
        {
            if (pKingdom?.data == null || pYear < 0) return;
            if (!RecruitmentPlans.TryGetValue(pKingdom.id, out RecruitmentYearPlan plan) ||
                plan.Year != pYear)
            {
                plan = new RecruitmentYearPlan(pKingdom.id, pYear);
                RecruitmentPlans[pKingdom.id] = plan;
                PersistRecruitmentPlan(pKingdom, plan);
            }
            ScheduleRecruitmentBatch(plan);
        }

        private static void ResumeRecruitmentYear(Kingdom pKingdom, int pYear)
        {
            if (pKingdom?.data == null) return;
            if (!RecruitmentPlans.TryGetValue(pKingdom.id, out RecruitmentYearPlan plan) ||
                plan.Year != pYear)
                plan = RestoreRecruitmentPlan(pKingdom, pYear);
            if (plan == null ||
                !TemporaryLevyRules.ShouldRunRecruitmentWorkItem(true,
                    plan.CompletedWorkItems, plan.ScannedCandidates,
                    plan.RecruitedActors)) return;
            ScheduleRecruitmentBatch(plan);
        }

        private static RecruitmentYearPlan RestoreRecruitmentPlan(Kingdom pKingdom, int pYear)
        {
            if (pKingdom?.data == null || pYear < 0) return null;
            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_LAST_YEAR, out int recordedYear, int.MinValue);
            if (recordedYear != pYear) return null;
            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_WORK_ITEMS, out int workItems, 0);
            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_SCANNED, out int scanned, 0);
            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_RECRUITED, out int recruited, 0);
            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_FRONTIER_CURSOR, out int frontierCursor, 0);
            var plan = new RecruitmentYearPlan(pKingdom.id, pYear)
            {
                CompletedWorkItems = TemporaryLevyRules.ClampRestoredCounter(
                    workItems, TemporaryLevyRules.MaxWorkItemsPerKingdomYear),
                ScannedCandidates = TemporaryLevyRules.ClampRestoredCounter(
                    scanned, TemporaryLevyRules.MaxCandidatesPerKingdomYear),
                RecruitedActors = TemporaryLevyRules.ClampRestoredCounter(
                    recruited, TemporaryLevyRules.MaxRecruitsPerKingdomYear),
                PreferredCityCursor = TemporaryLevyRules.ClampRestoredCounter(
                    frontierCursor, TemporaryLevyRules.MaxWorkItemsPerKingdomYear)
            };
            RecruitmentPlans[pKingdom.id] = plan;
            return plan;
        }

        private static void PersistRecruitmentPlan(Kingdom pKingdom, RecruitmentYearPlan pPlan)
        {
            if (pKingdom?.data == null || pPlan == null || pKingdom.id != pPlan.KingdomId) return;
            pKingdom.data.set(LineageKeys.TEMPORARY_LEVY_LAST_YEAR, pPlan.Year);
            pKingdom.data.set(LineageKeys.TEMPORARY_LEVY_WORK_ITEMS, pPlan.CompletedWorkItems);
            pKingdom.data.set(LineageKeys.TEMPORARY_LEVY_SCANNED, pPlan.ScannedCandidates);
            pKingdom.data.set(LineageKeys.TEMPORARY_LEVY_RECRUITED, pPlan.RecruitedActors);
            pKingdom.data.set(LineageKeys.TEMPORARY_LEVY_FRONTIER_CURSOR, pPlan.PreferredCityCursor);
        }

        private static void ResumeActiveRecruitmentPlans()
        {
            if (World.world?.kingdoms == null) return;
            int year = Date.getCurrentYear();
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral() ||
                    !MilitaryEmergencyService.HasAny(kingdom)) continue;
                kingdom.data.get(LineageKeys.TEMPORARY_LEVY_LAST_YEAR,
                    out int lastYear, int.MinValue);
                if (lastYear == year)
                    ResumeRecruitmentYear(kingdom, year);
                else
                {
                    kingdom.data.set(LineageKeys.TEMPORARY_LEVY_LAST_YEAR, year);
                    ScheduleRecruitmentYear(kingdom, year);
                }
            }
        }

        private static void ScheduleRecruitmentBatch(RecruitmentYearPlan pPlan)
        {
            if (pPlan == null) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("levy_recruit", pPlan.KingdomId),
                DeferredWorkClass.CriticalRuntime,
                () => ProcessRecruitmentBatch(pPlan.KingdomId, pPlan.Year));
        }

        private static void SchedulePreparationRecruitment(
            PreparationRecruitmentPlan pPlan)
        {
            if (pPlan == null) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "levy_preparation", pPlan.KingdomId),
                DeferredWorkClass.CriticalRuntime,
                () => ProcessPreparationRecruitment(pPlan.KingdomId,
                    pPlan.MonthKey));
        }

        private static void ProcessPreparationRecruitment(long pKingdomId,
            int pMonthKey)
        {
            if (!PreparationRecruitmentPlans.TryGetValue(pKingdomId,
                    out PreparationRecruitmentPlan plan) ||
                plan.MonthKey != pMonthKey) return;
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (kingdom?.data == null || kingdom.isRekt() ||
                kingdom.isNeutral() ||
                AutonomousRestorationService.IsActiveCampaignKingdom(
                    kingdom))
            {
                CancelPreparationRecruitment(kingdom);
                return;
            }

            int currentMonthKey = TemporaryLevyRules.ToMonthKey(
                Date.getCurrentYear(), Date.getCurrentMonth());
            if (currentMonthKey != pMonthKey)
            {
                CancelPreparationRecruitment(kingdom);
                return;
            }
            if (CityReservePoolService.ResolveMobilizationPhase(kingdom) !=
                ArmyMobilizationPhase.Notice)
            {
                CancelPreparationRecruitment(kingdom);
                return;
            }

            if (!TrySelectPreparationCity(kingdom, plan, out City city,
                    out bool waitingForFrontier))
            {
                if (waitingForFrontier)
                    SchedulePreparationRecruitment(plan);
                else
                    CompletePreparationRecruitment(kingdom);
                return;
            }
            if (!StandingArmyService.RequestEstablishment(kingdom, city,
                    out ArmyRecruitmentDisposition disposition,
                    out Army targetArmy))
            {
                PersistPreparationRecruitmentPlan(kingdom, plan);
                SchedulePreparationRecruitment(plan);
                return;
            }

            int living = SafeArmyCount(targetArmy);
            int targetStrength = targetArmy?.data != null
                ? StandingArmyService.TargetStrength(targetArmy, kingdom)
                : PreparationTargetStrength(kingdom, city);
            int requested = TemporaryLevyRules.PreparationRequest(
                establishmentAccepted:
                    disposition != ArmyRecruitmentDisposition.Reject,
                living, targetStrength,
                TemporaryLevyRules.MaxRecruitsPerWorkItem);
            var candidates = new List<Actor>(requested);
            bool confirmedExhausted = false;
            if (requested > 0)
                CityReservePoolService.TryConsumeForMobilization(
                    kingdom, city, requested, targetArmy,
                    allowArmyCreation:
                        disposition == ArmyRecruitmentDisposition.Create,
                    candidates, out confirmedExhausted);
            int recruited = EnlistPreparationActors(kingdom, city,
                disposition, ref targetArmy, candidates,
                ref confirmedExhausted);
            int remainingShortage = targetArmy?.data != null
                ? ApprovedTargetShortage(kingdom, targetArmy)
                : Math.Max(0, targetStrength);
            bool cityWorkComplete = confirmedExhausted ||
                                    remainingShortage <= 0 ||
                                    candidates.Count == 0;
            bool keepCity = TemporaryLevyRules.
                ShouldKeepPreparationRecruitmentCity(cityWorkComplete,
                    recruited);
            if (!keepCity)
            {
                plan.VisitedCityIds.Add(city.id);
                plan.CurrentCityId = -1L;
            }
            PersistPreparationRecruitmentPlan(kingdom, plan);
            if (TemporaryLevyRules.ShouldContinuePreparationMonth(
                    emergencyActive: true, activeNotice: true,
                    plan.VisitedCityIds.Count,
                    kingdom.cities?.Count ?? 0))
                SchedulePreparationRecruitment(plan);
            else
                CompletePreparationRecruitment(kingdom);
        }

        private static bool TrySelectPreparationCity(Kingdom pKingdom,
            PreparationRecruitmentPlan pPlan, out City pCity,
            out bool pWaitingForFrontier)
        {
            pCity = ResolveCity(pPlan.CurrentCityId);
            pWaitingForFrontier = false;
            if (IsPreparationCity(pKingdom, pPlan, pCity)) return true;

            pPlan.CurrentCityId = -1L;
            bool preferredTargetsReady = ArmyDeploymentService.
                TryGetPreferredLevyCityCount(pKingdom,
                    out int preferredCityCount);
            if (!preferredTargetsReady) preferredCityCount = 0;

            int cityCount = pKingdom.cities?.Count ?? 0;
            int candidateCount = preferredCityCount + cityCount;
            if (candidateCount <= 0) return false;
            int start = PositiveModulo(pPlan.CitySelectionCursor,
                candidateCount);
            for (int offset = 0; offset < candidateCount; offset++)
            {
                int ordinal = (start + offset) % candidateCount;
                pPlan.CitySelectionCursor =
                    PositiveModulo(ordinal + 1, candidateCount);
                City candidate = null;
                if (ordinal < preferredCityCount)
                {
                    if (!ArmyDeploymentService.TryGetPreferredLevyCity(
                            pKingdom, ordinal, out candidate))
                        continue;
                }
                else
                {
                    int cityOrdinal = ordinal - preferredCityCount;
                    try { candidate = pKingdom.cities[cityOrdinal]; }
                    catch { candidate = null; }
                }
                if (!IsPreparationCity(pKingdom, pPlan, candidate))
                    continue;
                pPlan.CurrentCityId = candidate.id;
                pCity = candidate;
                return true;
            }
            pCity = null;
            return false;
        }

        private static bool IsPreparationCity(Kingdom pKingdom,
            PreparationRecruitmentPlan pPlan, City pCity)
        {
            return pKingdom?.data != null && pPlan != null &&
                   pCity?.data != null && !pCity.isRekt() &&
                   pCity.kingdom == pKingdom &&
                   !pPlan.VisitedCityIds.Contains(pCity.id);
        }

        private static PreparationRecruitmentPlan
            RestorePreparationRecruitmentPlan(Kingdom pKingdom,
                int pMonthKey)
        {
            if (pKingdom?.data == null) return null;
            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_PREPARATION_MONTH,
                out int recordedMonthKey, int.MinValue);
            pKingdom.data.get(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_IN_PROGRESS,
                out bool inProgress, false);
            if (recordedMonthKey != pMonthKey || !inProgress) return null;

            var plan = new PreparationRecruitmentPlan(pKingdom.id,
                pMonthKey);
            pKingdom.data.get(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_CURRENT_CITY,
                out plan.CurrentCityId, -1L);
            pKingdom.data.get(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_FRONTIER_CURSOR,
                out plan.CitySelectionCursor, 0);
            pKingdom.data.get(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_VISITED_CITIES,
                out string visitedCityIds, "");
            RestoreVisitedPreparationCities(plan, visitedCityIds);
            PreparationRecruitmentPlans[pKingdom.id] = plan;
            return plan;
        }

        private static void PersistPreparationRecruitmentPlan(
            Kingdom pKingdom, PreparationRecruitmentPlan pPlan)
        {
            if (pKingdom?.data == null || pPlan == null ||
                pKingdom.id != pPlan.KingdomId) return;
            pKingdom.data.set(LineageKeys.TEMPORARY_LEVY_PREPARATION_MONTH,
                pPlan.MonthKey);
            pKingdom.data.set(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_IN_PROGRESS, true);
            pKingdom.data.set(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_CURRENT_CITY,
                pPlan.CurrentCityId);
            pKingdom.data.set(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_FRONTIER_CURSOR,
                pPlan.CitySelectionCursor);
            pKingdom.data.set(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_VISITED_CITIES,
                SerializeVisitedPreparationCities(pPlan.VisitedCityIds));
        }

        private static void CompletePreparationRecruitment(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            PreparationRecruitmentPlans.Remove(pKingdom.id);
            pKingdom.data.set(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_IN_PROGRESS, false);
            pKingdom.data.set(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_CURRENT_CITY, -1L);
            pKingdom.data.set(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_FRONTIER_CURSOR, 0);
            pKingdom.data.set(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_VISITED_CITIES, "");
        }

        private static void CancelPreparationRecruitment(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            bool runtimePlanPresent = PreparationRecruitmentPlans.Remove(
                pKingdom.id);
            pKingdom.data.get(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_IN_PROGRESS,
                out bool persistedInProgress, false);
            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_PREPARATION_MONTH,
                out int persistedMonthKey, int.MinValue);
            if (!TemporaryLevyRules.ShouldClearPreparationState(
                    runtimePlanPresent, persistedInProgress,
                    persistedMonthKey)) return;
            pKingdom.data.set(LineageKeys.TEMPORARY_LEVY_PREPARATION_MONTH,
                int.MinValue);
            pKingdom.data.set(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_IN_PROGRESS, false);
            pKingdom.data.set(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_CURRENT_CITY, -1L);
            pKingdom.data.set(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_FRONTIER_CURSOR, 0);
            pKingdom.data.set(
                LineageKeys.TEMPORARY_LEVY_PREPARATION_VISITED_CITIES, "");
        }

        private static void RestoreVisitedPreparationCities(
            PreparationRecruitmentPlan pPlan, string pSerializedIds)
        {
            if (pPlan == null || string.IsNullOrEmpty(pSerializedIds)) return;
            string[] ids = pSerializedIds.Split(
                new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < ids.Length; i++)
            {
                if (long.TryParse(ids[i], out long cityId) && cityId >= 0L)
                    pPlan.VisitedCityIds.Add(cityId);
            }
        }

        private static string SerializeVisitedPreparationCities(
            HashSet<long> pCityIds)
        {
            if (pCityIds == null || pCityIds.Count == 0) return "";
            var orderedIds = new List<long>(pCityIds);
            orderedIds.Sort();
            return string.Join(",", orderedIds);
        }

        private static void ProcessRecruitmentBatch(long pKingdomId, int pYear)
        {
            if (!RecruitmentPlans.TryGetValue(pKingdomId, out RecruitmentYearPlan plan) ||
                plan.Year != pYear) return;
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (kingdom?.data == null || kingdom.isRekt())
            {
                RecruitmentPlans.Remove(pKingdomId);
                ScheduleDemobilization(pKingdomId);
                return;
            }
            if (AutonomousRestorationService.IsActiveCampaignKingdom(kingdom))
            {
                RecruitmentPlans.Remove(pKingdomId);
                ScheduleDemobilization(pKingdomId);
                return;
            }
            bool emergencyActive = MilitaryEmergencyService.HasAny(kingdom);
            if (!emergencyActive)
            {
                ScheduleDemobilization(pKingdomId);
                return;
            }
            if (!TemporaryLevyRules.ShouldRunRecruitmentWorkItem(true,
                    plan.CompletedWorkItems, plan.ScannedCandidates, plan.RecruitedActors))
                return;

            bool activeNotice = WarNoticeService.HasActiveNotice(kingdom);
            bool preferredTargetsReady = ArmyDeploymentService.
                TryGetPreferredLevyCityCount(kingdom,
                    out int preferredCityCount);
            if (TemporaryLevyRules.ShouldWaitForPreparationTargets(
                    activeNotice, preferredTargetsReady))
            {
                ScheduleRecruitmentBatch(plan);
                return;
            }

            int preparationOrdinal = TemporaryLevyRules.
                ResolvePreparationCityOrdinal(plan.CompletedWorkItems,
                    preferredCityCount);
            City city;
            if (preparationOrdinal >= 0 && ArmyDeploymentService.
                    TryGetPreferredLevyCity(kingdom, preparationOrdinal,
                        out city))
                plan.PreferredCityCursor = preparationOrdinal + 1;
            else
                city = NextCursorCity(kingdom);

            bool establishmentReady = StandingArmyService.
                RequestEstablishment(kingdom, city,
                    out ArmyRecruitmentDisposition disposition,
                    out Army establishmentArmy);
            if (!establishmentReady)
            {
                ScheduleRecruitmentBatch(plan);
                return;
            }
            if (disposition == ArmyRecruitmentDisposition.Reject)
            {
                RecruitmentPlans.Remove(pKingdomId);
                return;
            }

            var candidates = new List<Actor>(
                TemporaryLevyRules.MaxRecruitsPerWorkItem);
            int scanned = CityReservePoolService.TryConsumeForMobilization(
                kingdom, city, TemporaryLevyRules.MaxRecruitsPerWorkItem,
                establishmentArmy,
                allowArmyCreation:
                    disposition == ArmyRecruitmentDisposition.Create,
                candidates,
                out bool confirmedExhausted);
            int recruited = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                Actor actor = candidates[i];
                City donorCity = actor?.city;
                if (donorCity?.data == null ||
                    !Enlist(kingdom, donorCity, actor, disposition,
                        ref establishmentArmy)) continue;
                recruited++;
            }
            int availableAfterReturn = CityReservePoolService.
                RestoreRejectedCandidates(kingdom, city,
                    establishmentArmy, candidates);
            confirmedExhausted = CityReservePoolRules.
                ResolveConfirmedExhaustionAfterReturn(confirmedExhausted,
                    availableAfterReturn);
            plan.CompletedWorkItems++;
            plan.ScannedCandidates += scanned;
            plan.RecruitedActors += recruited;
            PersistRecruitmentPlan(kingdom, plan);

            if (confirmedExhausted)
            {
                RecruitmentPlans.Remove(pKingdomId);
                return;
            }

            if (TemporaryLevyRules.ShouldRunRecruitmentWorkItem(
                    MilitaryEmergencyService.HasAny(kingdom), plan.CompletedWorkItems,
                    plan.ScannedCandidates, plan.RecruitedActors))
            {
                ScheduleRecruitmentBatch(plan);
            }
        }

        private static City NextCursorCity(Kingdom pKingdom)
        {
            int cityCount = pKingdom?.cities?.Count ?? 0;
            if (cityCount <= 0) return null;
            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_CITY_CURSOR, out int cursor, 0);
            cursor = PositiveModulo(cursor, cityCount);
            City city = pKingdom.cities[cursor];
            pKingdom.data.set(LineageKeys.TEMPORARY_LEVY_CITY_CURSOR,
                PositiveModulo(cursor + 1, cityCount));
            return city;
        }

        private static bool ScanCity(Kingdom pKingdom, City pCity,
            ref int pScanned, ref int pRecruited, int pRecruitLimit,
            bool pForceEstablishment = false, Army pTargetArmy = null)
        {
            return ScanCity(pKingdom, pCity, ref pScanned,
                ref pRecruited, pRecruitLimit, out bool ignored,
                pForceEstablishment, pTargetArmy);
        }

        private static bool ScanCity(Kingdom pKingdom, City pCity,
            ref int pScanned, ref int pRecruited, int pRecruitLimit,
            out bool pCityScanComplete,
            bool pForceEstablishment = false, Army pTargetArmy = null)
        {
            return ScanCity(pKingdom, pCity, ref pScanned,
                ref pRecruited, pRecruitLimit, out pCityScanComplete,
                out _, pForceEstablishment, pTargetArmy);
        }

        private static bool ScanCity(Kingdom pKingdom, City pCity,
            ref int pScanned, ref int pRecruited, int pRecruitLimit,
            out bool pCityScanComplete,
            out RecruitmentScanSummary pScanSummary,
            bool pForceEstablishment = false, Army pTargetArmy = null)
        {
            pCityScanComplete = true;
            pScanSummary = default;
            if (pCity?.data == null || pCity.isRekt() ||
                pCity.kingdom != pKingdom) return true;
            if (!OccupiedCitySupplyService.CanProvideToRealm(
                    pCity, pKingdom)) return true;
            int population;
            try { population = pCity.getPopulationPeople(); }
            catch { return true; }
            int recruitLimit = WartimeRecruitmentPopulationRules.RecruitmentCapacity(
                population, Math.Min(
                    TemporaryLevyRules.MaxRecruitsPerWorkItem,
                    Math.Max(0, pRecruitLimit)));
            if (recruitLimit <= 0) return true;
            int ordinaryMilitary = StandingArmyService.CountOrdinaryMilitary(pCity);
            int effectiveSlots = MandateMilitaryPhaseService.
                EffectiveWarriorSlots(pKingdom, pCity.status.warrior_slots);
            if (pForceEstablishment)
                effectiveSlots = TemporaryLevyRules.
                    ForcedEstablishmentSlotLimit(ordinaryMilitary,
                        effectiveSlots, recruitLimit);
            if (effectiveSlots <= 0 || ordinaryMilitary >= effectiveSlots)
                return true;
            ArmyRecruitmentDisposition disposition;
            Army establishmentArmy;
            if (pTargetArmy?.data != null)
            {
                disposition = ArmyRecruitmentDisposition.Replenish;
                establishmentArmy = pTargetArmy;
            }
            else if (!StandingArmyService.RequestEstablishment(pKingdom,
                    pCity, out disposition, out establishmentArmy))
                return false;
            if (disposition == ArmyRecruitmentDisposition.Reject)
                return true;
            pCity.data.get(LineageKeys.TEMPORARY_LEVY_ACTOR_CURSOR, out int cursor, 0);
            if (cursor < 0) cursor = 0;

            int unitCount = pCity.units.Count;
            if (cursor >= unitCount) cursor = 0;
            int available = Math.Min(unitCount - cursor,
                TemporaryLevyRules.MaxCandidatesPerWorkItem);
            int localLimit = Math.Max(0, available);
            int localScanned = 0;
            var orphanWarriors = new List<Actor>();
            var slaveCandidates = new List<Actor>();
            var reserveCandidates = new List<Actor>();
            var civilianCandidates = new List<Actor>();
            var scanSummary = default(RecruitmentScanSummary);
            for (int i = 0; i < localLimit; i++)
            {
                if (pScanned >= TemporaryLevyRules.MaxCandidatesPerWorkItem ||
                    pRecruited >= recruitLimit ||
                    ordinaryMilitary >= effectiveSlots) break;

                Actor actor = pCity.units[cursor + i];
                pScanned++;
                localScanned++;
                if (IsRecoverableOrphanWarrior(pKingdom, pCity, actor))
                {
                    orphanWarriors.Add(actor);
                    scanSummary.Viable++;
                    continue;
                }
                if (actor?.isWarrior() == true)
                {
                    scanSummary.AlreadyWarrior++;
                    continue;
                }
                if (SlaveService.IsSlave(actor))
                {
                    if (CanEnlist(pKingdom, pCity, actor,
                            ordinaryMilitary, effectiveSlots,
                            out RecruitmentCandidateRejection slaveRejection,
                            pAllowSlave: true))
                    {
                        slaveCandidates.Add(actor);
                        scanSummary.Viable++;
                    }
                    else scanSummary.RecordRejection(slaveRejection);
                    continue;
                }
                if (SlaveService.IsRetiredSoldier(actor))
                {
                    if (CanEnlist(pKingdom, pCity, actor,
                            ordinaryMilitary, effectiveSlots,
                            out RecruitmentCandidateRejection reserveRejection,
                            pReserveRecallOnly: true))
                    {
                        reserveCandidates.Add(actor);
                        scanSummary.Viable++;
                    }
                    else scanSummary.RecordRejection(reserveRejection);
                    continue;
                }
                if (!CanEnlist(pKingdom, pCity, actor, ordinaryMilitary,
                        effectiveSlots,
                        out RecruitmentCandidateRejection civilianRejection))
                {
                    scanSummary.RecordRejection(civilianRejection);
                    continue;
                }
                civilianCandidates.Add(actor);
                scanSummary.Viable++;
            }
            for (int i = 0; i < orphanWarriors.Count; i++)
            {
                if (pRecruited >= recruitLimit) break;
                if (!RecoverOrphanWarrior(pKingdom, pCity,
                        orphanWarriors[i], disposition,
                        ref establishmentArmy))
                {
                    scanSummary.EnlistFailures++;
                    continue;
                }
                pRecruited++;
                ordinaryMilitary++;
            }
            for (int i = 0; i < slaveCandidates.Count; i++)
            {
                if (pRecruited >= recruitLimit ||
                    ordinaryMilitary >= effectiveSlots) break;
                Actor actor = slaveCandidates[i];
                if (!Enlist(pKingdom, pCity, actor, disposition,
                        ref establishmentArmy))
                {
                    scanSummary.EnlistFailures++;
                    continue;
                }
                pRecruited++;
                ordinaryMilitary++;
            }
            for (int i = 0; i < reserveCandidates.Count; i++)
            {
                if (pRecruited >= recruitLimit ||
                    ordinaryMilitary >= effectiveSlots) break;
                Actor actor = reserveCandidates[i];
                if (!Enlist(pKingdom, pCity, actor, disposition,
                        ref establishmentArmy))
                {
                    scanSummary.EnlistFailures++;
                    continue;
                }
                pRecruited++;
                ordinaryMilitary++;
            }
            for (int i = 0; i < civilianCandidates.Count; i++)
            {
                if (pRecruited >= recruitLimit ||
                    ordinaryMilitary >= effectiveSlots) break;
                Actor actor = civilianCandidates[i];
                if (!Enlist(pKingdom, pCity, actor, disposition,
                        ref establishmentArmy))
                {
                    scanSummary.EnlistFailures++;
                    continue;
                }
                pRecruited++;
                ordinaryMilitary++;
            }
            bool complete = cursor + localScanned >= unitCount;
            pCity.data.set(LineageKeys.TEMPORARY_LEVY_ACTOR_CURSOR,
                complete ? 0 : cursor + localScanned);
            pCityScanComplete = complete;
            pScanSummary = scanSummary;
            return true;
        }

        private static bool IsRecoverableOrphanWarrior(Kingdom pKingdom,
            City pCity, Actor pActor)
        {
            bool localResident = pActor?.data != null &&
                                 pActor.city == pCity &&
                                 pActor.kingdom == pKingdom;
            bool hasArmy = pActor?.army != null;
            bool allowSlave = SlaveService.IsSlave(pActor);
            bool protectedIdentity = !localResident ||
                IsProtectedIdentity(pKingdom, pActor, allowSlave);
            return TemporaryLevyRules.ShouldRecoverOrphanWarrior(
                MilitaryEmergencyService.HasAny(pKingdom),
                pActor?.isWarrior() == true, hasArmy, localResident,
                protectedIdentity, WartimeGarrisonService.IsActive(pActor));
        }

        private static bool RecoverOrphanWarrior(Kingdom pKingdom,
            City pCity, Actor pActor,
            ArmyRecruitmentDisposition pDisposition,
            ref Army pEstablishmentArmy)
        {
            if (!EnsureArmyMembership(pCity, pActor, pDisposition,
                    ref pEstablishmentArmy,
                    out bool createdStandingCadre)) return false;
            if (createdStandingCadre)
            {
                pActor.data.get(LineageKeys.MILITARY_BIOGRAPHY_ACTIVE,
                    out bool biographyActive, false);
                if (!biographyActive)
                {
                    pActor.data.set(LineageKeys.MILITARY_BIOGRAPHY_ACTIVE,
                        true);
                    pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME,
                        (float)LineageService.CurTime());
                    ChronicleEvents.OnEnlisted(pActor);
                }
            }
            NotifyArmyRosterChanged(pKingdom, pActor.army,
                pRosterExpanded: true);
            return true;
        }

        private static void ScheduleCasualtyReinforcement(
            CasualtyReinforcementPlan pPlan)
        {
            if (pPlan == null || RecoverableDemand(pPlan) <= 0) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "levy_casualty_reinforce", pPlan.KingdomId),
                DeferredWorkClass.CriticalRuntime,
                () => ProcessCasualtyReinforcementWorkItem(
                    pPlan.KingdomId));
        }

        private static void ProcessCasualtyReinforcementWorkItem(
            long pKingdomId)
        {
            if (!CasualtyReinforcementPlans.TryGetValue(pKingdomId,
                    out CasualtyReinforcementPlan plan)) return;
            int directedDemand = DirectedDemand(plan);
            if (directedDemand <= 0)
            {
                ProcessCasualtyReinforcement(pKingdomId,
                    pScheduleContinuation: true);
                return;
            }

            int batchBudget = TemporaryLevyRules.
                ImmediateDirectedRecoveryBatchBudget(directedDemand);
            int batchesProcessed = 0;
            while (CasualtyReinforcementPlans.TryGetValue(pKingdomId,
                       out plan) && plan.TargetDemandsByArmy.Count > 0)
            {
                Kingdom kingdom = ResolveKingdom(pKingdomId);
                bool coverageComplete = DirectedDemand(plan) <= 0;
                if (coverageComplete)
                {
                    ProcessCasualtyReinforcement(pKingdomId,
                        pScheduleContinuation: false);
                    break;
                }
                directedDemand = DirectedDemand(plan);
                if (!TemporaryLevyRules.
                        ShouldContinueImmediateDirectedRecovery(
                            targetArmyActive: kingdom?.data != null &&
                                !kingdom.isRekt(),
                            pendingDemand: directedDemand,
                            candidateCoverageComplete: false,
                            batchesProcessed: batchesProcessed,
                            batchBudget: batchBudget))
                    break;
                ProcessCasualtyReinforcement(pKingdomId,
                    pScheduleContinuation: false);
                batchesProcessed++;
            }

            if (CasualtyReinforcementPlans.TryGetValue(pKingdomId,
                    out plan))
                ScheduleCasualtyReinforcement(plan);
        }

        private static void ProcessCasualtyReinforcement(long pKingdomId,
            bool pScheduleContinuation)
        {
            if (!CasualtyReinforcementPlans.TryGetValue(pKingdomId,
                    out CasualtyReinforcementPlan plan)) return;
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            bool emergency = kingdom?.data != null && !kingdom.isRekt() &&
                             MilitaryEmergencyService.HasAny(kingdom);
            int recoverableDemand = RecoverableDemand(plan);
            if (!TemporaryLevyRules.
                    ShouldContinueCasualtyRecoveryUntilCoverage(
                        emergency, recoverableDemand,
                        recoverableDemand <= 0))
            {
                if (!emergency || PendingDemand(plan) <= 0)
                    CasualtyReinforcementPlans.Remove(pKingdomId);
                return;
            }

            Army targetArmy = TryResolveTargetArmy(kingdom, plan,
                out int targetDemand);
            if (targetArmy?.data != null)
            {
                targetDemand = ApprovedTargetShortage(kingdom, targetArmy);
                if (targetDemand > 0)
                    plan.TargetDemandsByArmy[targetArmy.id] = targetDemand;
                else
                    plan.TargetDemandsByArmy.Remove(targetArmy.id);
            }
            int demand = targetArmy?.data != null
                ? targetDemand
                : plan.PendingDemand;
            if (demand <= 0)
            {
                if (PendingDemand(plan) <= 0)
                    CasualtyReinforcementPlans.Remove(pKingdomId);
                else if (pScheduleContinuation)
                    ScheduleCasualtyReinforcement(plan);
                return;
            }
            bool directedReplenishment = targetArmy?.data != null;
            City city = directedReplenishment &&
                        TemporaryLevyRules.
                            ShouldUseDirectedReplenishmentAnchor(
                                plan.CompletedWorkItems)
                ? AWArmyService.FindAnchorCity(targetArmy)
                : ResolveCity(plan.CurrentCityId);
            if (!directedReplenishment &&
                !IsCasualtyCandidateCity(kingdom, plan, city))
            {
                plan.CurrentCityId = -1L;
                city = plan.CompletedWorkItems == 0
                    ? ResolveCity(plan.PreferredCityId)
                    : null;
            }
            if (!directedReplenishment &&
                !IsCasualtyCandidateCity(kingdom, plan, city))
            {
                if (!ArmyDeploymentService.TryGetPreferredLevyCity(
                        kingdom, plan.CompletedWorkItems, out city))
                    city = null;
            }
            if (!directedReplenishment &&
                !IsCasualtyCandidateCity(kingdom, plan, city))
                city = NextUnexhaustedCasualtyCity(kingdom, plan);
            if (!IsCasualtyCandidateCity(kingdom, plan, city))
            {
                if (directedReplenishment)
                    plan.ExhaustedTargetArmyIds.Add(targetArmy.id);
                else
                    plan.RealmReserveExhausted = true;
                if (pScheduleContinuation)
                    ScheduleCasualtyReinforcement(plan);
                return;
            }

            ArmyRecruitmentDisposition disposition;
            Army establishmentArmy = targetArmy;
            bool establishmentReady;
            if (targetArmy?.data != null)
            {
                disposition = ArmyRecruitmentDisposition.Replenish;
                establishmentReady = true;
            }
            else
            {
                establishmentReady = StandingArmyService.
                    RequestEstablishment(kingdom, city,
                        out disposition, out establishmentArmy);
            }
            if (establishmentReady &&
                disposition == ArmyRecruitmentDisposition.Reject)
            {
                CasualtyReinforcementPlans.Remove(pKingdomId);
                return;
            }
            var candidates = new List<Actor>(
                TemporaryLevyRules.CasualtyReinforcementBatchLimit(
                    demand));
            bool confirmedExhausted = false;
            int scanned = 0;
            if (establishmentReady)
                scanned = CityReservePoolService.TryConsumeForMobilization(
                    kingdom, city,
                    TemporaryLevyRules.CasualtyReinforcementBatchLimit(
                        demand), establishmentArmy,
                    allowArmyCreation:
                        disposition == ArmyRecruitmentDisposition.Create,
                    candidates,
                    out confirmedExhausted);
            int recruited = 0;
            var scanSummary = default(RecruitmentScanSummary);
            scanSummary.Viable = candidates.Count;
            if (establishmentReady && targetArmy?.data != null)
                recruited = EnlistReserveActors(kingdom, city,
                    targetArmy, candidates,
                    preparationRecruitment: false);
            else
                for (int i = 0; i < candidates.Count; i++)
                {
                    Actor actor = candidates[i];
                    City donorCity = actor?.city;
                    if (donorCity?.data == null ||
                        !Enlist(kingdom, donorCity, actor, disposition,
                            ref establishmentArmy)) continue;
                    recruited++;
                }
            int availableAfterReturn = CityReservePoolService.
                RestoreRejectedCandidates(kingdom, city,
                    establishmentArmy, candidates);
            confirmedExhausted = CityReservePoolRules.
                ResolveConfirmedExhaustionAfterReturn(confirmedExhausted,
                    availableAfterReturn);
            scanSummary.EnlistFailures = Math.Max(0,
                candidates.Count - recruited);
            LogRecoveryBatch(kingdom, plan, targetArmy, city, demand,
                scanned, recruited, establishmentReady, scanSummary);
            if (!establishmentReady)
            {
                if (pScheduleContinuation)
                    ScheduleCasualtyReinforcement(plan);
                return;
            }
            plan.CurrentCityId = -1L;
            if (targetArmy?.data != null)
            {
                int remaining = Math.Max(0, targetDemand - recruited);
                if (remaining > 0)
                    plan.TargetDemandsByArmy[targetArmy.id] = remaining;
                else
                {
                    plan.TargetDemandsByArmy.Remove(targetArmy.id);
                    plan.ExhaustedTargetArmyIds.Remove(targetArmy.id);
                }
            }
            else
                plan.PendingDemand = Math.Max(0,
                    plan.PendingDemand - recruited);
            plan.CompletedWorkItems++;
            if (confirmedExhausted && PendingDemand(plan) > 0)
            {
                if (directedReplenishment)
                    plan.ExhaustedTargetArmyIds.Add(targetArmy.id);
                else
                    plan.RealmReserveExhausted = true;
            }
            recoverableDemand = RecoverableDemand(plan);
            if (TemporaryLevyRules.
                    ShouldContinueCasualtyRecoveryUntilCoverage(
                        MilitaryEmergencyService.HasAny(kingdom),
                        recoverableDemand, recoverableDemand <= 0))
            {
                if (pScheduleContinuation)
                    ScheduleCasualtyReinforcement(plan);
                return;
            }
            CasualtyReinforcementPlans.Remove(pKingdomId);
        }

        private static int ApprovedTargetShortage(Kingdom pKingdom,
            Army pTargetArmy)
        {
            int living = 0;
            try { living = Math.Max(0, pTargetArmy?.countUnits() ?? 0); }
            catch { }
            int approved = CityArmyReinforcementService.ApprovedTarget(
                pTargetArmy, pKingdom);
            return CityArmyReinforcementRules.Shortage(living, approved);
        }

        private static int PreparationTargetStrength(Kingdom pKingdom,
            City pCity)
        {
            if (pKingdom?.data == null || pCity?.data == null ||
                pCity.kingdom != pKingdom) return 0;
            int population;
            int slots;
            try { population = Math.Max(0, pCity.getPopulationPeople()); }
            catch { population = 0; }
            try { slots = Math.Max(0, pCity.status.warrior_slots); }
            catch { slots = 0; }
            slots = MandateMilitaryPhaseService.EffectiveWarriorSlots(
                pKingdom, slots);
            return CityArmyReinforcementRules.CityCapacity(population,
                slots);
        }

        private static int SafeArmyCount(Army pArmy)
        {
            try { return Math.Max(0, pArmy?.countUnits() ?? 0); }
            catch { return 0; }
        }

        private static int EnlistPreparationActors(Kingdom pKingdom,
            City pSourceCity, ArmyRecruitmentDisposition pDisposition,
            ref Army pTargetArmy, IReadOnlyList<Actor> pCandidates,
            ref bool pConfirmedExhausted)
        {
            if (pKingdom?.data == null || pSourceCity?.data == null ||
                pCandidates == null) return 0;
            int recruited = 0;
            if (CityReservePoolService.ResolveMobilizationPhase(pKingdom) ==
                ArmyMobilizationPhase.Notice)
                for (int i = 0; i < pCandidates.Count; i++)
                {
                    Actor actor = pCandidates[i];
                    ArmyRecruitmentDisposition disposition =
                        pTargetArmy?.data == null
                            ? pDisposition
                            : ArmyRecruitmentDisposition.Replenish;
                    if (disposition == ArmyRecruitmentDisposition.Reject ||
                        !Enlist(pKingdom, pSourceCity, actor, disposition,
                            ref pTargetArmy,
                            pTrackReplenishmentArrival: false)) continue;
                    recruited++;
                }
            int availableAfterReturn = CityReservePoolService.
                RestoreRejectedCandidates(pKingdom, pSourceCity,
                    pTargetArmy, pCandidates);
            pConfirmedExhausted = CityReservePoolRules.
                ResolveConfirmedExhaustionAfterReturn(pConfirmedExhausted,
                    availableAfterReturn);
            return recruited;
        }

        internal static int EnlistReserveActors(Kingdom kingdom, City source,
            Army targetArmy, IReadOnlyList<Actor> candidates,
            bool preparationRecruitment,
            bool pTrackReplenishmentArrival = true)
        {
            if (kingdom?.data == null || source?.data == null ||
                candidates == null) return 0;
            int recruited = 0;
            Army target = targetArmy;
            bool canEnlist = targetArmy?.data != null &&
                !AWArmyService.IsSpecialArmy(targetArmy) &&
                AWArmyService.GetIntendedKingdom(targetArmy) == kingdom &&
                (!preparationRecruitment ||
                 CityReservePoolService.ResolveMobilizationPhase(kingdom) ==
                 ArmyMobilizationPhase.Notice);
            if (canEnlist)
                for (int i = 0; i < candidates.Count; i++)
                {
                    Actor actor = candidates[i];
                    City donorCity = actor?.city ?? source;
                    if (donorCity?.data == null ||
                        !Enlist(kingdom, donorCity, actor,
                            ArmyRecruitmentDisposition.Replenish, ref target,
                            pTrackReplenishmentArrival)) continue;
                    recruited++;
                }
            return recruited;
        }

        private static int DirectedDemand(CasualtyReinforcementPlan pPlan)
        {
            if (pPlan == null) return 0;
            long total = 0L;
            foreach (KeyValuePair<long, int> entry in
                     pPlan.TargetDemandsByArmy)
            {
                if (TemporaryLevyRules.IsReplenishmentDemandExhausted(
                        directedReplenishment: true,
                        realmReserveExhausted:
                            pPlan.RealmReserveExhausted,
                        targetReserveExhausted:
                            pPlan.ExhaustedTargetArmyIds.Contains(entry.Key)))
                    continue;
                total += Math.Max(0, entry.Value);
                if (total >= int.MaxValue) return int.MaxValue;
            }
            return (int)total;
        }

        private static int RecoverableDemand(CasualtyReinforcementPlan pPlan)
        {
            if (pPlan == null) return 0;
            long total = TemporaryLevyRules.IsReplenishmentDemandExhausted(
                directedReplenishment: false,
                realmReserveExhausted: pPlan.RealmReserveExhausted,
                targetReserveExhausted: false)
                ? 0L
                : Math.Max(0, pPlan.PendingDemand);
            total += DirectedDemand(pPlan);
            return total >= int.MaxValue ? int.MaxValue : (int)total;
        }

        private static bool IsCasualtyCandidateCity(Kingdom pKingdom,
            CasualtyReinforcementPlan pPlan, City pCity)
        {
            if (pKingdom?.data == null || pPlan == null ||
                pCity?.data == null || pCity.isRekt() ||
                pCity.kingdom != pKingdom ||
                pPlan.ExhaustedCandidateCityIds.Contains(pCity.id))
                return false;
            int population;
            int ordinaryMilitary;
            int normalWarriorSlots;
            try
            {
                population = pCity.getPopulationPeople();
                ordinaryMilitary = StandingArmyService.CountOrdinaryMilitary(
                    pCity);
                normalWarriorSlots = MandateMilitaryPhaseService.
                    EffectiveWarriorSlots(pKingdom,
                        pCity.status?.warrior_slots ?? 0);
            }
            catch { return false; }
            return TemporaryLevyRules.CanSupplyCasualtyRecoveryCity(
                population, ordinaryMilitary, normalWarriorSlots,
                pPlan.ForceEstablishment);
        }

        private static City NextUnexhaustedCasualtyCity(Kingdom pKingdom,
            CasualtyReinforcementPlan pPlan)
        {
            int cityCount = pKingdom?.cities?.Count ?? 0;
            for (int i = 0; i < cityCount; i++)
            {
                City city = NextCursorCity(pKingdom);
                if (IsCasualtyCandidateCity(pKingdom, pPlan, city))
                    return city;
            }
            return null;
        }

        private static bool HasCompletedCasualtyCandidateCoverage(
            Kingdom pKingdom, CasualtyReinforcementPlan pPlan)
        {
            if (pKingdom?.data == null || pPlan == null) return true;
            int cityCount = pKingdom.cities?.Count ?? 0;
            if (cityCount <= 0) return true;
            for (int i = 0; i < cityCount; i++)
            {
                City city = null;
                try { city = pKingdom.cities[i]; }
                catch { }
                if (IsCasualtyCandidateCity(pKingdom, pPlan, city))
                    return false;
            }
            return true;
        }

        private static void LogRecoveryBatch(Kingdom pKingdom,
            CasualtyReinforcementPlan pPlan, Army pTargetArmy, City pCity,
            int pDemand, int pScanned, int pRecruited,
            bool pEstablishmentReady, RecruitmentScanSummary pScanSummary)
        {
            int pendingDemand = PendingDemand(pPlan);
            if (!TemporaryLevyDiagnosticRules.ShouldWriteRecoveryDiagnostic(
                    AWPerformanceSettings.ArmyRtsDiagnosticsEnabled,
                    pendingDemand)) return;
            int population = 0;
            int ordinaryMilitary = 0;
            int slots = 0;
            bool supplyEligible = false;
            if (pCity?.data != null)
            {
                try { population = Math.Max(0, pCity.getPopulationPeople()); }
                catch { }
                try
                {
                    ordinaryMilitary = StandingArmyService.
                        CountOrdinaryMilitary(pCity);
                    slots = Math.Max(0, MandateMilitaryPhaseService.
                        EffectiveWarriorSlots(pKingdom,
                            pCity.status?.warrior_slots ?? 0));
                    supplyEligible = OccupiedCitySupplyService.
                        CanProvideToRealm(pCity, pKingdom);
                }
                catch { }
            }
            int capacity = WartimeRecruitmentPopulationRules.
                RecruitmentCapacity(population,
                    TemporaryLevyRules.CasualtyReinforcementBatchLimit(
                        pDemand));
            int targetUnits = 0;
            try { targetUnits = Math.Max(0, pTargetArmy?.countUnits() ?? 0); }
            catch { }
            AncientWarfare3.ModClass.LogInfo("[AW3 RTS levy] kingdom=" +
                (pKingdom?.id ?? -1L) + " demand=" + Math.Max(0, pDemand) +
                " pending=" + pendingDemand + " target_army=" +
                (pTargetArmy?.id ?? -1L) + " target_units=" + targetUnits +
                " city=" + (pCity?.id ?? -1L) + " supply=" +
                supplyEligible + " population=" + population +
                " capacity=" + capacity + " ordinary=" +
                ordinaryMilitary + " slots=" + slots + " scanned=" +
                Math.Max(0, pScanned) + " recruited=" +
                Math.Max(0, pRecruited) + " establishment_ready=" +
                pEstablishmentReady + " work_items=" +
                Math.Max(0, pPlan?.CompletedWorkItems ?? 0) + " " +
                TemporaryLevyDiagnosticRules.RecoveryCandidateBreakdown(
                    pScanSummary.AlreadyWarrior, pScanSummary.Ineligible,
                    pScanSummary.Viable, pScanSummary.EnlistFailures) + " " +
                TemporaryLevyDiagnosticRules.RecoveryIneligibilityBreakdown(
                    pScanSummary.NotResident, pScanSummary.NotLivingAdult,
                    pScanSummary.WrongProfession,
                    pScanSummary.ReservePolicy, pScanSummary.SlavePolicy,
                    pScanSummary.ProtectedIdentity,
                    pScanSummary.NativeEligibility, pScanSummary.AgeLimit,
                    pScanSummary.Capacity));
        }

        private static int PendingDemand(CasualtyReinforcementPlan pPlan)
        {
            if (pPlan == null) return 0;
            long total = Math.Max(0, pPlan.PendingDemand);
            foreach (int demand in pPlan.TargetDemandsByArmy.Values)
            {
                total += Math.Max(0, demand);
                if (total >= int.MaxValue) return int.MaxValue;
            }
            return (int)total;
        }

        private static Army TryResolveTargetArmy(Kingdom pKingdom,
            CasualtyReinforcementPlan pPlan, out int pDemand)
        {
            pDemand = 0;
            if (pKingdom?.data == null || pPlan == null ||
                pPlan.TargetDemandsByArmy.Count == 0) return null;
            var targetIds = new List<long>(pPlan.TargetDemandsByArmy.Keys);
            targetIds.Sort();
            int start = 0;
            while (start < targetIds.Count &&
                   targetIds[start] <= pPlan.TargetDemandCursor)
                start++;
            if (start >= targetIds.Count) start = 0;
            for (int offset = 0; offset < targetIds.Count; offset++)
            {
                long armyId = targetIds[(start + offset) % targetIds.Count];
                if (pPlan.ExhaustedTargetArmyIds.Contains(armyId))
                    continue;
                int demand = pPlan.TargetDemandsByArmy.TryGetValue(armyId,
                    out int pending) ? pending : 0;
                bool eligible = StandingArmyService.
                    TryResolveReplenishmentTarget(pKingdom, armyId,
                        out Army target);
                if (TemporaryLevyRules.
                    ShouldDirectReplenishmentToRequestedArmy(armyId,
                        eligible, demand))
                {
                    pPlan.TargetDemandCursor = armyId;
                    pDemand = demand;
                    return target;
                }
                pPlan.TargetDemandsByArmy.Remove(armyId);
                pPlan.ExhaustedTargetArmyIds.Remove(armyId);
                // An Army can become a non-alive shell after its final
                // casualty. Keep its demand alive so the same wartime
                // recovery plan establishes a replacement rather than
                // silently discarding all available reinforcements.
                pPlan.PendingDemand = TemporaryLevyRules.
                    MoveInvalidDirectedReplenishmentDemand(
                        pPlan.PendingDemand, demand);
                pPlan.ForceEstablishment |= TemporaryLevyRules.
                    ShouldForceEmergencyRecoverySlots(
                        forceEstablishment: false,
                        directedReplenishment: true);
            }
            return null;
        }

        private static void ScheduleCaptainRecovery(CaptainRecoveryPlan pPlan)
        {
            if (pPlan == null) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "levy_captain_recovery", pPlan.TargetArmyId),
                DeferredWorkClass.CriticalRuntime,
                () => ProcessCaptainRecovery(pPlan.TargetArmyId));
        }

        private static void ProcessCaptainRecovery(long pArmyId)
        {
            if (!CaptainRecoveryPlans.TryGetValue(pArmyId,
                    out CaptainRecoveryPlan plan)) return;
            Kingdom kingdom = ResolveKingdom(plan.KingdomId);
            Army army = ArmyStrategicIndexService.ResolveIndexedArmy(
                plan.TargetArmyId, plan.KingdomId);
            if (!IsCaptainRecoveryTargetValid(kingdom, army))
            {
                CaptainRecoveryPlans.Remove(pArmyId);
                return;
            }
            if (TryPromoteExistingLevyCaptain(army))
            {
                CaptainRecoveryPlans.Remove(pArmyId);
                return;
            }

            City city = ResolveCity(plan.PreferredCityId);
            if (city?.data == null || city.kingdom != kingdom || city.isRekt())
                city = null;

            int scanned = 0;
            int recruited = 0;
            bool reserveExhausted = ScanCityForCaptainRecovery(
                kingdom, city, army,
                ref scanned, ref recruited);
            plan.CompletedWorkItems++;
            if (HasOperationalCaptain(army))
            {
                CaptainRecoveryPlans.Remove(pArmyId);
                KingdomWarDirectorService.OnArmyChanged(kingdom);
                return;
            }
            if (reserveExhausted)
            {
                CaptainRecoveryPlans.Remove(pArmyId);
                return;
            }

            if (TemporaryLevyRules.ShouldContinueCasualtyReinforcement(
                    MilitaryEmergencyService.HasAny(kingdom),
                    pendingDemand: 1, plan.CompletedWorkItems))
            {
                ScheduleCaptainRecovery(plan);
                return;
            }
            CaptainRecoveryPlans.Remove(pArmyId);
        }

        private static bool IsCaptainRecoveryTargetValid(Kingdom pKingdom,
            Army pArmy)
        {
            if (pKingdom?.data == null || pArmy?.data == null ||
                pKingdom.isRekt() || !MilitaryEmergencyService.HasAny(pKingdom) ||
                AWArmyService.IsSpecialArmy(pArmy) || HasOperationalCaptain(pArmy))
                return false;
            try
            {
                return pArmy.getKingdom() == pKingdom && pArmy.countUnits() > 0;
            }
            catch { return false; }
        }

        private static bool CanPromoteExistingLevyCaptain(Kingdom pKingdom,
            Army pArmy, Actor pActor)
        {
            if (!IsTemporaryLevy(pActor) || pActor?.data == null ||
                pActor.kingdom != pKingdom || pActor.army != pArmy ||
                pActor.isRekt() || !pActor.isAlive() || !pActor.isAdult() ||
                !pActor.isWarrior() || !pActor.isKingdomCiv() ||
                pActor.isKing() || pActor.isCityLeader() ||
                SlaveService.IsSlave(pActor) ||
                WartimeGarrisonService.IsActive(pActor) ||
                TemporarySlaveVanguardService.IsMember(pActor) ||
                RoyalGuardService.IsRoyalGuard(pActor)) return false;
            return AWArmyService.CaptainMatchesArmyKingdom(pArmy, pActor) &&
                   HistoricalMasterVocationService.CanJoinArmy(pActor,
                       pArmy) &&
                   HistoricalMasterVocationService.CanEnter(pActor,
                       HistoricalMasterMilitaryContext.ArmyCaptain);
        }

        private static bool ScanCityForCaptainRecovery(Kingdom pKingdom,
            City pCity, Army pTargetArmy, ref int pScanned,
            ref int pRecruited)
        {
            if (pKingdom?.data == null || pTargetArmy?.data == null)
                return false;
            var candidates = new List<Actor>(1);
            pScanned += CityReservePoolService.TryConsumeFromSourceCity(
                pKingdom, pCity, 1, pTargetArmy, candidates,
                out bool confirmedExhausted);
            if (candidates.Count == 0) return confirmedExhausted;
            Army target = pTargetArmy;
            Actor candidate = candidates[0];
            City donorCity = candidate?.city;
            if (donorCity?.data != null &&
                Enlist(pKingdom, donorCity, candidate,
                    ArmyRecruitmentDisposition.Replenish, ref target))
                pRecruited++;
            int availableAfterReturn = CityReservePoolService.
                RestoreRejectedCandidates(pKingdom, pCity,
                    target, candidates);
            return CityReservePoolRules.
                ResolveConfirmedExhaustionAfterReturn(confirmedExhausted,
                    availableAfterReturn);
        }

        private static void RemoveCaptainRecoveryPlansForKingdom(
            long pKingdomId)
        {
            var remove = new List<long>();
            foreach (KeyValuePair<long, CaptainRecoveryPlan> pair in
                     CaptainRecoveryPlans)
                if (pair.Value?.KingdomId == pKingdomId)
                    remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++)
                CaptainRecoveryPlans.Remove(remove[i]);
        }

        private static bool CanEnlist(Kingdom pKingdom, City pCity, Actor pActor,
            int pOrdinaryMilitary, int pEffectiveSlots,
            out RecruitmentCandidateRejection pRejection,
            bool pReserveRecallOnly = false,
            bool pAllowSlave = false)
        {
            pRejection = RecruitmentCandidateRejection.None;
            if (pActor?.data == null || pActor.city != pCity ||
                pActor.kingdom != pKingdom)
            {
                pRejection = RecruitmentCandidateRejection.NotResident;
                return false;
            }
            if (pActor.isRekt() || !pActor.isAlive() || !pActor.isAdult())
            {
                pRejection = RecruitmentCandidateRejection.NotLivingAdult;
                return false;
            }
            if (pActor.asset?.is_boat == true ||
                !pActor.isProfession(UnitProfession.Unit))
            {
                pRejection = RecruitmentCandidateRejection.WrongProfession;
                return false;
            }
            bool retired = SlaveService.IsRetiredSoldier(pActor);
            if (pReserveRecallOnly)
            {
                if (!SoldierRetirementRules.CanRecallReserve(
                        MilitaryEmergencyService.HasAny(pKingdom), retired,
                        pActor.getAge(),
                        TemporaryLevyRules.MaximumEnlistmentAge))
                {
                    pRejection = RecruitmentCandidateRejection.ReservePolicy;
                    return false;
                }
            }
            else if (retired)
            {
                pRejection = RecruitmentCandidateRejection.ReservePolicy;
                return false;
            }
            if (!pAllowSlave && SlaveService.IsSlave(pActor))
            {
                pRejection = RecruitmentCandidateRejection.SlavePolicy;
                return false;
            }
            bool protectedIdentity = IsProtectedIdentity(pKingdom, pActor,
                pAllowSlave);
            if (protectedIdentity)
            {
                pRejection = RecruitmentCandidateRejection.ProtectedIdentity;
                return false;
            }
            bool originalEligible;
            using (MilitaryRecruitmentScope.Open(MilitaryRecruitmentKind.TemporaryLevy))
                originalEligible = pCity.checkCanMakeWarrior(pActor);
            if (!originalEligible)
            {
                pRejection = RecruitmentCandidateRejection.NativeEligibility;
                return false;
            }
            float age = pActor.getAge();
            if (TemporaryLevyRules.CanEnlist(originalEligible,
                    protectedIdentity, age, pOrdinaryMilitary,
                    pEffectiveSlots)) return true;
            pRejection = !(age < TemporaryLevyRules.MaximumEnlistmentAge)
                ? RecruitmentCandidateRejection.AgeLimit
                : RecruitmentCandidateRejection.Capacity;
            return false;
        }

        private static bool IsProtectedIdentity(Kingdom pKingdom, Actor pActor,
            bool pAllowSlave)
        {
            if (pActor.isKing() || pActor.isCityLeader() || HeirService.IsCurrentHeir(pKingdom, pActor))
                return true;
            if (GeneralService.IsActiveGeneralFast(pActor) || RoyalGuardService.IsRoyalGuard(pActor) ||
                RoyalAsylumService.IsActive(pActor) ||
                (!pAllowSlave && SlaveService.IsSlave(pActor))) return true;
            if (DynasticReproductionService
                .ShouldProtectFromOrdinaryMilitaryService(pActor)) return true;
            if (pActor.army != null && AWArmyService.IsSpecialArmy(pActor.army)) return true;
            if (!HistoricalMasterVocationService.CanEnter(pActor, HistoricalMasterMilitaryContext.OrdinaryWarrior))
                return true;
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            return !string.IsNullOrEmpty(office) && layer != CourtOfficeLayer.Military;
        }

        private static bool PassesOriginalEligibilityWithoutCapacity(
            City pCity, Actor pActor)
        {
            if (pCity?.data == null || pActor?.data == null ||
                pActor.isBaby()) return false;
            if (!pCity.hasCulture()) return true;
            if (pActor.isSexFemale() &&
                pCity.culture.hasTrait("conscription_male_only"))
                return false;
            return !pActor.isSexMale() ||
                   !pCity.culture.hasTrait("conscription_female_only");
        }

        private static bool Enlist(Kingdom pKingdom, City pCity,
            Actor pActor, ArmyRecruitmentDisposition pDisposition,
            ref Army pEstablishmentArmy,
            bool pTrackReplenishmentArrival = true)
        {
            using (MilitaryRecruitmentScope.Open(MilitaryRecruitmentKind.TemporaryLevy))
            {
                if (!pCity.checkCanMakeWarrior(pActor)) return false;
                pCity.makeWarrior(pActor);
            }
            if (!pActor.isWarrior()) return false;
            if (!EnsureArmyMembership(pCity, pActor, pDisposition,
                    ref pEstablishmentArmy,
                    out bool createdStandingCadre))
            {
                pActor.stopBeingWarrior();
                return false;
            }

            if (pDisposition == ArmyRecruitmentDisposition.Replenish &&
                pTrackReplenishmentArrival)
                ArmyRtsControllerService.TrackReplenishmentArrival(pActor,
                    pEstablishmentArmy);

            if (createdStandingCadre)
            {
                pActor.data.get(LineageKeys.MILITARY_BIOGRAPHY_ACTIVE,
                    out bool biographyActive, false);
                if (!biographyActive)
                {
                    pActor.data.set(LineageKeys.MILITARY_BIOGRAPHY_ACTIVE,
                        true);
                    pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME,
                        (float)LineageService.CurTime());
                    ChronicleEvents.OnEnlisted(pActor);
                }
                NotifyArmyRosterChanged(pKingdom, pActor.army,
                    pRosterExpanded: true);
                return true;
            }

            LevyPool pool = Pool(pKingdom.id);
            string noticeSignature = string.IsNullOrEmpty(pool.NoticeSignature)
                ? WarNoticeService.IncomingNoticeSignature(pKingdom)
                : pool.NoticeSignature;
            long warId = pool.ActiveWarId;
            if (warId < 0) MilitaryEmergencyService.TryGetActiveWarId(pKingdom, out warId);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY, true);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_KINGDOM_ID, pKingdom.id);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_NOTICE_SIGNATURE, noticeSignature ?? "");
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_ORIGINAL_CITY_ID, pCity.id);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_WAR_ID, warId);
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
            pool.ActorIds.Add(pActor.data.id);
            ActiveActorIds.Add(pActor.data.id);
            NotifyArmyRosterChanged(pKingdom, pActor.army,
                pRosterExpanded: true);
            RecordEnlistedDeferred(pActor.data.id, pKingdom.id, pCity.id);
            return true;
        }

        private static void NotifyArmyRosterChanged(Kingdom pKingdom,
            Army pArmy, bool pRosterExpanded)
        {
            if (pKingdom?.data == null || pArmy?.data == null) return;
            WarNoticeService.QueueArmyChanged(pKingdom, pArmy,
                pRosterExpanded);
            if (TemporaryLevyRules.ShouldNotifyWarDirectorOfRosterMutation(
                    memberAssigned: true,
                    emergencyActive: MilitaryEmergencyService.HasAny(
                        pKingdom)))
                KingdomWarDirectorService.QueueArmyChanged(pKingdom);
        }

        private static bool EnsureArmyMembership(City pCity, Actor pActor,
            ArmyRecruitmentDisposition pDisposition,
            ref Army pEstablishmentArmy,
            out bool pCreatedStandingCadre)
        {
            pCreatedStandingCadre = false;
            if (pCity?.data == null || pActor?.data == null ||
                !pActor.isWarrior()) return false;
            Army army = pEstablishmentArmy;
            bool createdArmy = false;
            try
            {
                if (army?.data != null &&
                    !CityReservePoolRules.MatchesSourceCity(pCity.id,
                        AWArmyService.GetAnchorCityId(army)))
                    return false;
                if (army?.data == null &&
                    pDisposition == ArmyRecruitmentDisposition.Create)
                {
                    if (ArmyFieldIndexService.TryGetCityArmy(pCity,
                            out Army canonical))
                        army = canonical;
                    else if (pCity.hasArmy())
                        army = AWArmyService.CreateDetachedArmy(
                            pCity.kingdom, pCity, pActor);
                    else
                        army = World.world?.armies?.newArmy(pActor, pCity);
                    createdArmy = army?.data != null;
                }
            }
            catch { }
            if (army?.data == null) return false;
            if (TemporaryLevyRules.ShouldAssignArmyAnchor(createdArmy,
                    AWArmyService.GetAnchorCityId(army)))
                army.data.set(LineageKeys.AW_ARMY_CITY_ID, pCity.id);
            AWArmyService.EnsureOrdinaryNativeName(army, pCity.kingdom,
                pCity);
            pEstablishmentArmy = army;
            if (pActor.army != army) AWArmyService.AddToArmy(pActor, army);
            bool memberAssigned = pActor.army == army;
            if (ArmyEstablishmentRules.ShouldPublishCompletedCreation(
                    createdArmy, memberAssigned))
                ArmyStrategicIndexService.OnArmyRegistered(army);
            Actor current = null;
            try { current = army.getCaptain(); }
            catch { }
            bool currentAlive = false;
            bool currentIsMember = false;
            try
            {
                currentAlive = current?.data != null &&
                               current.isAlive() && !current.isRekt();
                currentIsMember = current?.data != null &&
                                  current.army == army &&
                                  army.units != null &&
                                  army.units.Contains(current);
            }
            catch { }
            bool captainOperational = HasOperationalCaptain(army);
            if (TemporaryLevyRules.ShouldCommissionReplacementCaptain(
                    memberAssigned, captainOperational,
                    captainExists: current?.data != null,
                    captainAlive: currentAlive,
                    captainIsMember: currentIsMember))
            {
                if (current?.data != null)
                {
                    using (ArmyCaptainDisposalScope.Open(army))
                    {
                        try { army.setCaptain(null); }
                        catch { }
                    }
                }
                AWArmyService.SetCaptainIfChanged(army, pActor);
            }
            bool actorIsCaptain = false;
            try { actorIsCaptain = ReferenceEquals(army.getCaptain(), pActor); }
            catch { }
            pCreatedStandingCadre = memberAssigned && actorIsCaptain;
            return memberAssigned;
        }

        private static bool HasOperationalCaptain(Army pArmy)
        {
            Actor captain;
            try { captain = pArmy?.getCaptain(); }
            catch { return false; }
            return AWArmyService.IsCaptainLeaseEligible(pArmy, captain,
                requireMembership: true);
        }

        private static void ActivateWar(Kingdom pKingdom, long pWarId, string pNoticeSignature)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            LevyPool pool = Pool(pKingdom.id);
            pool.ActiveWarId = pWarId;
            if (!string.IsNullOrEmpty(pNoticeSignature)) pool.NoticeSignature = pNoticeSignature;
        }

        private static void ScheduleIfSafe(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || MilitaryEmergencyService.HasAny(pKingdom)) return;
            ScheduleDemobilization(pKingdom.id);
        }

        private static void ScheduleDemobilization(long pKingdomId)
        {
            if (pKingdomId < 0 || !Pools.TryGetValue(pKingdomId, out LevyPool pool)) return;
            if (pool.ActorIds.Count == 0)
            {
                Pools.Remove(pKingdomId);
                return;
            }
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("levy_demobilize", pKingdomId),
                DeferredWorkClass.Runtime,
                () => DemobilizeBatch(pKingdomId));
        }

        private static void DemobilizeBatch(long pKingdomId)
        {
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (!Pools.TryGetValue(pKingdomId, out LevyPool pool)) return;
            if (!TemporaryMilitaryServiceRules.ShouldDemobilize(
                    pool.ActorIds.Count > 0,
                    kingdom?.data != null &&
                    MilitaryEmergencyService.HasAny(kingdom))) return;

            long[] batch = pool.DemobilizationBuffer;
            int count = 0;
            foreach (long actorId in pool.ActorIds)
            {
                batch[count++] = actorId;
                if (count >= batch.Length) break;
            }

            for (int i = 0; i < count; i++)
            {
                long actorId = batch[i];
                Actor actor = ResolveActor(actorId);
                if (actor?.data != null) DemobilizeActor(actor, pKingdomId);
                pool.ActorIds.Remove(actorId);
            }

            if (pool.ActorIds.Count == 0)
            {
                Pools.Remove(pKingdomId);
                return;
            }
            ScheduleDemobilization(pKingdomId);
        }

        private static void DemobilizeActor(Actor pActor, long pMobilizingKingdomId)
        {
            pActor.data.get(LineageKeys.TEMPORARY_LEVY_KINGDOM_ID, out long recordedKingdomId, -1L);
            bool sameKingdom = pActor.kingdom?.id == pMobilizingKingdomId && recordedKingdomId == pMobilizingKingdomId;
            bool living = !pActor.isRekt() && pActor.isAlive();
            SyntheticLevyDisposition disposition =
                SyntheticLevyRules.ResolveDemobilization(
                    SyntheticLevyService.IsSynthetic(pActor), living,
                    GeneralService.GetMerit(pActor));
            if (disposition == SyntheticLevyDisposition.RemoveActor)
            {
                ClearFields(pActor);
                SyntheticLevyService.RemoveWithoutPersonalHistory(pActor);
                return;
            }
            if (disposition == SyntheticLevyDisposition.PromotePermanent)
            {
                ClearFields(pActor);
                SyntheticLevyService.Promote(pActor);
                return;
            }
            ClearFields(pActor);
            if (sameKingdom && living)
            {
                City destination = ResolveDemobilizationCity(pActor, pMobilizingKingdomId);
                if (destination?.data != null && pActor.city != destination)
                {
                    try { pActor.joinCity(destination); } catch { }
                }
                TemporaryMilitaryDemobilizationService.RestoreCivilian(
                    pActor);
                SlaveService.OnTemporaryLevyDemobilized(pActor);
                RecordDemobilizedDeferred(pActor.data.id, pMobilizingKingdomId,
                    destination?.id ?? pActor.city?.id ?? -1L);
            }
            else if (living)
            {
                TemporaryMilitaryDemobilizationService.RestoreCivilian(
                    pActor);
                SlaveService.OnTemporaryLevyDemobilized(pActor);
            }
        }

        private static City ResolveDemobilizationCity(Actor pActor, long pKingdomId)
        {
            pActor.data.get(LineageKeys.TEMPORARY_LEVY_ORIGINAL_CITY_ID, out long originalCityId, -1L);
            City original = ResolveCity(originalCityId);
            if (original?.data != null && !original.isRekt() && original.kingdom?.id == pKingdomId) return original;
            if (pActor.city?.data != null && !pActor.city.isRekt() && pActor.city.kingdom?.id == pKingdomId)
                return pActor.city;
            return ResolveKingdom(pKingdomId)?.capital;
        }

        private static void ClearFields(Actor pActor)
        {
            if (pActor?.data == null) return;
            ActiveActorIds.Remove(pActor.data.id);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY, false);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_NOTICE_SIGNATURE, "");
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_ORIGINAL_CITY_ID, -1L);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_WAR_ID, -1L);
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
        }

        private static bool HasPersistedFlag(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.TEMPORARY_LEVY, out bool active, false);
            return active;
        }

        private static LevyPool Pool(long pKingdomId)
        {
            if (!Pools.TryGetValue(pKingdomId, out LevyPool pool))
            {
                pool = new LevyPool();
                Pools[pKingdomId] = pool;
            }
            return pool;
        }

        private static void RecordEnlistedDeferred(long pActorId, long pKingdomId, long pCityId)
        {
            DeferredRuntimeWorkService.EnqueueOrdered(DeferredWorkClass.Persistent, () =>
            {
                Actor actor = ResolveActor(pActorId);
                Kingdom kingdom = ResolveKingdom(pKingdomId);
                City city = ResolveCity(pCityId);
                if (actor?.data == null || kingdom?.data == null) return;
                HistoryWriter.RecordPerson(actor.data.id, kingdom, actor.getName(), "temporary_levy_enlisted",
                    HistoryText.Actor(actor) + HistoryLocalizationRules.H("aw_hist_temporary_levy_enlisted"),
                    ChronicleCategory.WAR, HistoryTarget.City(city));
            });
        }

        private static void RecordDemobilizedDeferred(long pActorId, long pKingdomId, long pCityId)
        {
            DeferredRuntimeWorkService.EnqueueOrdered(DeferredWorkClass.Persistent, () =>
            {
                Actor actor = ResolveActor(pActorId);
                Kingdom kingdom = ResolveKingdom(pKingdomId);
                City city = ResolveCity(pCityId);
                if (actor?.data == null || kingdom?.data == null) return;
                HistoryWriter.RecordPerson(actor.data.id, kingdom, actor.getName(), "temporary_levy_demobilized",
                    HistoryText.Actor(actor) + HistoryLocalizationRules.H("aw_hist_temporary_levy_demobilized"),
                    ChronicleCategory.WAR, HistoryTarget.City(city));
            });
        }

        private static int PositiveModulo(int pValue, int pModulo)
        {
            if (pModulo <= 0) return 0;
            int result = pValue % pModulo;
            return result < 0 ? result + pModulo : result;
        }

        private static Kingdom ResolveKingdom(long pId)
        {
            try { return pId >= 0 ? World.world?.kingdoms?.get(pId) : null; }
            catch { return null; }
        }

        private static City ResolveCity(long pId)
        {
            try { return pId >= 0 ? World.world?.cities?.get(pId) : null; }
            catch { return null; }
        }

        private static Actor ResolveActor(long pId)
        {
            try { return pId >= 0 ? World.world?.units?.get(pId) : null; }
            catch { return null; }
        }
    }
}
