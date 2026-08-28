using System;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.pathfinding;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.schools;
using AncientWarfare3.core.uiquery;

namespace AncientWarfare3.core.policy
{
    internal static class RuntimePerformanceDiagnostic
    {
        private static readonly long[] RecentTicks =
            new long[RecentFeatureBenchmarkRules.EntryIds.Length];
        private static readonly int[] RecentCounts =
            new int[RecentFeatureBenchmarkRules.EntryIds.Length];
        private static readonly long[] ActorRaceTicks =
            new long[ActorRacePerformanceRules.BucketCount *
                     ActorRacePerformanceRules.MetricCount];
        private static readonly int[] ActorRaceCounts =
            new int[ActorRaceTicks.Length];
        private static readonly long[] ArmyRtsControllerTicks =
            new long[ArmyRtsControllerPerformanceRules.StageCount];
        private static readonly int[] ArmyRtsControllerCounts =
            new int[ArmyRtsControllerTicks.Length];
        private static readonly long[] DeathStageTicks =
            new long[ActorDeathPerformanceRules.StageCount];
        private static readonly int[] DeathStageCounts =
            new int[ActorDeathPerformanceRules.StageCount];
        private static long _frame;
        private static bool _sampling;
        private static long _intervalStarted;
        private static long _intervalFrameStarted;
        private static long _intervalProcessCpuStarted;
        private static long _intervalMapUpdateTicks;
        private static long _intervalOutsideMapTicks;
        private static long _previousFrameEnded;
        private static int _intervalGc0Started;
        private static int _intervalGc1Started;
        private static int _intervalGc2Started;
        private static int _intervalMode;
        private static int _actorDetailSamples;
        private static long _frameStarted;
        private static string _frameContinuousStageId = "none";
        private static long _frameContinuousStageTicks;
        private static string _frameAnnualStageId = "none";
        private static long _frameAnnualStageTicks;
        private static long _worstFrameNumber;
        private static long _worstFrameTicks;
        private static string _worstFrameStageId = "none";
        private static long _worstFrameStageTicks;
        private static string _worstFrameAnnualStageId = "none";
        private static long _worstFrameAnnualStageTicks;

        // 帧尖峰归因。worst_frame_ms 长期是平均帧的 4–9 倍,但既有的
        // worst_frame_stage 通道只有 AW_ChroniclePatch 一个写入方,所以每次采样
        // 都报 none。这里按帧累一份粗粒度分桶,谁成为本区间最坏帧就把它当帧的
        // 账本连同 GC 次数一起快照下来 —— 尖峰要么落在某个桶里,要么落在
        // worst_frame_other(我们代码之外),两种结论都能直接指向下一步。
        public enum FrameCostBucket
        {
            MilitaryP0 = 0,
            MilitaryFrontLane,
            ActorPost,
            Presentation,
            DeferredWork,
            // 以下 11 项必须与 AWSchedulerStageBucket 的声明顺序逐一对应,
            // AccountSchedulerFrameCost 靠偏移量做映射。
            SchedulerMaintenance,
            SchedulerWorld,
            SchedulerMap,
            SchedulerCities,
            SchedulerActors,
            SchedulerBuildings,
            SchedulerArmies,
            SchedulerKingdoms,
            SchedulerStatuses,
            SchedulerOtherVanilla,
            SchedulerAw3Authority,
        }

        private static readonly string[] FrameCostBucketNames =
        {
            "military_p0",
            "military_front_lane",
            "actor_post",
            "presentation",
            "deferred_work",
            "sched_maintenance",
            "sched_world",
            "sched_map",
            "sched_cities",
            "sched_actors",
            "sched_buildings",
            "sched_armies",
            "sched_kingdoms",
            "sched_statuses",
            "sched_other_vanilla",
            "sched_aw3_authority",
        };

        // military_p0 与 actor_post 跑在调度器的 Actors 阶段内部,和
        // sched_actors 是包含关系。它们照常打印(要知道 Actors 阶段里我们占了
        // 多少),但不能计入 accounted,否则 other 会被重复扣减到 0。
        private static bool IsNestedFrameBucket(int pIndex)
        {
            return pIndex == (int)FrameCostBucket.MilitaryP0 ||
                   pIndex == (int)FrameCostBucket.ActorPost;
        }

        private static readonly long[] FrameBucketTicks =
            new long[FrameCostBucketNames.Length];
        private static readonly long[] WorstFrameBucketTicks =
            new long[FrameCostBucketNames.Length];
        private static int _frameGcStarted;
        private static int _worstFrameGcCount;

        public static void AccountFrameCost(FrameCostBucket pBucket,
            long pStarted)
        {
            if (pStarted == 0L) return;
            int index = (int)pBucket;
            if (index < 0 || index >= FrameBucketTicks.Length) return;
            long elapsed = Stopwatch.GetTimestamp() - pStarted;
            if (elapsed <= 0L) return;
            Interlocked.Add(ref FrameBucketTicks[index], elapsed);
        }

        public static void AccountSchedulerFrameCost(
            AWSchedulerStageBucket pBucket, long pStarted)
        {
            int offset = (int)pBucket;
            if (offset < 0 ||
                offset >= (int)AWSchedulerStageBucket.Count) return;
            AccountFrameCost(
                (FrameCostBucket)((int)FrameCostBucket.SchedulerMaintenance +
                                  offset), pStarted);
        }
        private const int ActorDetailBudgetPerFrame =
            ActorDiagnosticSamplingRules.MaximumDetailSamplesPerFrame;
        private static long _sampleFrameStarted;
        private static long _actorWallTicks;
        private static long _actorAiTicks;
        private static int _actorAiCalls;
        private static readonly Dictionary<string, ActorTaskSample>
            ActorTaskSamples = new Dictionary<string, ActorTaskSample>(
                StringComparer.Ordinal);
        private static long _pathSmoothTicks;
        private static int _pathSmoothCalls;
        private static long _slowestPathSmoothTicks;
        private static long _slowestPathSmoothActorId = -1L;
        private static string _slowestPathSmoothTaskId = "none";
        private static long _pathStepTicks;
        private static long _pathStepNestedTicks;
        private static int _pathStepCalls;
        private static long _buildingWallTicks;
        private static long _updateAgeWallTicks;
        private static long _deathCheckTicks;
        private static int _deathCheckCalls;
        private static long _deathEventTicks;
        private static int _deathEventCalls;
        private static long _recentTotalTicks;
        private static int _recentTotalCalls;
        private static string _slowestDetailId = "none";
        private static long _slowestDetailTicks;
        private static string _slowestDeferredKey = "none";
        private static long _slowestDeferredTicks;
        private static string _slowestAnnualStageId = "none";
        private static long _slowestAnnualStageTicks;
        private static string _slowestAuthorityStageId = "none";
        private static long _slowestAuthorityStageTicks;
        private static int _sampleGc0Start;
        private static int _sampleGc1Start;
        private static int _sampleGc2Start;
        private static long _sampleManagedHeapStart;
        [ThreadStatic] private static int _pathSmoothDepth;

        private sealed class ActorTaskSample
        {
            public long Ticks;
            public int Calls;
        }

        public readonly struct ActorRaceScopeToken
        {
            public ActorRaceScopeToken(long pStarted,
                ActorRacePerformanceBucket pBucket)
            {
                Started = pStarted;
                Bucket = pBucket;
            }

            public long Started { get; }
            public ActorRacePerformanceBucket Bucket { get; }
        }

        public static bool IsSampling => _sampling;

        public static void BeginFrame()
        {
            if (_frame < long.MaxValue) _frame++;
            long frameStarted = Stopwatch.GetTimestamp();
            int currentMode = CurrentIntervalMode();
            if (currentMode == 0)
            {
                if (RuntimePerformanceDiagnosticRules.
                        ShouldTerminateIntervalBaseline(
                            _intervalMode, _sampling))
                    TerminateIntervalBaseline();
                _sampling = false;
                _frameStarted = 0L;
                _previousFrameEnded = 0L;
                return;
            }
            Interlocked.Exchange(ref _actorDetailSamples, 0);
            bool startInterval = RuntimePerformanceDiagnosticRules.
                ShouldStartIntervalBaseline(_intervalMode, currentMode);
            if (startInterval)
            {
                ResetDeathInterval();
                _intervalStarted = frameStarted;
                _intervalFrameStarted = Math.Max(0L, _frame - 1L);
                _intervalProcessCpuStarted = CurrentProcessCpuTicks();
                _intervalMapUpdateTicks = 0L;
                _intervalOutsideMapTicks = 0L;
                _intervalGc0Started = GC.CollectionCount(0);
                _intervalGc1Started = GC.CollectionCount(1);
                _intervalGc2Started = GC.CollectionCount(2);
                ResetWorstFrameInterval();
            }
            else
            {
                _intervalOutsideMapTicks +=
                    RuntimePerformanceDiagnosticRules.FrameGapTicks(
                        _previousFrameEnded, frameStarted);
            }
            _intervalMode = currentMode;
            _frameStarted = frameStarted;
            _frameContinuousStageId = "none";
            _frameContinuousStageTicks = 0L;
            _frameAnnualStageId = "none";
            _frameAnnualStageTicks = 0L;
            Array.Clear(FrameBucketTicks, 0, FrameBucketTicks.Length);
            _frameGcStarted = GC.CollectionCount(0);
            _slowestAuthorityStageId = "none";
            _slowestAuthorityStageTicks = 0L;
            _sampling = RuntimePerformanceDiagnosticRules.ShouldSample(
                currentMode != 0, _frame);
            AWSchedulerStageDiagnostics.BeginFrame(_sampling);
            if (_sampling)
            {
                ResetSample();
                _sampleFrameStarted = Stopwatch.GetTimestamp();
                _sampleGc0Start = GC.CollectionCount(0);
                _sampleGc1Start = GC.CollectionCount(1);
                _sampleGc2Start = GC.CollectionCount(2);
                _sampleManagedHeapStart = GC.GetTotalMemory(false);
            }
        }

        public static long BeginScope()
        {
            return _sampling ? Stopwatch.GetTimestamp() : 0L;
        }

        public static long BeginContinuousScope()
        {
            return _frameStarted != 0L ? Stopwatch.GetTimestamp() : 0L;
        }

        public static long BeginAuthorityStage()
        {
            // Authority stages are synchronous main-thread work. Keep timing
            // active between text-log sampling windows so deferred spikes
            // retain their actual stage owner.
            return _frameStarted != 0L ? Stopwatch.GetTimestamp() : 0L;
        }

        public static long BeginDeferredItemScope()
        {
            return _frameStarted != 0L ? Stopwatch.GetTimestamp() : 0L;
        }

        public static void EndAuthorityStage(string pId, long pStarted)
        {
            long elapsed = Elapsed(pStarted);
            if (elapsed < 0L || !RuntimePerformanceDiagnosticRules.
                    ShouldReplaceSlowest(_slowestAuthorityStageTicks,
                        elapsed)) return;
            _slowestAuthorityStageTicks = elapsed;
            _slowestAuthorityStageId = string.IsNullOrEmpty(pId)
                ? "authority_unknown"
                : pId;
        }

        public static bool ShouldCollectActorDetail()
        {
            return _sampling || Bench.bench_enabled;
        }

        public static bool TryConsumeActorDetailSample()
        {
            if (!ShouldCollectActorDetail()) return false;
            int used = Interlocked.Increment(ref _actorDetailSamples) - 1;
            return ActorDiagnosticSamplingRules.ShouldCollect(_sampling,
                Bench.bench_enabled, used, ActorDetailBudgetPerFrame);
        }

        public static long BeginDeathEvent()
        {
            return DeathDiagnosticsEnabled()
                ? Stopwatch.GetTimestamp()
                : 0L;
        }

        public static long BeginDeathStage(ActorDeathPerformanceStage pStage)
        {
            return DeathDiagnosticsEnabled() &&
                   ActorDeathPerformanceRules.IsValid(pStage)
                ? Stopwatch.GetTimestamp()
                : 0L;
        }

        public static ActorRaceScopeToken BeginActorRaceScope(Actor pActor)
        {
            if (!_sampling) return default;
            return new ActorRaceScopeToken(Stopwatch.GetTimestamp(),
                ActorRacePerformanceRules.Classify(pActor?.asset?.id));
        }

        public static void EndActorRaceScope(ActorRacePerformanceMetric pMetric,
            ActorRaceScopeToken pToken)
        {
            long elapsed = Elapsed(pToken.Started);
            if (elapsed < 0L) return;
            int index = ActorRacePerformanceRules.Index(pToken.Bucket, pMetric);
            ActorRaceTicks[index] += elapsed;
            ActorRaceCounts[index]++;
        }

        public static long BeginArmyRtsControllerStage(
            ArmyRtsControllerPerformanceStage pStage)
        {
            return _sampling && ArmyRtsControllerPerformanceRules.IsValid(
                pStage) ? Stopwatch.GetTimestamp() : 0L;
        }

        public static void EndArmyRtsControllerStage(
            ArmyRtsControllerPerformanceStage pStage, long pStarted)
        {
            long elapsed = Elapsed(pStarted);
            if (elapsed < 0L || !ArmyRtsControllerPerformanceRules.IsValid(
                    pStage)) return;
            int index = (int)pStage;
            ArmyRtsControllerTicks[index] += elapsed;
            ArmyRtsControllerCounts[index]++;
        }

        public static long BeginPathSmooth()
        {
            if (!_sampling) return 0L;
            _pathSmoothDepth++;
            return Stopwatch.GetTimestamp();
        }

        public static long BeginPathStep()
        {
            if (!_sampling) return 0L;
            long started = Math.Max(1L, Stopwatch.GetTimestamp());
            return _pathSmoothDepth > 0 ? -started : started;
        }

        public static void EndActorWall(long started)
        {
            AddElapsed(ref _actorWallTicks, started);
        }

        public static void EndActorAi(long started, string pTaskId)
        {
            long elapsed = Elapsed(started);
            if (elapsed < 0L) return;
            _actorAiTicks += elapsed;
            _actorAiCalls++;
            RecordActorTask(pTaskId, elapsed);
        }

        public static void EndPathSmooth(long started, Actor pActor = null)
        {
            try
            {
                long elapsed = Elapsed(started);
                if (elapsed < 0L) return;
                _pathSmoothTicks += elapsed;
                _pathSmoothCalls++;
                if (elapsed <= _slowestPathSmoothTicks) return;
                _slowestPathSmoothTicks = elapsed;
                _slowestPathSmoothActorId = pActor?.data?.id ?? -1L;
                try
                {
                    _slowestPathSmoothTaskId = pActor?.ai?.task?.id ?? "none";
                }
                catch
                {
                    _slowestPathSmoothTaskId = "unknown";
                }
            }
            finally
            {
                if (started != 0L)
                    _pathSmoothDepth = Math.Max(0, _pathSmoothDepth - 1);
            }
        }

        public static void EndPathStep(long started)
        {
            if (started == 0L) return;
            long absolute = started == long.MinValue
                ? long.MaxValue
                : Math.Abs(started);
            long elapsed = Math.Max(0L, Stopwatch.GetTimestamp() - absolute);
            _pathStepTicks += elapsed;
            if (started < 0L) _pathStepNestedTicks += elapsed;
            _pathStepCalls++;
        }

        public static void EndBuildingWall(long started)
        {
            AddElapsed(ref _buildingWallTicks, started);
        }

        public static void EndUpdateAgeWall(long started)
        {
            AddElapsed(ref _updateAgeWallTicks, started);
        }

        public static void EndDeathCheck(long started)
        {
            long elapsed = Elapsed(started);
            if (elapsed < 0L) return;
            _deathCheckTicks += elapsed;
            _deathCheckCalls++;
        }

        public static void EndDeathEvent(long started)
        {
            long elapsed = Elapsed(started);
            if (elapsed < 0L) return;
            Interlocked.Add(ref _deathEventTicks, elapsed);
            Interlocked.Increment(ref _deathEventCalls);
        }

        public static void EndDeathStage(ActorDeathPerformanceStage pStage,
            long started)
        {
            long elapsed = Elapsed(started);
            if (elapsed < 0L ||
                !ActorDeathPerformanceRules.IsValid(pStage)) return;
            int index = (int)pStage;
            Interlocked.Add(ref DeathStageTicks[index], elapsed);
            Interlocked.Increment(ref DeathStageCounts[index]);
        }

        public static void EndDetail(string pId, long started)
        {
            long elapsed = Elapsed(started);
            if (elapsed < 0L || !RuntimePerformanceDiagnosticRules.
                    ShouldReplaceSlowest(_slowestDetailTicks, elapsed)) return;
            _slowestDetailTicks = elapsed;
            _slowestDetailId = string.IsNullOrEmpty(pId) ? "unknown" : pId;
        }

        public static void EndDeferredItem(string pKey, long started)
        {
            long elapsed = Elapsed(started);
            if (elapsed < 0L || !RuntimePerformanceDiagnosticRules.
                    ShouldReplaceSlowest(_slowestDeferredTicks, elapsed)) return;
            _slowestDeferredTicks = elapsed;
            _slowestDeferredKey = string.IsNullOrEmpty(pKey)
                ? "ordered"
                : pKey;
        }

        public static void EndAnnualStage(string pId, long started)
        {
            long elapsed = Elapsed(started);
            if (elapsed < 0L) return;
            if (RuntimePerformanceDiagnosticRules.ShouldReplaceSlowest(
                    _frameAnnualStageTicks, elapsed))
            {
                _frameAnnualStageTicks = elapsed;
                _frameAnnualStageId = string.IsNullOrEmpty(pId)
                    ? "annual_unknown"
                    : pId;
            }
            if (!_sampling || !RuntimePerformanceDiagnosticRules.
                    ShouldReplaceSlowest(_slowestAnnualStageTicks, elapsed))
                return;
            _slowestAnnualStageTicks = elapsed;
            _slowestAnnualStageId = string.IsNullOrEmpty(pId)
                ? "annual_unknown"
                : pId;
        }

        public static void EndContinuousStage(string pId, long pStarted)
        {
            long elapsed = Elapsed(pStarted);
            if (elapsed < 0L || !RuntimePerformanceDiagnosticRules.
                    ShouldReplaceSlowest(_frameContinuousStageTicks,
                        elapsed)) return;
            _frameContinuousStageTicks = elapsed;
            _frameContinuousStageId = string.IsNullOrEmpty(pId)
                ? "unknown"
                : pId;
        }

        public static void RecordRecent(int index, long exclusiveElapsed,
            bool outermost, long outerElapsed)
        {
            if (!_sampling || exclusiveElapsed < 0L || outerElapsed < 0L ||
                !RecentFeatureBenchmarkRules.IsValidIndex(index)) return;
            System.Threading.Interlocked.Add(ref RecentTicks[index],
                exclusiveElapsed);
            System.Threading.Interlocked.Increment(ref RecentCounts[index]);
            if (!outermost) return;
            System.Threading.Interlocked.Add(ref _recentTotalTicks,
                outerElapsed);
            System.Threading.Interlocked.Increment(ref _recentTotalCalls);
        }

        public static void FlushFrame()
        {
            long frameEnded = Stopwatch.GetTimestamp();
            long continuousFrameTicks = _frameStarted <= 0L
                ? -1L
                : Math.Max(0L, frameEnded - _frameStarted);
            RecordWorstFrame(continuousFrameTicks);
            if (continuousFrameTicks >= 0L)
                _intervalMapUpdateTicks += continuousFrameTicks;
            _frameStarted = 0L;
            _previousFrameEnded = frameEnded;
            if (!_sampling) return;
            int currentMode = CurrentIntervalMode();
            if (!RuntimePerformanceDiagnosticRules.ShouldFlushInterval(
                    _intervalMode, currentMode))
            {
                TerminateIntervalBaseline();
                _sampling = false;
                return;
            }
            if (!RuntimePerformanceDiagnosticRules.ShouldEmitTextLog(
                    Enabled(), Bench.bench_enabled))
            {
                ResetDeathInterval();
                _sampling = false;
                return;
            }
            long intervalEnded = frameEnded;
            long intervalTicks = _intervalStarted <= 0L
                ? 0L
                : Math.Max(0L, intervalEnded - _intervalStarted);
            AWActorPostDiagnosticSnapshot actorPostDiagnostics =
                AWCooperativeActorPostRunner.TakeDiagnostics();
            long intervalFrames = RuntimePerformanceDiagnosticRules.
                IntervalFrameCount(_intervalFrameStarted, _frame);
            double averageFps = RuntimePerformanceDiagnosticRules.
                AverageFramesPerSecond(intervalFrames, intervalTicks,
                    Stopwatch.Frequency);
            ReadProcessSnapshot(out long currentProcessCpuTicks,
                out long processPrivateBytes, out long processWorkingBytes,
                out int processHandles, out int processThreads);
            long processCpuTicks = _intervalProcessCpuStarted <= 0L ||
                                   currentProcessCpuTicks <= 0L
                ? 0L
                : Math.Max(0L,
                    currentProcessCpuTicks - _intervalProcessCpuStarted);
            double processCpuCores = RuntimePerformanceDiagnosticRules.
                AverageLogicalCoreUsage(processCpuTicks, intervalTicks,
                    TimeSpan.TicksPerSecond, Stopwatch.Frequency);
            long averageMapUpdateTicks = intervalFrames <= 0L
                ? 0L
                : _intervalMapUpdateTicks / intervalFrames;
            long averageOutsideMapTicks = intervalFrames <= 0L
                ? 0L
                : _intervalOutsideMapTicks / intervalFrames;
            long intervalUnaccountedTicks = Math.Max(0L,
                intervalTicks - _intervalMapUpdateTicks -
                _intervalOutsideMapTicks);
            int currentGc0 = GC.CollectionCount(0);
            int currentGc1 = GC.CollectionCount(1);
            int currentGc2 = GC.CollectionCount(2);
            int intervalGc0 = Math.Max(0, currentGc0 - _intervalGc0Started);
            int intervalGc1 = Math.Max(0, currentGc1 - _intervalGc1Started);
            int intervalGc2 = Math.Max(0, currentGc2 - _intervalGc2Started);
            long managedHeapBytes = GC.GetTotalMemory(false);
            ReadLiveWorldCounts(out int livePopulation,
                out int totalActorObjects, out int dyingActorObjects,
                out int actorDestroyQueue, out int schoolPendingDeaths,
                out int schoolPendingDescents, out int deferredRuntimeWork,
                out int kingdomRepairQueue, out int armyCount);
            AWAsyncDiagnosticsSnapshot asyncSnapshot =
                AWAsyncRuntime.SnapshotDiagnostics();
            AWAsyncCommitTimingSnapshot asyncCommitTiming =
                AWAsyncRuntime.TakeMainThreadCommitTiming();
            AWAsyncDiagnosticsSnapshot readSnapshot =
                AWHistoricalReadService.SnapshotDiagnostics();
            AWAsyncShadowSnapshot shadowSnapshot =
                AWAsyncShadowRuntime.Snapshot();
            AWPathDiagnostics pathDiagnostics =
                AWPathfindingBootstrap.PathDiagnostics;
            CityMilitaryThreatDiagnostics cityThreatDiagnostics =
                CityMilitaryThreatFacts.SnapshotDiagnostics();
            ArmyRtsAsyncPlanningDiagnostics rtsAsyncDiagnostics =
                ArmyRtsAsyncPlanningService.SnapshotDiagnostics();
            int actorPathActive =
                AWPathfindingBootstrap.Finder?.ActiveCount ?? 0;
            int actorPathQueue =
                AWPathfindingBootstrap.Finder?.QueueDepth ?? 0;
            int actorPathWorkers =
                AWPathfindingBootstrap.Finder?.WorkerCount ?? 0;
            int strategicPathActive = ArmyRouteProviderService.ActiveCount;
            ArmyRtsBenchmarkSnapshot armyRts =
                ArmyRtsBenchmark.Snapshot();
            AWIdleBehaviourThrottleDiagnosticSnapshot idleThrottle =
                AWIdleBehaviourThrottleDiagnostics.Snapshot();
            HistoricalSchoolDiagnosticSnapshot schoolDiagnostics =
                HistoricalSchoolDiagnostics.Snapshot();
            long frameTicks = Elapsed(_sampleFrameStarted);
            AWSchedulerStageDiagnosticSnapshot schedulerStages =
                AWSchedulerStageDiagnostics.TakeSnapshot()
                    .WithFrameTicks(frameTicks);
            long exclusivePathStep =
                RuntimePerformanceDiagnosticRules.ExclusiveTicks(
                    _pathStepTicks, _pathStepNestedTicks);
            long knownActor = _actorAiTicks + _pathSmoothTicks +
                              exclusivePathStep;
            long otherActor =
                RuntimePerformanceDiagnosticRules.UnaccountedTicks(
                    _actorWallTicks, knownActor);
            string actorTaskId = "none";
            long actorTaskTicks = 0L;
            int actorTaskCalls = 0;
            foreach (KeyValuePair<string, ActorTaskSample> item in
                     ActorTaskSamples)
            {
                if (item.Value.Ticks <= actorTaskTicks) continue;
                actorTaskId = item.Key;
                actorTaskTicks = item.Value.Ticks;
                actorTaskCalls = item.Value.Calls;
            }
            int slowest = RecentFeatureBenchmarkSnapshotRules.SlowestIndex(
                RecentTicks);
            string slowestId = slowest >= 0
                ? RecentFeatureBenchmarkRules.IdForIndex(slowest)
                : "none";
            long slowestTicks = slowest >= 0 ? RecentTicks[slowest] : 0L;
            int slowestCalls = slowest >= 0 ? RecentCounts[slowest] : 0;
            int gc0Collections = Math.Max(0,
                GC.CollectionCount(0) - _sampleGc0Start);
            int gc1Collections = Math.Max(0,
                GC.CollectionCount(1) - _sampleGc1Start);
            int gc2Collections = Math.Max(0,
                GC.CollectionCount(2) - _sampleGc2Start);
            long managedHeapDelta = GC.GetTotalMemory(false) -
                                    _sampleManagedHeapStart;
            ModClass.LogInfo("[AW3 PERF] frame=" + _frame +
                " bench=" + (Bench.bench_enabled ? 1 : 0) +
                " interval_real_ms=" + Milliseconds(intervalTicks) +
                " interval_frames=" + intervalFrames +
                " avg_fps=" + averageFps.ToString("0.###",
                    CultureInfo.InvariantCulture) +
                " map_update_avg_ms=" + Milliseconds(averageMapUpdateTicks) +
                " outside_map_avg_ms=" +
                Milliseconds(averageOutsideMapTicks) +
                " interval_unaccounted_ms=" +
                Milliseconds(intervalUnaccountedTicks) +
                " process_cpu_cores=" + processCpuCores.ToString("0.###",
                    CultureInfo.InvariantCulture) +
                " managed_heap_mb=" + Megabytes(managedHeapBytes) +
                " process_private_mb=" + Megabytes(processPrivateBytes) +
                " process_working_mb=" + Megabytes(processWorkingBytes) +
                " process_handles=" + processHandles +
                " process_threads=" + processThreads +
                " interval_gc0_collections=" + intervalGc0 +
                " interval_gc1_collections=" + intervalGc1 +
                " interval_gc2_collections=" + intervalGc2 +
                " unity_time_scale=" +
                UnityEngine.Time.timeScale.ToString("0.###",
                    CultureInfo.InvariantCulture) +
                " unity_target_fps=" + UnityEngine.Application.targetFrameRate +
                " unity_vsync=" + UnityEngine.QualitySettings.vSyncCount +
                " school_db_sync_total=" +
                schoolDiagnostics.DbSyncDependencies +
                " school_sql_batches_total=" + schoolDiagnostics.SqlBatches +
                " school_sql_commit_total_ms=" +
                Milliseconds(schoolDiagnostics.SqlCommitTicks) +
                " live_population=" + livePopulation +
                " total_actor_objects=" + totalActorObjects +
                " dying_actor_objects=" + dyingActorObjects +
                " actor_destroy_queue=" + actorDestroyQueue +
                " school_pending_deaths=" + schoolPendingDeaths +
                " school_pending_descents=" + schoolPendingDescents +
                " deferred_runtime_work=" + deferredRuntimeWork +
                " deferred_runtime_detail=" +
                DeferredRuntimeWorkService.GetDiagnostics() +
                " native_authority=" +
                AWAuthorityCycleService.GetDiagnostics() +
                " kingdom_repair_queue=" + kingdomRepairQueue +
                " army_count=" + armyCount +
                " frame_ms=" + Milliseconds(frameTicks) +
                " scheduler_stage_ms=" +
                schedulerStages.FormatMilliseconds() +
                " scheduler_total_ms=" +
                Milliseconds(schedulerStages.SchedulerTicks) +
                " scheduler_unaccounted_ms=" +
                Milliseconds(schedulerStages.UnaccountedTicks) +
                " scheduler_host_unaccounted_ms=" +
                Milliseconds(schedulerStages.HostUnaccountedTicks) +
                 " simulation_coordinator=master_batch_runner" +
                " simulation_workers=" +
                AWSimulationWorkerPool.Instance.GetDiagnostics() +
                 " status_scheduler=vanilla_master_lifecycle" +
                " stack_effects=" +
                AWActiveStackEffectsUpdater.GetDiagnostics() +
                " inside_boat=" +
                AWInsideBoatActorIndex.GetDiagnostics() +
                " worst_frame=" + _worstFrameNumber +
                " worst_frame_ms=" + Milliseconds(_worstFrameTicks) +
                " worst_frame_stage=" + _worstFrameStageId +
                " worst_frame_stage_ms=" +
                Milliseconds(_worstFrameStageTicks) +
                " worst_frame_annual_stage=" +
                _worstFrameAnnualStageId +
                " worst_frame_annual_stage_ms=" +
                Milliseconds(_worstFrameAnnualStageTicks) +
                " worst_frame_buckets=" + BuildWorstFrameBreakdown() +
                " worst_frame_gc=" + _worstFrameGcCount +
                " actor_ms=" + Milliseconds(_actorWallTicks) +
                " actor_post_worker_ms=" +
                Milliseconds(actorPostDiagnostics.WorkerTicks) +
                " actor_post_commit_ms=" +
                Milliseconds(actorPostDiagnostics.CommitTicks) +
                " actor_post_stages=" +
                AWCooperativeActorPostRunner.TakeStageBreakdown() +
                " p0_segments=" +
                AWCooperativeActorPostRunner.TakeP0Breakdown() +
                " p0_index=" +
                ArmyMilitaryMovementPriorityIndex.Diagnostics() +
                " prefix_segments=" +
                AncientWarfare3.patch.AW_FramePrioritySchedulerPatch
                    .TakePrefixBreakdown() +
                " reign_end=" +
                AncientWarfare3.core.lineage.PosthumousTitleService
                    .TakeReignBreakdown() +
                " reign_commit=" +
                AncientWarfare3.core.lineage.RulerTitleCommitService
                    .TakeCommitBreakdown() +
                " enemy_search_calls=" + actorPostDiagnostics.Calls +
                " enemy_search_candidates=" +
                actorPostDiagnostics.Candidates +
                " enemy_search_empty=" + actorPostDiagnostics.Empty +
                 " enemy_presence_cache=disabled" +
                " actor_ai_ms=" + Milliseconds(_actorAiTicks) +
                " actor_ai_calls=" + _actorAiCalls +
                " actor_task=" + actorTaskId +
                " actor_task_ms=" + Milliseconds(actorTaskTicks) +
                " actor_task_calls=" + actorTaskCalls +
                " idle_social_allowed=" + idleThrottle.SocializeAllowed +
                " idle_social_deferred=" + idleThrottle.SocializeDeferred +
                " idle_emote_allowed=" + idleThrottle.EmoteAllowed +
                " idle_emote_deferred=" + idleThrottle.EmoteDeferred +
                " idle_sleep_allowed=" + idleThrottle.SleepAllowed +
                " idle_sleep_deferred=" + idleThrottle.SleepDeferred +
                " idle_budget_rejected=" + idleThrottle.BudgetRejected +
                " path_smooth_ms=" + Milliseconds(_pathSmoothTicks) +
                " path_smooth_calls=" + _pathSmoothCalls +
                " path_smooth_slowest_actor=" + _slowestPathSmoothActorId +
                " path_smooth_slowest_task=" + _slowestPathSmoothTaskId +
                " path_smooth_slowest_ms=" +
                Milliseconds(_slowestPathSmoothTicks) +
                " path_step_exclusive_ms=" + Milliseconds(exclusivePathStep) +
                " path_step_calls=" + _pathStepCalls +
                " actor_other_ms=" + Milliseconds(otherActor) +
                ArmyRtsControllerFields() +
                " buildings_ms=" + Milliseconds(_buildingWallTicks) +
                " update_age_ms=" + Milliseconds(_updateAgeWallTicks) +
                " death_check_ms=" + Milliseconds(_deathCheckTicks) +
                " death_check_calls=" + _deathCheckCalls +
                " death_aw3_ms=" + Milliseconds(
                    Interlocked.Read(ref _deathEventTicks)) +
                " death_calls=" + Volatile.Read(ref _deathEventCalls) +
                DeathStageFields() +
                " aw3_total_ms=" + Milliseconds(_recentTotalTicks) +
                " aw3_total_calls=" + _recentTotalCalls +
                " aw3_slowest=" + slowestId +
                " aw3_slowest_ms=" + Milliseconds(slowestTicks) +
                " aw3_slowest_calls=" + slowestCalls +
                " async_state=" + AWAsyncRuntime.State +
                " async_worker_alive=" +
                (AWAsyncRuntime.WorkerAlive ? 1 : 0) +
                " async_workers=" + asyncSnapshot.WorkerCount +
                " async_world=" + asyncSnapshot.WorldGeneration +
                " async_queued=" + asyncSnapshot.Queued +
                " async_active=" + asyncSnapshot.Active +
                " async_completions=" + asyncSnapshot.Completions +
                " async_scheduled=" + asyncSnapshot.Scheduled +
                " async_merged=" + asyncSnapshot.Merged +
                " async_cancelled=" + asyncSnapshot.Cancelled +
                " async_stale=" + asyncSnapshot.Stale +
                " async_faulted=" + asyncSnapshot.Faulted +
                " async_committed=" + asyncSnapshot.Committed +
                " async_background_committed=" +
                asyncSnapshot.BackgroundCommitted +
                " async_rejected=" + asyncSnapshot.Rejected +
                " async_commit_slowest=" +
                asyncCommitTiming.SlowestKey +
                " async_commit_slowest_lane=" +
                asyncCommitTiming.SlowestLane +
                " async_commit_slowest_ms=" +
                Milliseconds(asyncCommitTiming.SlowestTicks) +
                " async_commit_total_ms=" +
                Milliseconds(asyncCommitTiming.TotalTicks) +
                " async_commit_calls=" + asyncCommitTiming.Calls +
                " async_db_pending=" + HistoricalWriteService.PendingCount +
                " async_db_terminal=" +
                (HistoricalWriteService.TerminalFaulted ? 1 : 0) +
                " async_db_earliest_uncommitted=" +
                HistoricalWriteService.EarliestUncommittedSequence +
                " async_read_worker_alive=" +
                (AWHistoricalReadService.WorkerAlive ? 1 : 0) +
                " async_read_connection_open=" +
                (AWHistoricalReadService.ConnectionOpen ? 1 : 0) +
                " async_read_queued=" + readSnapshot.Queued +
                " async_read_active=" + readSnapshot.Active +
                " async_read_completions=" + readSnapshot.Completions +
                " async_read_scheduled=" + readSnapshot.Scheduled +
                " async_read_merged=" + readSnapshot.Merged +
                " async_read_cancelled=" + readSnapshot.Cancelled +
                " async_read_stale=" + readSnapshot.Stale +
                " async_read_faulted=" + readSnapshot.Faulted +
                " async_read_committed=" + readSnapshot.Committed +
                " async_read_rejected=" + readSnapshot.Rejected +
                " async_shadow_comparisons=" +
                shadowSnapshot.Comparisons +
                " async_shadow_mismatches=" + shadowSnapshot.Mismatches +
                " async_traversal_captured=" +
                pathDiagnostics.TraversalChunksCaptured +
                " async_traversal_published=" +
                pathDiagnostics.TraversalBuildsPublished +
                " async_traversal_stale=" +
                pathDiagnostics.TraversalBuildsStale +
                " async_traversal_sync_fallback=" +
                pathDiagnostics.TraversalSyncFallbacks +
                " path_generated=" + pathDiagnostics.Generated +
                " path_reused=" + pathDiagnostics.Reused +
                " path_reused_running=" +
                pathDiagnostics.ReusedRunning +
                " path_reuse_probe=recorded=" +
                pathDiagnostics.ReuseProbeRecorded +
                ",probes=" + pathDiagnostics.ReuseProbeProbes +
                ",loose=" + pathDiagnostics.ReuseProbeLooseHits +
                ",strict=" + pathDiagnostics.ReuseProbeStrictHits +
                ",tracked=" + pathDiagnostics.ReuseProbeTracked +
                ",evictions=" + pathDiagnostics.ReuseProbeEvictions +
                " path_straight_segments=" +
                pathDiagnostics.StraightSegments +
                " path_cancelled=" + pathDiagnostics.Cancelled +
                " path_completed=" + pathDiagnostics.Completed +
                " path_failed=" + pathDiagnostics.Failed +
                " path_failed_by_reason=" + pathDiagnostics.FailedByReason() +
                " path_operational_requests=" +
                pathDiagnostics.OperationalRequests +
                " path_essential_requests=" +
                pathDiagnostics.EssentialTravelRequests +
                " path_ambient_requests=" +
                pathDiagnostics.AmbientRequests +
                " path_replaced_pending=" +
                pathDiagnostics.ReplacedPending +
                " path_replaced_running=" +
                pathDiagnostics.ReplacedRunning +
                " path_rejected=" + pathDiagnostics.Rejected +
                " path_operational_queue_high=" +
                pathDiagnostics.OperationalQueueHighWater +
                " path_essential_queue_high=" +
                pathDiagnostics.EssentialQueueHighWater +
                " path_ambient_queue_high=" +
                pathDiagnostics.AmbientQueueHighWater +
                " path_expanded_nodes=" + pathDiagnostics.ExpandedNodes +
                " path_owner_state=" + PathfindingOwnershipService.State +
                " presentation_visibility=" +
                AWPresentationVisibility.GetDiagnostics() +
                " actor_path_workers=" + actorPathWorkers +
                " actor_path_active=" + actorPathActive +
                " actor_path_queue=" + actorPathQueue +
                " actor_path_gates=" + AWPathMovementBridge.ActorGateCount +
                " city_threat_requests=" + cityThreatDiagnostics.Requests +
                " city_threat_physical_scans=" +
                cityThreatDiagnostics.PhysicalScans +
                " city_threat_cache_hits=" + cityThreatDiagnostics.Hits +
                " city_threat_invalidations=" +
                cityThreatDiagnostics.Invalidations +
                " city_threat_revision=" + cityThreatDiagnostics.Revision +
                " rts_async_snapshots=" + rtsAsyncDiagnostics.Snapshots +
                " rts_async_scheduled=" + rtsAsyncDiagnostics.Scheduled +
                " rts_async_completed=" + rtsAsyncDiagnostics.Completed +
                " rts_async_applied=" + rtsAsyncDiagnostics.Applied +
                " rts_async_rejected_stale=" +
                rtsAsyncDiagnostics.RejectedStale +
                " army_rts_active_routes=" + strategicPathActive +
                " army_rts_planner_passes=" +
                armyRts.PlannerPasses +
                " army_rts_missions=" + armyRts.Missions +
                " army_rts_target_comparisons=" +
                armyRts.TargetComparisons +
                " army_rts_target_agreements=" +
                armyRts.TargetAgreements +
                " army_rts_duplicate_reservations=" +
                armyRts.DuplicateReservations +
                " army_rts_routes_submitted=" +
                armyRts.RoutesSubmitted +
                " army_rts_routes_reused=" + armyRts.RoutesReused +
                " army_rts_routes_completed=" +
                armyRts.RoutesCompleted +
                " army_rts_routes_failed=" + armyRts.RoutesFailed +
                " army_rts_routes_cancelled=" +
                armyRts.RoutesCancelled +
                " army_rts_formation_corrections=" +
                armyRts.FormationCorrections +
                " army_rts_retreats=" + armyRts.Retreats +
                " army_rts_replans=" + armyRts.Replans +
                " army_rts_no_progress_ms=" +
                armyRts.NoProgressMilliseconds +
                " transport_diag=" +
                ArmyRtsTransportDiagnostics.Snapshot() +
                " detail=" + _slowestDetailId +
                " detail_ms=" + Milliseconds(_slowestDetailTicks) +
                " deferred_key=" + _slowestDeferredKey +
                " deferred_item_ms=" +
                Milliseconds(_slowestDeferredTicks) +
                " annual_stage=" + _slowestAnnualStageId +
                " annual_stage_ms=" +
                Milliseconds(_slowestAnnualStageTicks) +
                " authority_stage=" + _slowestAuthorityStageId +
                " authority_stage_ms=" +
                Milliseconds(_slowestAuthorityStageTicks) +
                " authority_steps=" +
                AWAuthorityCycleService.TakeAuthorityBreakdown() +
                " deferred_prefix_ms=" +
                DeferredRuntimeWorkService.TakePrefixCostDiagnostics() +
                " gc0_collections=" + gc0Collections +
                " gc1_collections=" + gc1Collections +
                " gc2_collections=" + gc2Collections +
                " managed_heap_delta_kb=" + Kilobytes(managedHeapDelta) +
                RaceMetricFields(ActorRacePerformanceMetric.ActorAi,
                    "actor_ai") +
                RaceMetricFields(ActorRacePerformanceMetric.PathSubmit,
                    "path_submit") +
                RaceMetricFields(ActorRacePerformanceMetric.PathSmooth,
                    "path_smooth") +
                RaceMetricFields(ActorRacePerformanceMetric.PathStep,
                    "path_step") +
                RaceMetricFields(ActorRacePerformanceMetric.UpdateAge,
                    "update_age") +
                RaceMetricFields(ActorRacePerformanceMetric.MainSprite,
                    "main_sprite"));
            _intervalStarted = intervalEnded;
            _intervalFrameStarted = _frame;
            _intervalProcessCpuStarted = currentProcessCpuTicks;
            _intervalMapUpdateTicks = 0L;
            _intervalOutsideMapTicks = 0L;
            _intervalGc0Started = currentGc0;
            _intervalGc1Started = currentGc1;
            _intervalGc2Started = currentGc2;
            ResetWorstFrameInterval();
            ResetDeathInterval();
            _sampling = false;
        }

        private static string DeathStageFields()
        {
            int slowestIndex = -1;
            long slowestTicks = 0L;
            var details = new StringBuilder();
            for (int i = 0; i < DeathStageTicks.Length; i++)
            {
                long ticks = Interlocked.Read(ref DeathStageTicks[i]);
                int count = Volatile.Read(ref DeathStageCounts[i]);
                if (ticks > slowestTicks)
                {
                    slowestTicks = ticks;
                    slowestIndex = i;
                }
                if (count <= 0) continue;
                if (details.Length > 0) details.Append(',');
                ActorDeathPerformanceStage stage =
                    (ActorDeathPerformanceStage)i;
                details.Append(ActorDeathPerformanceRules.Id(stage));
                details.Append(':');
                details.Append(Milliseconds(ticks));
                details.Append('/');
                details.Append(count);
            }
            string slowest = slowestIndex < 0
                ? "none"
                : ActorDeathPerformanceRules.Id(
                    (ActorDeathPerformanceStage)slowestIndex);
            return " death_slowest=" + slowest +
                   " death_slowest_ms=" + Milliseconds(slowestTicks) +
                   " death_stages=" +
                   (details.Length == 0 ? "none" : details.ToString());
        }

        private static string ArmyRtsControllerFields()
        {
            int slowestIndex = -1;
            long slowestTicks = 0L;
            var details = new StringBuilder();
            for (int i = 0; i < ArmyRtsControllerTicks.Length; i++)
            {
                long ticks = ArmyRtsControllerTicks[i];
                int count = ArmyRtsControllerCounts[i];
                if (ticks > slowestTicks)
                {
                    slowestTicks = ticks;
                    slowestIndex = i;
                }
                if (count <= 0) continue;
                if (details.Length > 0) details.Append(',');
                ArmyRtsControllerPerformanceStage stage =
                    (ArmyRtsControllerPerformanceStage)i;
                details.Append(ArmyRtsControllerPerformanceRules.Id(stage));
                details.Append(':');
                details.Append(Milliseconds(ticks));
                details.Append('/');
                details.Append(count);
            }
            string slowest = slowestIndex < 0
                ? "none"
                : ArmyRtsControllerPerformanceRules.Id(
                    (ArmyRtsControllerPerformanceStage)slowestIndex);
            return " army_rts_controller_slowest=" + slowest +
                   " army_rts_controller_slowest_ms=" +
                   Milliseconds(slowestTicks) +
                   " army_rts_controller_stages=" +
                   (details.Length == 0 ? "none" : details.ToString());
        }

        private static void ResetDeathInterval()
        {
            Interlocked.Exchange(ref _deathEventTicks, 0L);
            Interlocked.Exchange(ref _deathEventCalls, 0);
            for (int i = 0; i < DeathStageTicks.Length; i++)
            {
                Interlocked.Exchange(ref DeathStageTicks[i], 0L);
                Interlocked.Exchange(ref DeathStageCounts[i], 0);
            }
        }

        private static void RecordActorTask(string pTaskId, long pElapsed)
        {
            if (string.IsNullOrEmpty(pTaskId) || pElapsed < 0L) return;
            if (!ActorTaskSamples.TryGetValue(pTaskId,
                    out ActorTaskSample sample))
            {
                sample = new ActorTaskSample();
                ActorTaskSamples[pTaskId] = sample;
            }
            sample.Ticks += pElapsed;
            sample.Calls++;
        }

        private static bool Enabled()
        {
            return AWPerformanceSettings.EnablePerformanceDiagnostics;
        }

        private static int CurrentIntervalMode()
        {
            bool frameEligible = RuntimePerformanceDiagnosticRules.
                IsPerformanceFrameEligible(Config.game_loaded,
                    SmoothLoader.isLoading());
            return RuntimePerformanceDiagnosticRules.PerformanceIntervalMode(
                frameEligible, Enabled(), Bench.bench_enabled);
        }

        private static void TerminateIntervalBaseline()
        {
            _intervalStarted = 0L;
            _intervalFrameStarted = 0L;
            _intervalProcessCpuStarted = 0L;
            _intervalMapUpdateTicks = 0L;
            _intervalOutsideMapTicks = 0L;
            _previousFrameEnded = 0L;
            _intervalGc0Started = 0;
            _intervalGc1Started = 0;
            _intervalGc2Started = 0;
            _intervalMode = 0;
            _frameStarted = 0L;
            ResetWorstFrameInterval();
            ResetDeathInterval();
        }

        private static long CurrentProcessCpuTicks()
        {
            ReadProcessSnapshot(out long cpuTicks, out _, out _, out _, out _);
            return cpuTicks;
        }

        private static void ReadProcessSnapshot(out long pCpuTicks,
            out long pPrivateBytes, out long pWorkingBytes,
            out int pHandles, out int pThreads)
        {
            pCpuTicks = 0L;
            pPrivateBytes = 0L;
            pWorkingBytes = 0L;
            pHandles = 0;
            pThreads = 0;
            try { pWorkingBytes = Environment.WorkingSet; }
            catch { }

            Process process = null;
            try { process = Process.GetCurrentProcess(); }
            catch { }
            if (process == null) return;
            try
            {
                pCpuTicks = process.TotalProcessorTime.Ticks;
            }
            catch { }
            try { pPrivateBytes = process.PrivateMemorySize64; }
            catch { }
            try { pWorkingBytes = process.WorkingSet64; }
            catch { }
            try { pHandles = process.HandleCount; }
            catch { }
            try { pThreads = process.Threads.Count; }
            catch { }
            try { process.Dispose(); }
            catch { }
        }

        private static void RecordWorstFrame(long pFrameTicks)
        {
            if (pFrameTicks < 0L || !RuntimePerformanceDiagnosticRules.
                    ShouldReplaceSlowestFrame(_worstFrameTicks,
                        pFrameTicks)) return;
            _worstFrameNumber = _frame;
            _worstFrameTicks = pFrameTicks;
            _worstFrameStageId = _frameContinuousStageId;
            _worstFrameStageTicks = _frameContinuousStageTicks;
            _worstFrameAnnualStageId = _frameAnnualStageId;
            _worstFrameAnnualStageTicks = _frameAnnualStageTicks;
            Array.Copy(FrameBucketTicks, WorstFrameBucketTicks,
                FrameBucketTicks.Length);
            _worstFrameGcCount = Math.Max(0,
                GC.CollectionCount(0) - _frameGcStarted);
        }

        // 最坏帧里各桶的耗时,外加落在所有桶之外的余量。余量大就说明尖峰不在
        // 我们的代码里(原版模拟、渲染、或者被 GC 停了)。
        private static string BuildWorstFrameBreakdown()
        {
            var builder = new StringBuilder();
            long accounted = 0L;
            for (int i = 0; i < WorstFrameBucketTicks.Length; i++)
            {
                long ticks = WorstFrameBucketTicks[i];
                if (!IsNestedFrameBucket(i)) accounted += ticks;
                if (ticks <= 0L) continue;
                if (builder.Length > 0) builder.Append(',');
                builder.Append(FrameCostBucketNames[i]).Append(':')
                    .Append(Milliseconds(ticks));
            }

            long other = Math.Max(0L, _worstFrameTicks - accounted);
            if (builder.Length > 0) builder.Append(',');
            builder.Append("other:").Append(Milliseconds(other));
            return builder.ToString();
        }

        private static void ResetWorstFrameBuckets()
        {
            Array.Clear(WorstFrameBucketTicks, 0,
                WorstFrameBucketTicks.Length);
            _worstFrameGcCount = 0;
        }

        private static void ResetWorstFrameInterval()
        {
            _worstFrameNumber = 0L;
            _worstFrameTicks = 0L;
            _worstFrameStageId = "none";
            _worstFrameStageTicks = 0L;
            _worstFrameAnnualStageId = "none";
            _worstFrameAnnualStageTicks = 0L;
            ResetWorstFrameBuckets();
        }

        private static bool DeathDiagnosticsEnabled()
        {
            return Enabled() || Bench.bench_enabled;
        }

        private static bool AddElapsed(ref long target, long started)
        {
            long elapsed = Elapsed(started);
            if (elapsed < 0L) return false;
            target += elapsed;
            return true;
        }

        private static void UpdateMaximum(ref long pTarget, long pCandidate)
        {
            long current = Interlocked.Read(ref pTarget);
            while (pCandidate > current)
            {
                long observed = Interlocked.CompareExchange(ref pTarget,
                    pCandidate, current);
                if (observed == current) return;
                current = observed;
            }
        }

        private static long Elapsed(long started)
        {
            if (started == 0L) return -1L;
            long elapsed = Stopwatch.GetTimestamp() - started;
            return elapsed < 0L ? -1L : elapsed;
        }

        private static string Milliseconds(long ticks)
        {
            double value = ticks <= 0L
                ? 0d
                : ticks * 1000d / Stopwatch.Frequency;
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static void ReadLiveWorldCounts(out int pLivePopulation,
            out int pTotalActorObjects, out int pDyingActorObjects,
            out int pActorDestroyQueue, out int pSchoolPendingDeaths,
            out int pSchoolPendingDescents, out int pDeferredRuntimeWork,
            out int pKingdomRepairQueue, out int pArmyCount)
        {
            pLivePopulation = 0;
            pTotalActorObjects = 0;
            pDyingActorObjects = 0;
            pActorDestroyQueue = -1;
            pSchoolPendingDeaths = 0;
            pSchoolPendingDescents = 0;
            pDeferredRuntimeWork = 0;
            pKingdomRepairQueue = 0;
            pArmyCount = 0;
            ActorManager units = null;
            try { units = World.world?.units; }
            catch { }
            try
            {
                pLivePopulation = units?.units_only_alive?.Count ?? 0;
            }
            catch { }
            try { pTotalActorObjects = units?.Count ?? 0; }
            catch { }
            try
            {
                pDyingActorObjects = units?.units_only_dying?.Count ?? 0;
            }
            catch { }
            try
            {
                pActorDestroyQueue = HistoricalSchoolActorDestroyQueue.Count;
            }
            catch { }
            try
            {
                pSchoolPendingDeaths =
                    SchoolMembershipService.PendingDeathCount;
            }
            catch { }
            try
            {
                pSchoolPendingDescents =
                    HistoricalSchoolDescentService.PendingDescentCount;
            }
            catch { }
            try
            {
                pDeferredRuntimeWork = DeferredRuntimeWorkService.PendingCount;
            }
            catch { }
            try
            {
                pKingdomRepairQueue =
                    ActorKingdomSafetyService.PendingRepairCount;
            }
            catch { }
            try { pArmyCount = World.world?.armies?.Count ?? 0; }
            catch { }
        }

        private static string Kilobytes(long pBytes)
        {
            return (pBytes / 1024d).ToString("0.###",
                CultureInfo.InvariantCulture);
        }

        private static string Megabytes(long pBytes)
        {
            return (pBytes / (1024d * 1024d)).ToString("0.###",
                CultureInfo.InvariantCulture);
        }

        private static string RaceMetricFields(
            ActorRacePerformanceMetric pMetric, string pName)
        {
            return RaceBucketFields(ActorRacePerformanceBucket.Xia, "xia",
                       pMetric, pName) +
                   RaceBucketFields(ActorRacePerformanceBucket.Other, "other",
                       pMetric, pName);
        }

        private static string RaceBucketFields(
            ActorRacePerformanceBucket pBucket, string pBucketName,
            ActorRacePerformanceMetric pMetric, string pMetricName)
        {
            int index = ActorRacePerformanceRules.Index(pBucket, pMetric);
            long ticks = ActorRaceTicks[index];
            int calls = ActorRaceCounts[index];
            double microseconds = ActorRacePerformanceRules.MicrosecondsPerCall(
                ticks, Stopwatch.Frequency, calls);
            string prefix = " " + pBucketName + "_" + pMetricName;
            return prefix + "_ms=" + Milliseconds(ticks) +
                   prefix + "_calls=" + calls +
                   prefix + "_us=" +
                   microseconds.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static void ResetSample()
        {
            _actorWallTicks = 0L;
            _actorAiTicks = 0L;
            _actorAiCalls = 0;
            ActorTaskSamples.Clear();
            _pathSmoothTicks = 0L;
            _pathSmoothCalls = 0;
            _slowestPathSmoothTicks = 0L;
            _slowestPathSmoothActorId = -1L;
            _slowestPathSmoothTaskId = "none";
            _pathStepTicks = 0L;
            _pathStepNestedTicks = 0L;
            _pathStepCalls = 0;
            _buildingWallTicks = 0L;
            _updateAgeWallTicks = 0L;
            _deathCheckTicks = 0L;
            _deathCheckCalls = 0;
            _recentTotalTicks = 0L;
            _recentTotalCalls = 0;
            _slowestDetailId = "none";
            _slowestDetailTicks = 0L;
            _slowestDeferredKey = "none";
            _slowestDeferredTicks = 0L;
            _slowestAnnualStageId = "none";
            _slowestAnnualStageTicks = 0L;
            _slowestAuthorityStageId = "none";
            _slowestAuthorityStageTicks = 0L;
            Array.Clear(RecentTicks, 0, RecentTicks.Length);
            Array.Clear(RecentCounts, 0, RecentCounts.Length);
            Array.Clear(ActorRaceTicks, 0, ActorRaceTicks.Length);
            Array.Clear(ActorRaceCounts, 0, ActorRaceCounts.Length);
            Array.Clear(ArmyRtsControllerTicks, 0,
                ArmyRtsControllerTicks.Length);
            Array.Clear(ArmyRtsControllerCounts, 0,
                ArmyRtsControllerCounts.Length);
        }
    }
}
