using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.county;
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
            WarriorArmyMembership,
            EnclosedUnownedZoneRepair,
            DeJureMaintenance,
            CountyAdministrationRepair,
            CourtVacancyRetryDrain,
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
            SyntheticMobilization,
            CityReservePool,
            ArmyReplenishmentOperation,
            MandateMilitaryStrength,
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
            // WAL 检查点的派发点。这一步本身只是判断要不要起一个后台任务
            // (自身节流,绝大多数调用是早退),真正的 I/O 不在这一帧上。
            LineageWalCheckpoint,
        }

        // 每个权威周期最多处理几张重试票据。一张票据触发一次整王国的
        // Reconcile,不设上限时积压的票据会在一帧里全部展开。
        private const int CourtVacancyRetryTicketsPerCycle = 2;

        private static readonly long[] StepTicks =
            new long[System.Enum.GetValues(typeof(AuthorityStep)).Length];
        private static readonly long[] StepCalls =
            new long[StepTicks.Length];

        // 托管堆 28 秒内从 558MB 涨到 857MB,而且每次回收都是
        // gc0=gc1=gc2(次次全代)。已经直接观测到一帧里 GC 跑过、整帧 88.4ms、
        // 其中 86.1ms 记在 aw3_authority 头上 —— 也就是说不少"某某步骤偶发
        // 几十毫秒"其实是停顿落点,不是那段代码本身慢。
        //
        // 所以按步骤记分配量:耗时会被停顿污染,分配量不会。取样走
        // AWAllocationProbe —— 直接调 GC.GetAllocatedBytesForCurrentThread 在
        // Unity 的 Mono 上是空桩,上一轮整局日志全是 0。
        private static readonly long[] StepBytes =
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
            long allocated = AWDiagnosticsGate.Enabled
                ? AWAllocationProbe.Sample()
                : 0L;
            try { pAction(); }
            finally
            {
                AccountStepBytes(pStep, allocated);
                EndStep(pStep, started);
            }
        }

        private static void AccountStepBytes(AuthorityStep pStep,
            long pAllocatedAtEntry)
        {
            if (pAllocatedAtEntry == 0L) return;
            int index = (int)pStep;
            if (index < 0 || index >= StepBytes.Length) return;
            long delta = AWAllocationProbe.Sample() - pAllocatedAtEntry;
            // 净堆来源在回收后会给出负增量,毛分配量来源不会。两种都丢弃非正
            // 值:净堆来源因此是下界(GC 之后那一段少算),但不会算多。
            if (delta <= 0L) return;
            System.Threading.Interlocked.Add(ref StepBytes[index], delta);
        }

        /// <summary>取走并清空按步骤累计的分配量(KB)。</summary>
        internal static string TakeAuthorityAllocation()
        {
            var ranked = new System.Collections.Generic.List<
                System.Collections.Generic.KeyValuePair<string, long>>();
            for (int index = 0; index < StepBytes.Length; index++)
            {
                long bytes = System.Threading.Interlocked.Exchange(
                    ref StepBytes[index], 0L);
                if (bytes <= 0L) continue;
                ranked.Add(new System.Collections.Generic.KeyValuePair<
                    string, long>(((AuthorityStep)index).ToString(), bytes));
            }

            if (ranked.Count == 0) return "none";
            ranked.Sort((left, right) => right.Value.CompareTo(left.Value));
            var builder = new System.Text.StringBuilder();
            int limit = System.Math.Min(12, ranked.Count);
            for (int i = 0; i < limit; i++)
            {
                if (builder.Length > 0) builder.Append(',');
                builder.Append(ranked[i].Key).Append(':')
                    .Append((ranked[i].Value / 1024.0).ToString("0.#"));
            }

            return builder.ToString();
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
            long allocated = AWDiagnosticsGate.Enabled
                ? AWAllocationProbe.Sample()
                : 0L;
            try { Measure(pBenchmarkIndex, pAction); }
            finally
            {
                AccountStepBytes(pStep, allocated);
                EndStep(pStep, started);
            }
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
            DeJureRegionMaintenanceService.Reset();
            AWEnemyPresenceCache.Clear();
            // 世界切换时放掉后台检查点的连接:它指向的是上一局的运行时库,
            // 留着会挡住 CloseAndDeleteRuntimeDb 删文件。
            core.db.LineageArchiveCheckpointService.Shutdown();
            CityReservePoolService.ClearRuntime();
            SyntheticMobilizationLedgerService.ClearRuntime();
            WarriorArmyMembershipService.ClearRuntime();
            MandateMilitaryStrengthService.ClearRuntime();
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
            // 把「活着、是战士、却没进任何军队」的 actor 补回军队。它靠 7 处
            // Enqueue/NotifyArmyAvailable 持续收活,但泵在合并 c39aab9e 时和
            // 合成兵账本一起被丢掉,于是队列只进不出 —— 掉队的战士永远归不了队。
            Step(AuthorityStep.WarriorArmyMembership,
                WarriorArmyMembershipService.ProcessAuthorityCycle);
            Step(AuthorityStep.EnclosedUnownedZoneRepair,
                EnclosedUnownedZoneRepairService.ProcessAuthorityCycle);
            // 法理州的脏票据处理。同样在 c39aab9e 丢失(它由 982fc828 接入,
            // 早于那次合并),而全仓库有 17 处 MarkKingdomDirty/MarkRegionDirty
            // 在往里塞票据。预算沿用接入时的 2 张/周期。
            Step(AuthorityStep.DeJureMaintenance,
                () => DeJureRegionMaintenanceService.ProcessAuthorityCycle(2));
            // 县级重建原本只在世界载入时跑一次(AW3WorldLoadCoordinator 调
            // RepairAfterWorldLoaded),而 RepairDirtyCities 全仓库没有调用者。
            // City.addZone 与 City.newCityEvent 会 MarkCityDirty —— 正好是「该
            // 产生新县」的两种情况 —— 但那个集合只进不出。于是游戏中新建或扩张
            // 的城市永远没有县,CountiesForCity 返回空,DiscoverVacancies 也就
            // 永远登记不出县令空缺,AI 自然不会任命县令。
            Step(AuthorityStep.CountyAdministrationRepair,
                () => CountyAdministrationStore.RepairDirtyCities());
            // 同一类问题:CourtVacancyReconciliationService 在 TechnicalFailure
            // 时会写 RetryTickets,Request 也会写,但 DrainDueRetryTickets 在
            // 文件外没有任何调用者 —— 于是那些重试永远不会发生。
            // CourtVacancySourceGuardTests 明确断言「权威周期必须排空法庭重试
            // 票据」,说明这条接线是原本设计好后丢掉的。给一个每周期的票据上限,
            // 免得一次排空太多把帧撑爆。
            Step(AuthorityStep.CourtVacancyRetryDrain,
                () => CourtVacancyReconciliationService
                    .DrainDueRetryTickets(CourtVacancyRetryTicketsPerCycle));
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
            // 合成兵账本的唯一泵。它负责三件事:把 Mobilizing 记录的配额补齐、
            // 把 Demobilizing 记录名下的合成兵真正销毁、以及兜底扫出「战争已经
            // 不在了但记录还没进遣散」的漏网记录。
            //
            // 这一步在合并 c39aab9e(把 master 的协作式阶段机换回基线扁平列表)
            // 时被整段丢掉,于是 ProcessAuthorityCycle 全仓库没有调用者 ——
            // 战争结束后 MarkDemobilizing 照常把记录切到 Demobilizing,但没有
            // 任何东西再去执行销毁。实测日志:
            //   synthetic_levy=mob:30,active:0,demob:32,done:14 live=1356
            //   mob_live=672 ended_wars=17   live_population=8733
            // 17 场战争已结束,32 条记录卡在遣散相位,1356 个合成兵永不消失,
            // 占全世界人口的 15.5%。
            Step(AuthorityStep.SyntheticMobilization,
                RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                SyntheticMobilizationLedgerService.ProcessAuthorityCycle);
            Step(AuthorityStep.CityReservePool,
                RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                CityReservePoolService.ProcessAuthorityCycle);
            Step(AuthorityStep.ArmyReplenishmentOperation,
                RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                ArmyReplenishmentOperationService.ProcessAuthorityCycle);
            // 天命王国的战时应急征兵。自驱动(每年至多一次、且要求有正在进行
            // 的战争),同样在 c39aab9e 丢失。
            Step(AuthorityStep.MandateMilitaryStrength,
                RecentFeatureBenchmarkRules.ArmyRtsLogisticsIndex,
                MandateMilitaryStrengthService.ProcessAuthorityCycle);
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
            Step(AuthorityStep.LineageWalCheckpoint,
                core.db.LineageArchiveCheckpointService.RequestIfDue);
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
