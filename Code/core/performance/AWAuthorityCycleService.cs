using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.naming;
using AncientWarfare3.core.pathfinding;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.performance
{
    internal static class AWAuthorityCycleService
    {
        // 权威周期是实测的最大帧尖峰源:worst_frame_buckets 里 sched_aw3_authority
        // 在 14 个已载入采样中有 8 个是最坏帧主因,单帧 45–128ms。而
        // ProcessCooperativeStep 无条件返回 true,所以下面这一长串服务是背靠背
        // 一次跑完的,没有预算也没有让出点。先按步分段实测是哪几项占了这个量,
        // 再决定拆帧还是优化本体 —— 两条路的代价完全不同。
        internal enum AuthorityStep
        {
            WesternCourtElection = 0,
            AccessionIdentityDeferred,
            ReigningRoyalLineage,
            SuccessionDisputePersistence,
            LocalizedNameMigration,
            WesternLineageMigration,
            KingdomInstitutionalXiaization,
            DynasticMaleLineContinuity,
            NobleHeirPregnancy,
            RulerHouseholdPregnancy,
            ArmyMembershipReconciliation,
            EnclosedUnownedZoneRepair,
            EmptyCityResettlement,
            TemporaryMilitaryReturn,
            WarArmyReturn,
            ArmyRtsAssignmentReconciliation,
            PathfindingBootstrap,
            ArmyRtsScheduling,
            HistoricalSchoolRuntime,
            CivilServiceExam,
            DiplomacyProposal,
            DiplomaticOperation,
            ZhuluAgeDirector,
            WarTerminalSettlement,
            SpecialGovernmentWarParticipation,
            KingdomDecisionMonthly,
            TemporaryLevyLegacyMigration,
            CityReservePool,
            ArmyReplenishmentOperation,
            WarRefugee,
            DrainAuthorityCompletions,
            FlushPendingWarParticipantSources,
            // 原本合成一项 DrainDeferredAuthorityWork,实测 3.559 ms/次、占权威
            // 周期 28.9%。但它其实是三件事:两个无预算的 Bandit 服务,加上一次
            // 队列排空 —— 而队列排空受 MaximumItemsPerAuthorityFrame=1 限制,
            // 每次只执行一个 work item(日志里 last_drain 恒为 1)。三者各占多少
            // 直接决定接下来该动谁,所以拆开测。
            DeferredBanditPopulation,
            DeferredBanditDisposal,
            DeferredQueueDrain,
            // WarRefugee 同理:按月闸门,202 次调用里绝大多数是廉价早退,真正
            // 干活的那几次把整月的量压在一帧上(合计 712ms,占 28.6%)。三个
            // 子步骤的预算都是按月而非按帧的,先测出量集中在哪一个。
            RefugeeMonthlyReservations,
            RefugeePersistedJourneys,
            RefugeeThreatenedCities,
            SlaveCaptureScan,
        }

        private static readonly long[] StepTicks =
            new long[System.Enum.GetValues(typeof(AuthorityStep)).Length];
        private static readonly long[] StepCalls =
            new long[StepTicks.Length];

        private static long BeginStep()
        {
            return System.Diagnostics.Stopwatch.GetTimestamp();
        }
        private static void EndStep(AuthorityStep pStep, long pStarted)
        {
            int index = (int)pStep;
            if (index < 0 || index >= StepTicks.Length) return;
            System.Threading.Interlocked.Add(ref StepTicks[index],
                System.Diagnostics.Stopwatch.GetTimestamp() - pStarted);
            System.Threading.Interlocked.Increment(ref StepCalls[index]);
            RuntimePerformanceDiagnostic.EndAuthorityStage(
                pStep.ToString(), pStarted);
        }

        private static void Step(AuthorityStep pStep, System.Action pAction)
        {
            long started = BeginStep();
            try { pAction(); }
            finally { EndStep(pStep, started); }
        }

        // 供权威周期内部那些"自己还嵌了几件事"的服务把子步骤也记进同一本账。
        // WarRefugee 就是这种:外层一项 712ms,但真正的量集中在三个子步骤里的
        // 哪一个,不拆开看不出来。
        internal static void SubStep(AuthorityStep pStep, System.Action pAction)
        {
            Step(pStep, pAction);
        }

        private static void Step(AuthorityStep pStep, int pBenchmarkIndex,
            System.Action pAction)
        {
            long started = BeginStep();
            try { Measure(pBenchmarkIndex, pAction); }
            finally { EndStep(pStep, started); }
        }

        internal static string TakeAuthorityBreakdown()
        {
            var builder = new System.Text.StringBuilder();
            for (int index = 0; index < StepTicks.Length; index++)
            {
                long ticks = System.Threading.Interlocked.Exchange(
                    ref StepTicks[index], 0L);
                long calls = System.Threading.Interlocked.Exchange(
                    ref StepCalls[index], 0L);
                if (ticks <= 0L && calls <= 0L) continue;
                if (builder.Length > 0) builder.Append(',');
                builder.Append(((AuthorityStep)index).ToString())
                    .Append(':')
                    .Append((ticks * 1000.0 /
                             System.Diagnostics.Stopwatch.Frequency)
                        .ToString("0.###"))
                    .Append('/')
                    .Append(calls);
            }

            return builder.Length == 0 ? "none" : builder.ToString();
        }

        private static readonly AWAuthorityCycleGate CooperativeGate =
            new AWAuthorityCycleGate();
        private static readonly AWAuthorityCycleGate NativeGate =
            new AWAuthorityCycleGate();
        private static long _nativeCycleToken;

        public static void ProcessCooperativeCycle(long pCycleToken,
            bool pCyclePaused)
        {
            ProcessCycle(CooperativeGate, pCycleToken, pCyclePaused);
        }

        public static void ProcessNativeCycle()
        {
            if (_nativeCycleToken < long.MaxValue)
                _nativeCycleToken++;
            bool paused = MapBox.instance == null ||
                          MapBox.instance.isPaused();
            ProcessCycle(NativeGate, _nativeCycleToken, paused);
        }

        public static void Reset()
        {
            CooperativeGate.Reset();
            NativeGate.Reset();
            _nativeCycleToken = 0L;
            AWMilitaryFrontLaneScheduler.Reset();
            ArmyRtsSchedulingService.Reset();
            NobleHeirPregnancyService.Reset();
            RulerHouseholdPregnancyService.Reset();
            DynasticMaleLineContinuityService.Reset();
            EnclosedUnownedZoneRepairService.Reset();
            EmptyCityResettlementService.Reset();
            WarScoreService.ClearPendingCityOccupations();
            CivilServiceExamService.ClearRuntime();
            WesternCourtElectionService.Reset();
            AccessionIdentityService.ClearRuntime();
            DynastyTitleRegistryService.ClearRuntime();
            TemporaryMilitaryReturnService.ClearRuntime();
            WarArmyReturnService.ClearRuntime();
            ArmyRtsAssignmentReconciliationService.Reset();
            AWEnemyPresenceCache.Clear();
            AWStatusSimulationScheduler.ClearRuntime();
            CityReservePoolService.ClearRuntime();
            WarForceEliminationSettlementService.ClearRuntime();
            WarTerminalSettlementCoordinator.ClearRuntime();
            SpecialGovernmentWarParticipationService.ClearRuntime();
            ArmyReplenishmentOperationService.ClearRuntime();
            KingdomDecisionMonthlyService.Reset();
            WarParticipantEntrySourceService.Instance.ClearRuntime();
            ZhuluAgeDirectorService.Reset();
            AWLocalizedNameMigrationService.Reset();
            WesternLineageMigrationService.Reset();
            KingdomInstitutionalXiaizationService.Reset();
            ReigningRoyalLineageIndex.Reset();
            SuccessionDisputePersistenceService.Reset();
            ActorDeathArchiveService.Reset();
            PeasantRebelBanditStrongholdPopulationService.Clear();
            BanditStrongholdCityDisposalService.Clear();
        }

        private static void ProcessCycle(AWAuthorityCycleGate pGate,
            long pCycleToken, bool pPaused)
        {
            bool allowed = AWFrameSchedulerRules.ShouldRunAuthorityCycle(
                Config.game_loaded, SmoothLoader.isLoading(), pPaused,
                AW3MultiplayerReplicaScope.IsReplicaSession) &&
                           !AWWorldInitializationGate.IsPending();
            if (!pGate.TryEnter(pCycleToken, allowed)) return;

            Step(AuthorityStep.WesternCourtElection,
                WesternCourtElectionService.ProcessAuthorityCycle);
            Step(AuthorityStep.AccessionIdentityDeferred,
                AccessionIdentityService.ProcessDeferredInstallations);
            Step(AuthorityStep.ReigningRoyalLineage,
                ReigningRoyalLineageIndex.ProcessAuthorityCycle);
            Step(AuthorityStep.SuccessionDisputePersistence,
                SuccessionDisputePersistenceService.ProcessAuthorityCycle);
            Step(AuthorityStep.LocalizedNameMigration,
                AWLocalizedNameMigrationService.ProcessAuthorityCycle);
            Step(AuthorityStep.WesternLineageMigration,
                WesternLineageMigrationService.ProcessAuthorityCycle);
            Step(AuthorityStep.KingdomInstitutionalXiaization,
                KingdomInstitutionalXiaizationService.ProcessAuthorityCycle);
            Step(AuthorityStep.DynasticMaleLineContinuity,
                DynasticMaleLineContinuityService.ProcessAuthorityCycle);
            Step(AuthorityStep.NobleHeirPregnancy,
                NobleHeirPregnancyService.ProcessAuthorityCycle);
            Step(AuthorityStep.RulerHouseholdPregnancy,
                RulerHouseholdPregnancyService.ProcessAuthorityCycle);
            Step(AuthorityStep.ArmyMembershipReconciliation,
                ArmyMembershipReconciliationService.ProcessFrame);
            Step(AuthorityStep.EnclosedUnownedZoneRepair,
                EnclosedUnownedZoneRepairService.ProcessAuthorityCycle);
            Step(AuthorityStep.EmptyCityResettlement,
                EmptyCityResettlementService.ProcessAuthorityCycle);
            Step(AuthorityStep.TemporaryMilitaryReturn,
                TemporaryMilitaryReturnService.ProcessFrame);
            Step(AuthorityStep.WarArmyReturn,
                WarArmyReturnService.ProcessFrame);
            Step(AuthorityStep.ArmyRtsAssignmentReconciliation,
                ArmyRtsAssignmentReconciliationService.ProcessAuthorityCycle);
            Step(AuthorityStep.PathfindingBootstrap,
                RecentFeatureBenchmarkRules.PathfindingIndex,
                AWPathfindingBootstrap.ProcessFrame);
            long argStep = BeginStep();
            try
            {
                ArmyRtsSchedulingService.ProcessAw3Authority(pCycleToken,
                    pPaused);
            }
            finally { EndStep(AuthorityStep.ArmyRtsScheduling, argStep); }
            Step(AuthorityStep.HistoricalSchoolRuntime,
                RecentFeatureBenchmarkRules.SchoolsIndex,
                HistoricalSchoolRuntime.ProcessFrame);
            Step(AuthorityStep.CivilServiceExam,
                RecentFeatureBenchmarkRules.CivilServiceExamRuntimeIndex,
                CivilServiceExamService.ProcessAuthorityCycle);
            Step(AuthorityStep.DiplomacyProposal,
                RecentFeatureBenchmarkRules.DiplomacyIndex,
                DiplomacyProposalService.ProcessFrame);
            Step(AuthorityStep.DiplomaticOperation,
                RecentFeatureBenchmarkRules.DiplomacyIndex,
                DiplomaticOperationService.ProcessFrame);
            Step(AuthorityStep.ZhuluAgeDirector,
                RecentFeatureBenchmarkRules.DiplomacyIndex,
                ZhuluAgeDirectorService.ProcessAuthorityCycle);
            Step(AuthorityStep.WarTerminalSettlement,
                RecentFeatureBenchmarkRules.DiplomacyIndex,
                WarTerminalSettlementCoordinator.ProcessAuthorityCycle);
            Step(AuthorityStep.SpecialGovernmentWarParticipation,
                RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                SpecialGovernmentWarParticipationService.
                    ProcessAuthorityCycle);
            Step(AuthorityStep.KingdomDecisionMonthly,
                RecentFeatureBenchmarkRules.MonthKingdomPolicyIndex,
                KingdomDecisionMonthlyService.ProcessAuthorityCycle);
            Step(AuthorityStep.TemporaryLevyLegacyMigration,
                TemporaryLevyService.ProcessLegacyMigration);
            Step(AuthorityStep.CityReservePool,
                RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                CityReservePoolService.ProcessAuthorityCycle);
            Step(AuthorityStep.ArmyReplenishmentOperation,
                RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                ArmyReplenishmentOperationService.ProcessAuthorityCycle);
            argStep = BeginStep();
            try
            {
                WarRefugeeService.ProcessAuthorityCycle(pCycleToken, pPaused);
            }
            finally { EndStep(AuthorityStep.WarRefugee, argStep); }
            Step(AuthorityStep.DrainAuthorityCompletions,
                RecentFeatureBenchmarkRules.AsyncCommitIndex,
                DrainAuthorityCompletions);
            Step(AuthorityStep.FlushPendingWarParticipantSources,
                RecentFeatureBenchmarkRules.AsyncCommitIndex,
                FlushPendingWarParticipantSources);
            // DrainDeferredAuthorityWork 内部自行分三段记账,这里不再外包一层,
            // 否则 authority_steps 的总和会把这部分重复计入。
            Measure(RecentFeatureBenchmarkRules.DeferredWorkIndex,
                DrainDeferredAuthorityWork);
            Step(AuthorityStep.SlaveCaptureScan,
                RecentFeatureBenchmarkRules.CaptureScanIndex,
                SlaveCaptureScanService.DrainFrame);
        }

        private static void DrainAuthorityCompletions()
        {
            // Async results are applied once per render frame by
            // AW_DeferredRuntimeWorkPatch. Replaying them inside every
            // simulation pass makes large-step mode pay the same callback
            // cost multiple times in one frame.
            ActorDeathArchiveService.ProcessAuthorityCycle();
        }

        private static void DrainDeferredAuthorityWork()
        {
            Step(AuthorityStep.DeferredBanditPopulation,
                PeasantRebelBanditStrongholdPopulationService.
                    ProcessAuthorityCycle);
            Step(AuthorityStep.DeferredBanditDisposal,
                BanditStrongholdCityDisposalService.ProcessAuthorityCycle);
            int itemLimit = DeferredRuntimeWorkRules.
                ResolveItemsPerAuthorityFrame(
                DeferredRuntimeWorkService.PendingCount);
            if (itemLimit <= 0) return;
            long drainStep = BeginStep();
            try
            {
                DeferredRuntimeWorkService.DrainFrame(pMilliseconds: 1.0,
                    pMaxItems: itemLimit);
            }
            finally { EndStep(AuthorityStep.DeferredQueueDrain, drainStep); }
        }

        private static void FlushPendingWarParticipantSources()
        {
            WarParticipantEntrySourceService.Instance.
                FlushPendingSources(32);
        }

        // The cooperative runner is retained as the owner of the staged
        // simulation. This compatibility surface delegates to the existing
        // authority pass and never reintroduces removed migration stages.
        public static bool ProcessCooperativeStep(long pCycleToken,
            bool pCyclePaused)
        {
            ProcessCooperativeCycle(pCycleToken, pCyclePaused);
            return true;
        }

        public static void AbortCooperativeCycle()
        {
            CooperativeGate.Reset();
        }

        public static string GetCooperativePhaseName()
        {
            return "aw3.authority";
        }

        // Read-only compatibility surface for the newer performance report.
        public static string GetDiagnostics()
        {
            return "native_cycle_token=" + _nativeCycleToken;
        }

        private static void Measure(int pIndex, System.Action pAction)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try { pAction(); }
            finally
            {
                RecentFeatureBenchmark.End(pIndex, benchmark);
            }
        }
    }
}
