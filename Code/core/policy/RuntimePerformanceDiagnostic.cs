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
        private static readonly long[] ActorBatchTicks =
            new long[ActorBatchPerformanceRules.StageCount];
        private static readonly int[] ActorBatchCounts =
            new int[ActorBatchTicks.Length];
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
            _sampling = RuntimePerformanceDiagnosticRules.ShouldSample(
                RuntimePerformanceDiagnosticRules.ShouldEnableDetailedSampling(
                    Enabled(), Bench.bench_enabled), _frame);
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

        public static long BeginActorBatch(ActorBatchPerformanceStage pStage)
        {
            return _sampling && ActorBatchPerformanceRules.IsValid(pStage)
                ? Stopwatch.GetTimestamp()
                : 0L;
        }

        public static void EndActorBatch(ActorBatchPerformanceStage pStage,
            long pStarted)
        {
            long elapsed = Elapsed(pStarted);
            if (elapsed < 0L || !ActorBatchPerformanceRules.IsValid(pStage))
                return;
            int index = (int)pStage;
            ActorBatchTicks[index] += elapsed;
            ActorBatchCounts[index]++;
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
            if (elapsed < 0L || !RuntimePerformanceDiagnosticRules.
                    ShouldReplaceSlowest(_slowestAnnualStageTicks, elapsed))
                return;
            _slowestAnnualStageTicks = elapsed;
            _slowestAnnualStageId = string.IsNullOrEmpty(pId)
                ? "annual_unknown"
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
            if (!_sampling) return;
            if (!RuntimePerformanceDiagnosticRules.ShouldEmitTextLog(
                    Enabled(), Bench.bench_enabled))
            {
                ResetDeathInterval();
                _sampling = false;
                return;
            }
            AWAsyncDiagnosticsSnapshot asyncSnapshot =
                AWAsyncRuntime.SnapshotDiagnostics();
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
            int strategicPathActive = ArmyRouteProviderService.ActiveCount;
            ArmyRtsBenchmarkSnapshot armyRts =
                ArmyRtsBenchmark.Snapshot();
            long frameTicks = Elapsed(_sampleFrameStarted);
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
                " frame_ms=" + Milliseconds(frameTicks) +
                " actor_ms=" + Milliseconds(_actorWallTicks) +
                " actor_ai_ms=" + Milliseconds(_actorAiTicks) +
                " actor_ai_calls=" + _actorAiCalls +
                " actor_task=" + actorTaskId +
                " actor_task_ms=" + Milliseconds(actorTaskTicks) +
                " actor_task_calls=" + actorTaskCalls +
                " path_smooth_ms=" + Milliseconds(_pathSmoothTicks) +
                " path_smooth_calls=" + _pathSmoothCalls +
                " path_smooth_slowest_actor=" + _slowestPathSmoothActorId +
                " path_smooth_slowest_task=" + _slowestPathSmoothTaskId +
                " path_smooth_slowest_ms=" +
                Milliseconds(_slowestPathSmoothTicks) +
                " path_step_exclusive_ms=" + Milliseconds(exclusivePathStep) +
                " path_step_calls=" + _pathStepCalls +
                " actor_other_ms=" + Milliseconds(otherActor) +
                ActorBatchFields() +
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
                " async_rejected=" + asyncSnapshot.Rejected +
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
                " path_cancelled=" + pathDiagnostics.Cancelled +
                " path_completed=" + pathDiagnostics.Completed +
                " path_failed=" + pathDiagnostics.Failed +
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
                " actor_path_active=" + actorPathActive +
                " actor_path_queue=" + actorPathQueue +
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
                " detail=" + _slowestDetailId +
                " detail_ms=" + Milliseconds(_slowestDetailTicks) +
                " deferred_key=" + _slowestDeferredKey +
                " deferred_item_ms=" +
                Milliseconds(_slowestDeferredTicks) +
                " annual_stage=" + _slowestAnnualStageId +
                " annual_stage_ms=" +
                Milliseconds(_slowestAnnualStageTicks) +
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

        private static string ActorBatchFields()
        {
            int slowestIndex = -1;
            long slowestTicks = 0L;
            var details = new StringBuilder();
            for (int i = 0; i < ActorBatchTicks.Length; i++)
            {
                long ticks = ActorBatchTicks[i];
                int count = ActorBatchCounts[i];
                if (ticks > slowestTicks)
                {
                    slowestTicks = ticks;
                    slowestIndex = i;
                }
                if (count <= 0) continue;
                if (details.Length > 0) details.Append(',');
                ActorBatchPerformanceStage stage =
                    (ActorBatchPerformanceStage)i;
                details.Append(ActorBatchPerformanceRules.Id(stage));
                details.Append(':');
                details.Append(Milliseconds(ticks));
                details.Append('/');
                details.Append(count);
            }
            string slowest = slowestIndex < 0
                ? "none"
                : ActorBatchPerformanceRules.Id(
                    (ActorBatchPerformanceStage)slowestIndex);
            return " actor_batch_slowest=" + slowest +
                   " actor_batch_slowest_ms=" + Milliseconds(slowestTicks) +
                   " actor_batch_slowest_calls=" +
                   (slowestIndex < 0 ? 0 : ActorBatchCounts[slowestIndex]) +
                   " actor_batch_stages=" +
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

        private static string Kilobytes(long pBytes)
        {
            return (pBytes / 1024d).ToString("0.###",
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
            Array.Clear(RecentTicks, 0, RecentTicks.Length);
            Array.Clear(RecentCounts, 0, RecentCounts.Length);
            Array.Clear(ActorRaceTicks, 0, ActorRaceTicks.Length);
            Array.Clear(ActorRaceCounts, 0, ActorRaceCounts.Length);
            Array.Clear(ActorBatchTicks, 0, ActorBatchTicks.Length);
            Array.Clear(ActorBatchCounts, 0, ActorBatchCounts.Length);
            Array.Clear(ArmyRtsControllerTicks, 0,
                ArmyRtsControllerTicks.Length);
            Array.Clear(ArmyRtsControllerCounts, 0,
                ArmyRtsControllerCounts.Length);
        }
    }
}
