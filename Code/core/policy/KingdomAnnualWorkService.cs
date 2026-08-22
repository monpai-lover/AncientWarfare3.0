using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.core.policy
{
    internal static class KingdomAnnualWorkService
    {
        private sealed class PendingWork
        {
            public long KingdomId;
            public int ActiveYear;
            public int RequestedYear;
            public KingdomAnnualWorkStage Stage;
        }

        private static readonly Dictionary<long, PendingWork> Pending =
            new Dictionary<long, PendingWork>();
        private static readonly Dictionary<long, int> LastCompletedYear =
            new Dictionary<long, int>();

        public static void Schedule(Kingdom pKingdom)
        {
            if (!IsValid(pKingdom)) return;
            int year = Date.getCurrentYear();
            long kingdomId = pKingdom.id;
            if (Pending.TryGetValue(kingdomId, out PendingWork pending))
            {
                if (!KingdomAnnualWorkRules.ShouldAcceptSchedule(
                        pending.RequestedYear, year)) return;
                pending.RequestedYear = KingdomAnnualWorkRules.MergeYear(
                    pending.RequestedYear, year);
                return;
            }

            LastCompletedYear.TryGetValue(kingdomId, out int completedYear);
            if (LastCompletedYear.ContainsKey(kingdomId) &&
                !KingdomAnnualWorkRules.ShouldAcceptSchedule(completedYear,
                    year)) return;

            Pending[kingdomId] = new PendingWork
            {
                KingdomId = kingdomId,
                ActiveYear = year,
                RequestedYear = year,
                Stage = KingdomAnnualWorkStage.Succession
            };
            Enqueue(kingdomId);
        }

        public static void ClearRuntimeState()
        {
            Pending.Clear();
            LastCompletedYear.Clear();
            KingdomFoodReliefService.ClearRuntime();
            AsyncKingdomStrategyService.ClearRuntime();
            CityBureauAnnualWorkService.ClearRuntime();
            BanditGreatUprisingService.ClearRuntime();
            PeasantRebelBanditIslandMigrationService.ClearRuntime();
        }

        private static void Enqueue(long pKingdomId)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                KingdomAnnualWorkRules.CoalescingKey(pKingdomId),
                DeferredWorkClass.Persistent,
                () => Process(pKingdomId));
        }

        private static void Process(long pKingdomId)
        {
            if (!Pending.TryGetValue(pKingdomId, out PendingWork pending))
                return;
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (!IsValid(kingdom))
            {
                Pending.Remove(pKingdomId);
                FlushBenchmarkIfIdle();
                return;
            }

            long annualDiagnostic = RuntimePerformanceDiagnostic.
                BeginContinuousScope();
            long sampledDiagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try
            {
                RunStage(kingdom, pending.ActiveYear, pending.Stage);
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndAnnualStage(
                    StageDetailId(pending.Stage), annualDiagnostic);
                RuntimePerformanceDiagnostic.EndDetail(
                    StageDetailId(pending.Stage), sampledDiagnostic);
            }

            pending.Stage = KingdomAnnualWorkRules.NextStage(pending.Stage);
            if (pending.Stage != KingdomAnnualWorkStage.Complete)
            {
                Enqueue(pKingdomId);
                return;
            }

            LastCompletedYear[pKingdomId] = pending.ActiveYear;
            if (pending.RequestedYear > pending.ActiveYear)
            {
                pending.ActiveYear = pending.RequestedYear;
                pending.Stage = KingdomAnnualWorkStage.Succession;
                Enqueue(pKingdomId);
                return;
            }
            Pending.Remove(pKingdomId);
            FlushBenchmarkIfIdle();
        }

        private static void FlushBenchmarkIfIdle()
        {
            if (Pending.Count == 0) UpdateAgeBenchmark.Flush();
        }

        private static void RunStage(Kingdom pKingdom, int pYear,
            KingdomAnnualWorkStage pStage)
        {
            switch (pStage)
            {
                case KingdomAnnualWorkStage.Succession:
                    RunSuccession(pKingdom);
                    break;
                case KingdomAnnualWorkStage.RoyalAsylum:
                    RunRoyalAsylum(pKingdom);
                    break;
                case KingdomAnnualWorkStage.WarMobilization:
                    RunWarMobilization(pKingdom);
                    break;
                case KingdomAnnualWorkStage.Policy:
                    RunPolicy(pKingdom);
                    break;
                case KingdomAnnualWorkStage.CourtSupport:
                    RunCourtSupport(pKingdom);
                    break;
                case KingdomAnnualWorkStage.CourtAuxiliary:
                    RunCourtAuxiliary(pKingdom);
                    break;
                case KingdomAnnualWorkStage.ConferredPosthumous:
                    RunConferredPosthumous(pKingdom);
                    break;
                case KingdomAnnualWorkStage.DiplomaticMarriage:
                    RunDiplomaticMarriage(pKingdom);
                    break;
                case KingdomAnnualWorkStage.NobleRemarriage:
                    RunNobleRemarriage(pKingdom);
                    break;
                case KingdomAnnualWorkStage.DiplomaticOperation:
                    RunDiplomaticOperation(pKingdom);
                    break;
                case KingdomAnnualWorkStage.StateEconomy:
                    RunStateEconomy(pKingdom);
                    break;
                case KingdomAnnualWorkStage.StateGovernment:
                    RunStateGovernment(pKingdom);
                    break;
                case KingdomAnnualWorkStage.StateRealm:
                    RunStateRealm(pKingdom);
                    break;
                case KingdomAnnualWorkStage.StrategyMandate:
                    RunStrategyMandate(pKingdom);
                    break;
                case KingdomAnnualWorkStage.StrategyDiplomacy:
                    RunStrategyDiplomacy(pKingdom, pYear);
                    break;
                case KingdomAnnualWorkStage.StrategyMilitary:
                    RunStrategyMilitary(pKingdom, pYear);
                    break;
            }
        }

        private static string StageDetailId(KingdomAnnualWorkStage pStage)
        {
            return pStage switch
            {
                KingdomAnnualWorkStage.Succession => "annual_succession",
                KingdomAnnualWorkStage.RoyalAsylum => "annual_royal_asylum",
                KingdomAnnualWorkStage.WarMobilization =>
                    "annual_war_mobilization",
                KingdomAnnualWorkStage.Policy => "annual_policy",
                KingdomAnnualWorkStage.CourtSupport => "annual_court_support",
                KingdomAnnualWorkStage.CourtAuxiliary =>
                    "annual_court_auxiliary",
                KingdomAnnualWorkStage.ConferredPosthumous =>
                    "annual_conferred_posthumous",
                KingdomAnnualWorkStage.DiplomaticMarriage =>
                    "annual_diplomatic_marriage",
                KingdomAnnualWorkStage.NobleRemarriage =>
                    "annual_noble_remarriage",
                KingdomAnnualWorkStage.DiplomaticOperation =>
                    "annual_diplomatic_operation",
                KingdomAnnualWorkStage.StateEconomy => "annual_state_economy",
                KingdomAnnualWorkStage.StateGovernment =>
                    "annual_state_government",
                KingdomAnnualWorkStage.StateRealm => "annual_state_realm",
                KingdomAnnualWorkStage.StrategyMandate =>
                    "annual_strategy_mandate",
                KingdomAnnualWorkStage.StrategyDiplomacy =>
                    "annual_strategy_diplomacy",
                KingdomAnnualWorkStage.StrategyMilitary =>
                    "annual_strategy_military",
                _ => "annual_unknown"
            };
        }

        private static void RunSuccession(Kingdom pKingdom)
        {
            NobleRankService.RetryPendingDeathSuccessionOne();
            MeasureRecent(RecentFeatureBenchmarkRules.KingdomHeirIndex,
                () => HeirService.OnKingdomYear(pKingdom));
        }

        private static void RunRoyalAsylum(Kingdom pKingdom)
        {
            MeasureRecent(
                RecentFeatureBenchmarkRules.KingdomRoyalAsylumIndex,
                () => RoyalAsylumService.OnKingdomYear(pKingdom));
        }

        private static void RunWarMobilization(Kingdom pKingdom)
        {
            // AW3 no longer performs annual conscription or war mobilization.
            // Native enlistment and the dedicated wartime replenishment
            // operation own military strength changes.
        }

        private static void RunPolicy(Kingdom pKingdom)
        {
            MeasureDiagnostic("annual_policy_xiaization", () =>
                MeasureAge(UpdateAgeBenchmarkRules.KingdomXiaizationIndex,
                    () =>
                    {
                        XiaContactService.OnKingdomYear(pKingdom);
                        ReverseXiaizationService.OnKingdomYear(pKingdom);
                        XiaizationService.OnKingdomYear(pKingdom);
                    }));
            MeasureDiagnostic("annual_policy_core", () =>
                MeasureAge(UpdateAgeBenchmarkRules.KingdomPolicyIndex,
                    () => KingdomPolicyService.OnKingdomYear(pKingdom)));
        }

        private static void RunCourtSupport(Kingdom pKingdom)
        {
            MeasureRecent(
                RecentFeatureBenchmarkRules.KingdomCourtSupportIndex,
                () =>
                {
                    RoyalMedicalCareService.OnKingdomYear(pKingdom);
                    CourtDirectionService.RecalculateIfDirty(pKingdom);
                    CitySchoolSnapshotService.OnKingdomYear(pKingdom);
                    CourtPeaceService.OnKingdomYear(pKingdom);
                });
        }

        private static void RunCourtAuxiliary(Kingdom pKingdom)
        {
            MeasureRecent(
                RecentFeatureBenchmarkRules.KingdomCourtAuxiliaryIndex,
                () => CourtAuxiliaryLawService.OnKingdomYear(pKingdom));
        }

        private static void RunConferredPosthumous(Kingdom pKingdom)
        {
            ConferredPosthumousTitleService.OnKingdomYear(pKingdom);
        }

        private static void RunDiplomaticMarriage(Kingdom pKingdom)
        {
            MeasureRecent(
                RecentFeatureBenchmarkRules.KingdomDiplomaticMarriageIndex,
                () =>
                {
                    DiplomaticMarriageService.OnKingdomYear(pKingdom);
                    RulerHouseholdService.OnKingdomYear(pKingdom);
                });
        }

        private static void RunNobleRemarriage(Kingdom pKingdom)
        {
            MeasureDiagnostic("annual_noble_remarriage",
                () =>
                {
                    NobleRemarriageService.OnKingdomYear(pKingdom);
                    DynasticMaleLineContinuityService.OnKingdomYear(
                        pKingdom);
                });
        }

        private static void RunDiplomaticOperation(Kingdom pKingdom)
        {
            MeasureRecent(
                RecentFeatureBenchmarkRules.KingdomDiplomaticOperationIndex,
                () => DiplomaticOperationService.OnKingdomYear(pKingdom));
        }

        private static void RunStateEconomy(Kingdom pKingdom)
        {
            MeasureAge(UpdateAgeBenchmarkRules.KingdomCityTechIndex,
                () => CityTechService.OnKingdomYear(pKingdom));
            MeasureAge(UpdateAgeBenchmarkRules.KingdomCityEconomyIndex,
                () => CityEconomyService.OnKingdomYear(pKingdom));
            MeasureDiagnostic("annual_food_relief",
                () => KingdomFoodReliefService.OnKingdomYear(pKingdom));
        }

        private static void RunStateGovernment(Kingdom pKingdom)
        {
            long examAge = UpdateAgeBenchmark.Begin();
            long examRecent = RecentFeatureBenchmark.Begin();
            try { CivilServiceExamService.OnKingdomYear(pKingdom); }
            finally
            {
                UpdateAgeBenchmark.End(
                    UpdateAgeBenchmarkRules.KingdomCivilServiceExamIndex,
                    examAge);
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.CivilServiceExamAnnualIndex,
                    examRecent);
            }
            MeasureAge(UpdateAgeBenchmarkRules.KingdomOfficialCareerIndex,
                () => OfficialCareerStateService.OnKingdomYear(pKingdom));
            MeasureAge(UpdateAgeBenchmarkRules.KingdomMinisterialPowerIndex,
                () => MinisterialPowerService.OnKingdomYear(pKingdom));
            MeasureAge(UpdateAgeBenchmarkRules.KingdomVassalTributeIndex,
                () => VassalService.SettleAnnualTribute(pKingdom));
        }

        private static void RunStateRealm(Kingdom pKingdom)
        {
            MeasureAge(UpdateAgeBenchmarkRules.KingdomMandateRebelIndex,
                () => CorruptionService.OnKingdomYear(pKingdom));
            MeasureAge(UpdateAgeBenchmarkRules.KingdomWarTerritoryIndex,
                () => WarTerritoryService.OnKingdomYear(pKingdom));
            MeasureAge(UpdateAgeBenchmarkRules.KingdomMandateIndex,
                () =>
                {
                    MandateService.OnKingdomYear(pKingdom);
                    MandateIslandExileService.OnKingdomYear(pKingdom);
                    RitualDiplomacyOpinionService.OnKingdomYear(pKingdom);
                });

            long ageBenchmark = UpdateAgeBenchmark.Begin();
            long recentBenchmark = RecentFeatureBenchmark.Begin();
            try { FeudatoryService.OnKingdomYear(pKingdom); }
            finally
            {
                UpdateAgeBenchmark.End(
                    UpdateAgeBenchmarkRules.KingdomFeudatoryIndex,
                    ageBenchmark);
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.KingdomFeudatoryIndex,
                    recentBenchmark);
            }
            MilitaryGovernorateAiService.OnKingdomYear(pKingdom);
        }

        private static void RunStrategyMandate(Kingdom pKingdom)
        {
            MeasureAge(UpdateAgeBenchmarkRules.KingdomMandateDecisionIndex,
                () => MandateDecisionService.OnKingdomYear(pKingdom));
            MeasureAge(UpdateAgeBenchmarkRules.KingdomMandateRebelIndex,
                () => MandateRebelService.OnKingdomYear(pKingdom));
            MeasureAge(UpdateAgeBenchmarkRules.KingdomMandateRebelIndex,
                () => BanditGreatUprisingService.OnKingdomYear(pKingdom));
            MeasureAge(UpdateAgeBenchmarkRules.KingdomMandateRebelIndex,
                () => MassUprisingClusterService.OnKingdomYear(pKingdom));
            MeasureAge(UpdateAgeBenchmarkRules.KingdomMandateRebelIndex,
                () => PeasantRebelBanditSpawnService.OnKingdomYear(pKingdom));
            MeasureAge(UpdateAgeBenchmarkRules.KingdomForeignOccupationIndex,
                () => ForeignOccupationService.OnKingdomYear(pKingdom));

        }

        private static void RunStrategyDiplomacy(Kingdom pKingdom, int pYear)
        {
            if (!DiplomacyAiRules.ShouldRun(
                    AWPerformanceSettings.EnableDiplomacyAi)) return;
            bool runHeavy = KingdomYearSchedulerRules.ShouldRunHeavySystem(
                pYear, pKingdom.id, pModulo: 2, pSlot: 0);
            if (runHeavy)
            {
                MeasureAge(UpdateAgeBenchmarkRules.KingdomWarPlotIndex,
                    () => WarPlotRedirectService.OnKingdomYear(pKingdom));
                MeasureAge(UpdateAgeBenchmarkRules.KingdomWarAiIndex,
                    () => AsyncKingdomStrategyService.ScheduleWar(
                        pKingdom, pYear));
                MeasureAge(UpdateAgeBenchmarkRules.KingdomVassalAiIndex,
                    () =>
                    {
                        VassalService.OnKingdomYear(pKingdom);
                        VassalAIService.OnKingdomYear(pKingdom);
                        MeasureRecent(
                            RecentFeatureBenchmarkRules.KingdomDiplomacyIndex,
                            () =>
                            {
                                AsyncKingdomStrategyService.ScheduleDiplomacy(
                                    pKingdom, pYear);
                                DiplomaticCoalitionService.OnKingdomYear(
                                    pKingdom);
                            });
                    });
            }

        }

        private static void RunStrategyMilitary(Kingdom pKingdom, int pYear)
        {
            KingdomWarDirectorService.Schedule(pKingdom);
            bool runGeneral = KingdomYearSchedulerRules.ShouldRunHeavySystem(
                pYear, pKingdom.id, pModulo: 4, pSlot: 2);
            if (!runGeneral) return;
            MeasureAge(UpdateAgeBenchmarkRules.KingdomGeneralIndex,
                () =>
                {
                    GeneralService.OnKingdomYear(pKingdom);
                    CourtMeritRewardService.OnKingdomYear(pKingdom);
                });
        }

        private static void MeasureRecent(int pIndex, Action pAction)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try { pAction(); }
            finally { RecentFeatureBenchmark.End(pIndex, benchmark); }
        }

        private static void MeasureAge(int pIndex, Action pAction)
        {
            long benchmark = UpdateAgeBenchmark.Begin();
            try { pAction(); }
            finally { UpdateAgeBenchmark.End(pIndex, benchmark); }
        }

        private static void MeasureDiagnostic(string pId, Action pAction)
        {
            long diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try { pAction(); }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail(pId, diagnostic);
            }
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static bool IsValid(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   !pKingdom.isNeutral();
        }
    }
}
