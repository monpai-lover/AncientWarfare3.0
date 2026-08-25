using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AncientWarfare3.core.pathfinding;
using UnityEngine;

namespace AncientWarfare3.core.performance
{
    internal static class AWSimulationTickBenchmark
    {
        internal const string TotalsGroupId = "aw3_tick_totals";
        internal const string PhasesGroupId = "aw3_tick_phases";
        internal const string ActorsGroupId = "aw3_tick_actors";
        internal const string PathfindingGroupId = "aw3_tick_pathfinding";
        internal const string BuildingsGroupId = "aw3_tick_buildings";
        internal const string WorldBehavioursGroupId =
            "aw3_tick_world_behaviours";

        internal const string TickTotalId = "tick_total";
        internal const string ActorsTotalId = "actors_total";
        internal const string PathfindingTotalId = "pathfinding_total";
        internal const string BuildingsTotalId = "buildings_total";
        internal const string WorldBehavioursTotalId =
            "world_behaviours_total";

        private const int HistoryCapacity = 64;
        private const string BenchmarkAllId = "Benchmark All";
        private const string TickToolId = "Benchmark AW3 Tick";
        private const string ActorsToolId = "Benchmark AW3 Tick Actors";
        private const string PathfindingToolId =
            "Benchmark AW3 Tick Pathfinding";
        private const string BuildingsToolId =
            "Benchmark AW3 Tick Buildings";
        private const string WorldBehavioursToolId =
            "Benchmark AW3 Tick World Beh";

        private static readonly Queue<AWSimulationTickSample> History =
            new Queue<AWSimulationTickSample>(HistoryCapacity);
        private static readonly Stack<TickCapture> CapturePool =
            new Stack<TickCapture>(2);
        private static readonly List<TickCapture> PendingCompleted =
            new List<TickCapture>(2);
        private static readonly BenchmarkGroupState TotalsGroup =
            new BenchmarkGroupState(TotalsGroupId);
        private static readonly BenchmarkGroupState PhasesGroup =
            new BenchmarkGroupState(PhasesGroupId);
        private static readonly BenchmarkGroupState ActorsGroup =
            new BenchmarkGroupState(ActorsGroupId);
        private static readonly BenchmarkGroupState PathfindingGroup =
            new BenchmarkGroupState(PathfindingGroupId);
        private static readonly BenchmarkGroupState BuildingsGroup =
            new BenchmarkGroupState(BuildingsGroupId);
        private static readonly BenchmarkGroupState WorldBehavioursGroup =
            new BenchmarkGroupState(WorldBehavioursGroupId);

        private static TickCapture _current;
        private static int _suspendDepth;
        private static bool _benchStateInitialized;
        private static bool _lastBenchEnabled;
        private static bool _debugToolsRegistered;

        internal static bool IsCapturing =>
            _current != null && !_current.Cancelled;
        internal static bool ShouldSplitActorPostJobs => IsCapturing;

        internal static void RecordActorJobMetric(string pId,
            double pSeconds, long pCounter)
        {
            TickCapture capture = _current;
            if (capture == null || capture.Cancelled || !Bench.bench_enabled)
                return;
            AddMetric(capture.ActorJobs, pId, Math.Max(0d, pSeconds), pCounter);
        }

        internal static void RecordActorBackgroundMetric(string pId,
            string pPhase, double pWorkerSeconds, double pBackgroundSeconds,
            long pCounter)
        {
            TickCapture capture = _current;
            if (capture == null || capture.Cancelled || !Bench.bench_enabled)
                return;
            double backgroundSeconds = Math.Max(0d, pBackgroundSeconds);
            capture.TotalSeconds += backgroundSeconds;
            capture.ActorsSeconds += backgroundSeconds;
            if (backgroundSeconds > 0d)
                AddMetric(capture.Phases, pPhase, backgroundSeconds, 1L);
            AddMetric(capture.ActorJobs, pId, Math.Max(0d, pWorkerSeconds), pCounter);
        }

        internal static void Initialize()
        {
            SyncCaptureState();
            TryRegisterDebugTools();
        }

        internal static void SyncCaptureState()
        {
            TryRegisterDebugTools();
            bool enabled = Bench.bench_enabled;
            AWPathfindingProfiler.SetEnabled(enabled);
            if (!_benchStateInitialized)
            {
                _benchStateInitialized = true;
                _lastBenchEnabled = enabled;
                if (enabled) ResetSession();
                return;
            }

            if (enabled == _lastBenchEnabled) return;
            _lastBenchEnabled = enabled;
            DiscardCaptures();
            if (enabled) ResetSession();
        }

        internal static void BeginTick(float pSimulatedSeconds,
            AWSimulationMode pMode)
        {
            if (!Bench.bench_enabled || _suspendDepth > 0) return;

            if (_current != null) ReturnCapture(_current);
            _current = RentCapture();
            _current.SimulatedSeconds = Math.Max(0f, pSimulatedSeconds);
            _current.StartFrame = Time.frameCount;
            _current.StartedAt = Time.realtimeSinceStartupAsDouble;
            _current.Mode = pMode;
            _current.PathfindingStart =
                AWPathfindingProfiler.CaptureSnapshot();
        }

        internal static void MarkTickCompleted()
        {
            if (_current == null) return;
            _current.EndFrame = Time.frameCount;
            _current.CompletedAt = Time.realtimeSinceStartupAsDouble;
            if (!Bench.bench_enabled) _current.Cancelled = true;
            PendingCompleted.Add(_current);
            _current = null;
        }

        internal static TickCapture CapturePhaseTarget()
        {
            return _current;
        }

        internal static void RecordPhase(TickCapture pTarget,
            string pPhase, double pElapsedMilliseconds)
        {
            if (!Bench.bench_enabled)
            {
                if (pTarget != null) pTarget.Cancelled = true;
                if (_current != null) _current.Cancelled = true;
                return;
            }

            TickCapture capture = pTarget ?? _current;
            if (capture == null || capture.Cancelled) return;

            double seconds = Math.Max(0d, pElapsedMilliseconds) / 1000d;
            capture.TotalSeconds += seconds;
            capture.MaxSliceSeconds = Math.Max(capture.MaxSliceSeconds,
                seconds);
            AddMetric(capture.Phases, pPhase, seconds, 1L);
            RecordSpecializedPhase(capture, pPhase, seconds);
        }

        internal static void RecordWorldBehaviour(string pAssetId,
            long pElapsedTimestampTicks)
        {
            TickCapture capture = _current;
            if (capture == null || capture.Cancelled ||
                !Bench.bench_enabled) return;
            double seconds = Math.Max(0L, pElapsedTimestampTicks) /
                             (double)System.Diagnostics.Stopwatch.Frequency;
            AddMetric(capture.WorldBehaviours,
                string.IsNullOrEmpty(pAssetId) ? "unknown" : pAssetId,
                seconds, 1L);
        }

        internal static void RecordBatchJobs<TBatch, TObject>(
            string pBenchmarkId, List<TBatch> pBatches)
            where TBatch : Batch<TObject>, new()
        {
            TickCapture capture = _current;
            if (capture == null || capture.Cancelled ||
                !Bench.bench_enabled) return;

            Dictionary<string, Metric> target;
            switch (pBenchmarkId)
            {
                case "actors":
                    target = capture.ActorJobs;
                    break;
                case "buildings":
                    target = capture.BuildingJobs;
                    break;
                default:
                    return;
            }

            for (int i = 0; i < pBatches.Count; i++)
            {
                TBatch batch = pBatches[i];
                RecordJobList(target, batch.jobs_pre);
                RecordJobList(target, batch.jobs_post);
            }
        }

        internal static void FlushCompleted()
        {
            if (PendingCompleted.Count == 0) return;
            for (int i = 0; i < PendingCompleted.Count; i++)
            {
                TickCapture capture = PendingCompleted[i];
                if (!capture.Cancelled && Bench.bench_enabled &&
                    _suspendDepth == 0)
                    Commit(capture);
                ReturnCapture(capture);
            }
            PendingCompleted.Clear();
        }

        internal static void AbortCurrentTick()
        {
            DiscardCaptures();
        }

        internal static void Suspend()
        {
            _suspendDepth++;
            DiscardCaptures();
        }

        internal static void Resume()
        {
            if (_suspendDepth <= 0)
                throw new InvalidOperationException(
                    "AW tick benchmark is not suspended.");
            _suspendDepth--;
        }

        internal static bool AppendReport(StringBuilder pBuilder,
            int pPhaseLimit = 8, int pDetailLimit = 6)
        {
            AWSimulationTickWindowStats stats = GetWindowStats();
            if (stats.Count == 0) return false;

            AWCooperativeSimulationRunner runner =
                AWCooperativeSimulationRunner.Instance;
            pBuilder.AppendLine()
                .Append("  [AWSimulationTickBenchmark]")
                .Append(" samples=").Append(stats.Count)
                .Append(" mode=").Append(stats.LastMode.ToString()
                    .ToLowerInvariant())
                .Append(" tick=")
                .Append(FormatMilliseconds(stats.AverageWorkSeconds))
                .Append(" max=")
                .Append(FormatMilliseconds(stats.MaximumWorkSeconds))
                .Append(" sliceMax=")
                .Append(FormatMilliseconds(stats.MaximumSliceSeconds))
                .Append(" delta=").Append(stats.AverageSimulatedSeconds
                    .ToString("0.000", CultureInfo.InvariantCulture))
                .Append('s')
                .Append(" frames=").Append(stats.AverageFrames
                    .ToString("0.00", CultureInfo.InvariantCulture))
                .Append(" latency=")
                .Append(FormatMilliseconds(stats.AverageLatencySeconds))
                .Append(" theoretical=").Append(stats
                    .TheoreticalTicksPerSecond.ToString("0.00",
                        CultureInfo.InvariantCulture))
                .Append("tps/").Append(stats.TheoreticalSpeed.ToString(
                    "0.00", CultureInfo.InvariantCulture)).Append('x')
                .Append(" actual=").Append(runner.ActualSpeed.ToString(
                    "0.00", CultureInfo.InvariantCulture)).Append('x')
                .AppendLine();

            AppendTopRows(pBuilder, "phases", PhasesGroupId, TickTotalId,
                pPhaseLimit);
            AppendTopRows(pBuilder, "actors", ActorsGroupId, ActorsTotalId,
                pDetailLimit);
            AppendTopRows(pBuilder, "pathfinding", PathfindingGroupId,
                PathfindingTotalId, pDetailLimit);
            AppendTopRows(pBuilder, "buildings", BuildingsGroupId,
                BuildingsTotalId, pDetailLimit);
            AppendTopRows(pBuilder, "world_beh", WorldBehavioursGroupId,
                WorldBehavioursTotalId, pDetailLimit);
            return true;
        }

        private static void Commit(TickCapture pCapture)
        {
            AddUnattributedOverhead(pCapture.ActorJobs,
                pCapture.ActorsSeconds);
            AddUnattributedOverhead(pCapture.BuildingJobs,
                pCapture.BuildingsSeconds);
            AddUnattributedOverhead(pCapture.WorldBehaviours,
                pCapture.WorldBehavioursSeconds);
            RecordPathfindingMetrics(pCapture);

            pCapture.SetTotal(TickTotalId, pCapture.TotalSeconds);
            pCapture.SetTotal(ActorsTotalId, pCapture.ActorsSeconds);
            pCapture.SetTotal(PathfindingTotalId,
                SumSeconds(pCapture.Pathfinding));
            pCapture.SetTotal(BuildingsTotalId,
                pCapture.BuildingsSeconds);
            pCapture.SetTotal(WorldBehavioursTotalId,
                pCapture.WorldBehavioursSeconds);

            int previousSamples = History.Count;
            PublishGroup(TotalsGroup, pCapture.Totals, previousSamples);
            PublishGroup(PhasesGroup, pCapture.Phases, previousSamples);
            PublishGroup(ActorsGroup, pCapture.ActorJobs, previousSamples);
            PublishGroup(PathfindingGroup, pCapture.Pathfinding,
                previousSamples);
            PublishGroup(BuildingsGroup, pCapture.BuildingJobs,
                previousSamples);
            PublishGroup(WorldBehavioursGroup,
                pCapture.WorldBehaviours, previousSamples);

            if (History.Count >= HistoryCapacity) History.Dequeue();
            History.Enqueue(new AWSimulationTickSample(
                pCapture.TotalSeconds, pCapture.MaxSliceSeconds,
                pCapture.SimulatedSeconds,
                Math.Max(1, pCapture.EndFrame - pCapture.StartFrame + 1),
                Math.Max(0d, pCapture.CompletedAt - pCapture.StartedAt),
                pCapture.Mode));
        }

        private static void RecordSpecializedPhase(TickCapture pCapture,
            string pPhase, double pSeconds)
        {
            if (IsPhaseOrChild(pPhase, "vanilla.actors"))
            {
                pCapture.ActorsSeconds += pSeconds;
                RecordBatchStage(pCapture.ActorJobs, pPhase, pSeconds);
            }
            else if (IsPhaseOrChild(pPhase, "vanilla.buildings"))
            {
                pCapture.BuildingsSeconds += pSeconds;
                RecordBatchStage(pCapture.BuildingJobs, pPhase, pSeconds);
            }
            else if (string.Equals(pPhase, "vanilla.world_behaviours",
                         StringComparison.Ordinal))
            {
                pCapture.WorldBehavioursSeconds += pSeconds;
            }
        }

        private static bool IsPhaseOrChild(string pPhase, string pRoot)
        {
            return string.Equals(pPhase, pRoot, StringComparison.Ordinal) ||
                   pPhase.StartsWith(pRoot + ".", StringComparison.Ordinal);
        }

        private static void RecordBatchStage(
            Dictionary<string, Metric> pTarget, string pPhase,
            double pSeconds)
        {
            string id = null;
            if (pPhase.EndsWith(".parallel", StringComparison.Ordinal))
                id = "update_jobs_parallel";
            else if (pPhase.EndsWith(".clear_parallel_results",
                         StringComparison.Ordinal))
                id = "clear_parallel_results";
            else if (pPhase.EndsWith(".apply_parallel_results",
                         StringComparison.Ordinal))
                id = "apply_parallel_results";
            if (id != null) AddMetric(pTarget, id, pSeconds, 1L);
        }

        private static void RecordJobList<TObject>(
            Dictionary<string, Metric> pTarget, List<Job<TObject>> pJobs)
        {
            for (int i = 0; i < pJobs.Count; i++)
            {
                Job<TObject> job = pJobs[i];
                AddMetric(pTarget, job.id,
                    Math.Max(0d, job.time_benchmark), job.counter);
            }
        }

        private static void AddUnattributedOverhead(
            Dictionary<string, Metric> pEntries, double pTotalSeconds)
        {
            double detailedSeconds = 0d;
            foreach (Metric metric in pEntries.Values)
                detailedSeconds += metric.Seconds;
            double overhead = pTotalSeconds - detailedSeconds;
            if (overhead > 0.0000001d)
                AddMetric(pEntries, "unattributed_overhead", overhead, 1L);
        }

        private static void AddMetric(Dictionary<string, Metric> pEntries,
            string pId, double pSeconds, long pCounter)
        {
            if (!pEntries.TryGetValue(pId, out Metric metric))
            {
                metric = new Metric();
                pEntries.Add(pId, metric);
            }
            metric.Seconds += pSeconds;
            metric.Counter += pCounter;
        }

        private static double SumSeconds(Dictionary<string, Metric> pEntries)
        {
            double total = 0d;
            foreach (Metric metric in pEntries.Values)
                total += Math.Max(0d, metric.Seconds);
            return total;
        }

        private static void RecordPathfindingMetrics(TickCapture pCapture)
        {
            AWPathfindingProfiler.AWPathfindingProfilerSnapshot delta =
                AWPathfindingProfiler.CaptureSnapshot().DeltaFrom(
                    pCapture.PathfindingStart);
            AddPathfindingMetric(pCapture.Pathfinding, "reuse", delta.Reuse);
            AddPathfindingMetric(pCapture.Pathfinding, "reuse_miss",
                delta.ReuseMiss);
            AddPathfindingMetric(pCapture.Pathfinding, "create", delta.Create);
            AddPathfindingMetric(pCapture.Pathfinding, "task_create",
                delta.TaskCreate);
            AddPathfindingMetric(pCapture.Pathfinding, "cancel", delta.Cancel);
            AddPathfindingMetric(pCapture.Pathfinding, "cancel_empty",
                delta.CancelEmpty);
            AddPathfindingMetric(pCapture.Pathfinding, "enqueue", delta.Enqueue);
            AddPathfindingMetric(pCapture.Pathfinding, "queue_wait",
                delta.QueueWait);
            AddPathfindingMetric(pCapture.Pathfinding, "background_path",
                delta.BackgroundPath);
        }

        private static void AddPathfindingMetric(
            Dictionary<string, Metric> pTarget, string pId,
            AWPathfindingProfiler.AWPathfindingMetricSnapshot pMetric)
        {
            if (pMetric.Counter == 0L && pMetric.ElapsedTicks == 0L) return;
            AddMetric(pTarget, pId, pMetric.Seconds, pMetric.Counter);
        }

        private static void PublishGroup(BenchmarkGroupState pState,
            Dictionary<string, Metric> pEntries, int pPreviousSamples)
        {
            foreach (string id in pEntries.Keys)
                if (pState.KnownEntries.Add(id))
                    SeedMissingSamples(pState.GroupId, id,
                        pPreviousSamples);

            foreach (string id in pState.KnownEntries)
            {
                pEntries.TryGetValue(id, out Metric metric);
                Bench.benchSave(id, metric?.Seconds ?? 0d,
                    ClampCounter(metric?.Counter ?? 0L), pState.GroupId);
                Bench.saveAverageCounter(id, pState.GroupId);
            }
        }

        private static void SeedMissingSamples(string pGroupId, string pId,
            int pCount)
        {
            for (int i = 0; i < pCount; i++)
            {
                Bench.benchSave(pId, 0d, 0, pGroupId);
                Bench.saveAverageCounter(pId, pGroupId);
            }
        }

        private static int ClampCounter(long pValue)
        {
            if (pValue <= 0L) return 0;
            return pValue >= int.MaxValue ? int.MaxValue : (int)pValue;
        }

        private static TickCapture RentCapture()
        {
            TickCapture capture = CapturePool.Count > 0
                ? CapturePool.Pop()
                : new TickCapture();
            capture.Reset();
            return capture;
        }

        private static void ReturnCapture(TickCapture pCapture)
        {
            pCapture.Reset();
            CapturePool.Push(pCapture);
        }

        private static void DiscardCaptures()
        {
            if (_current != null)
            {
                ReturnCapture(_current);
                _current = null;
            }
            for (int i = 0; i < PendingCompleted.Count; i++)
                ReturnCapture(PendingCompleted[i]);
            PendingCompleted.Clear();
        }

        private static void ResetSession()
        {
            DiscardCaptures();
            foreach (TickCapture capture in CapturePool)
                capture.ClearMetricKeys();
            History.Clear();
            ResetGroup(TotalsGroup);
            ResetGroup(PhasesGroup);
            ResetGroup(ActorsGroup);
            ResetGroup(PathfindingGroup);
            ResetGroup(BuildingsGroup);
            ResetGroup(WorldBehavioursGroup);
        }

        private static void ResetGroup(BenchmarkGroupState pState)
        {
            pState.KnownEntries.Clear();
            Bench.getGroup(pState.GroupId).dict_data.Clear();
        }

        private static AWSimulationTickWindowStats GetWindowStats()
        {
            var accumulator = new AWSimulationTickWindowAccumulator();
            foreach (AWSimulationTickSample snapshot in History)
                accumulator.Add(snapshot);
            return accumulator.GetStats();
        }

        internal static bool TryRegisterDebugTools()
        {
            if (_debugToolsRegistered) return true;
            DebugToolLibrary library = AssetManager.debug_tool_library;
            if (library == null) return false;
            DebugToolAsset template = library.get(BenchmarkAllId);
            if (template?.action_2 == null) return false;

            RegisterDebugTool(library, template, TickToolId, PhasesGroupId,
                TickTotalId);
            RegisterDebugTool(library, template, ActorsToolId, ActorsGroupId,
                ActorsTotalId);
            RegisterDebugTool(library, template, PathfindingToolId,
                PathfindingGroupId, PathfindingTotalId);
            RegisterDebugTool(library, template, BuildingsToolId,
                BuildingsGroupId, BuildingsTotalId);
            RegisterDebugTool(library, template, WorldBehavioursToolId,
                WorldBehavioursGroupId, WorldBehavioursTotalId);
            _debugToolsRegistered = true;
            return true;
        }

        private static void RegisterDebugTool(DebugToolLibrary pLibrary,
            DebugToolAsset pTemplate, string pId, string pGroupId,
            string pTotalId)
        {
            if (pLibrary.has(pId)) return;
            pLibrary.add(new DebugToolAsset
            {
                id = pId,
                name = pId,
                type = DebugToolType.Benchmarks,
                priority = 2,
                benchmark_group_id = pGroupId,
                benchmark_total = pTotalId,
                benchmark_total_group = TotalsGroupId,
                split_benchmark = true,
                show_benchmark_buttons = true,
                update_timeout = 0.2f,
                action_start = ConfigureDebugTool,
                action_1 = ShowDebugHeader,
                action_2 = pTemplate.action_2
            });
        }

        private static void ConfigureDebugTool(DebugTool pTool)
        {
            pTool.sort_order_reversed = false;
            pTool.sort_by_names = false;
            pTool.sort_by_values = true;
            pTool.show_averages = true;
            pTool.hide_zeroes = true;
            pTool.show_counter = true;
            pTool.show_max = true;
            pTool.state = DebugToolState.Percent;
            pTool.paused = false;
            pTool.percentage_slowest = false;
        }

        private static void ShowDebugHeader(DebugTool pTool)
        {
            AWSimulationTickWindowStats stats = GetWindowStats();
            if (stats.Count == 0)
            {
                pTool.setText("tick samples:", 0);
                pTool.setSeparator();
                return;
            }

            double groupSeconds = GetAverage(pTool.asset.benchmark_total,
                TotalsGroupId);
            double share = stats.AverageWorkSeconds > 0d
                ? groupSeconds / stats.AverageWorkSeconds * 100d
                : 0d;
            pTool.setText("tick samples:", stats.Count);
            pTool.setText("tick work:",
                FormatMilliseconds(stats.AverageWorkSeconds));
            pTool.setText("tick max:",
                FormatMilliseconds(stats.MaximumWorkSeconds));
            pTool.setText("slice max:",
                FormatMilliseconds(stats.MaximumSliceSeconds));
            pTool.setText("simulated:", stats.AverageSimulatedSeconds
                .ToString("0.000", CultureInfo.InvariantCulture) + " s");
            pTool.setText("frames/tick:", stats.AverageFrames.ToString(
                "0.00", CultureInfo.InvariantCulture));
            pTool.setText("theoretical:", stats.TheoreticalTicksPerSecond
                .ToString("0.00", CultureInfo.InvariantCulture) + " TPS | " +
                stats.TheoreticalSpeed.ToString("0.00",
                    CultureInfo.InvariantCulture) + "x");
            if (!pTool.asset.benchmark_total.Equals(TickTotalId,
                    StringComparison.Ordinal))
            {
                pTool.setText("group work:",
                    FormatMilliseconds(groupSeconds));
                pTool.setText("share of tick:", share.ToString("0.0",
                                  CultureInfo.InvariantCulture) + "%",
                    (float)share, true);
            }
            pTool.setSeparator();
        }

        private static void AppendTopRows(StringBuilder pBuilder,
            string pLabel, string pGroupId, string pTotalId, int pLimit)
        {
            double total = GetAverage(pTotalId, TotalsGroupId);
            if (total <= 0d) return;

            var rows = new List<BenchmarkRow>();
            foreach (ToolBenchmarkData data in
                     Bench.getGroup(pGroupId).dict_data.Values)
            {
                double seconds = data.getAverage();
                if (double.IsNaN(seconds) || double.IsInfinity(seconds) ||
                    seconds <= 0.0000001d) continue;
                rows.Add(new BenchmarkRow(data.id, seconds,
                    data.getAverageCount()));
            }
            rows.Sort(CompareRowsDescending);
            int count = Math.Min(pLimit, rows.Count);
            if (count == 0) return;

            pBuilder.Append("    ").Append(pLabel).Append(": ");
            for (int i = 0; i < count; i++)
            {
                if (i > 0) pBuilder.Append(", ");
                BenchmarkRow row = rows[i];
                pBuilder.Append(row.Id).Append('=')
                    .Append(FormatMilliseconds(row.Seconds)).Append('(')
                    .Append((row.Seconds / total * 100d).ToString("0.0",
                        CultureInfo.InvariantCulture)).Append('%');
                if (row.Counter > 0L)
                    pBuilder.Append('/').Append(row.Counter);
                pBuilder.Append(')');
            }
            pBuilder.AppendLine();
        }

        private static int CompareRowsDescending(BenchmarkRow pLeft,
            BenchmarkRow pRight)
        {
            return pRight.Seconds.CompareTo(pLeft.Seconds);
        }

        private static double GetAverage(string pId, string pGroupId)
        {
            double value = Bench.getBenchResultAsDouble(pId, pGroupId,
                true);
            return double.IsNaN(value) || double.IsInfinity(value) ||
                   value < 0d
                ? 0d
                : value;
        }

        private static string FormatMilliseconds(double pSeconds)
        {
            return (pSeconds * 1000d).ToString("0.000",
                       CultureInfo.InvariantCulture) + "ms";
        }

        internal sealed class TickCapture
        {
            internal readonly Dictionary<string, Metric> Totals =
                new Dictionary<string, Metric>(StringComparer.Ordinal);
            internal readonly Dictionary<string, Metric> Phases =
                new Dictionary<string, Metric>(StringComparer.Ordinal);
            internal readonly Dictionary<string, Metric> ActorJobs =
                new Dictionary<string, Metric>(StringComparer.Ordinal);
            internal readonly Dictionary<string, Metric> Pathfinding =
                new Dictionary<string, Metric>(StringComparer.Ordinal);
            internal readonly Dictionary<string, Metric> BuildingJobs =
                new Dictionary<string, Metric>(StringComparer.Ordinal);
            internal readonly Dictionary<string, Metric> WorldBehaviours =
                new Dictionary<string, Metric>(StringComparer.Ordinal);

            internal float SimulatedSeconds;
            internal int StartFrame;
            internal int EndFrame;
            internal double StartedAt;
            internal double CompletedAt;
            internal double TotalSeconds;
            internal double MaxSliceSeconds;
            internal double ActorsSeconds;
            internal double BuildingsSeconds;
            internal double WorldBehavioursSeconds;
            internal AWSimulationMode Mode;
            internal bool Cancelled;
            internal AWPathfindingProfiler.AWPathfindingProfilerSnapshot
                PathfindingStart;

            internal void SetTotal(string pId, double pSeconds)
            {
                if (!Totals.TryGetValue(pId, out Metric metric))
                {
                    metric = new Metric();
                    Totals.Add(pId, metric);
                }
                metric.Seconds = pSeconds;
                metric.Counter = 1L;
            }

            internal void ClearMetricKeys()
            {
                Totals.Clear();
                Phases.Clear();
                ActorJobs.Clear();
                Pathfinding.Clear();
                BuildingJobs.Clear();
                WorldBehaviours.Clear();
            }

            internal void Reset()
            {
                ResetMetrics(Totals);
                ResetMetrics(Phases);
                ResetMetrics(ActorJobs);
                ResetMetrics(Pathfinding);
                ResetMetrics(BuildingJobs);
                ResetMetrics(WorldBehaviours);
                SimulatedSeconds = 0f;
                StartFrame = 0;
                EndFrame = 0;
                StartedAt = 0d;
                CompletedAt = 0d;
                TotalSeconds = 0d;
                MaxSliceSeconds = 0d;
                ActorsSeconds = 0d;
                BuildingsSeconds = 0d;
                WorldBehavioursSeconds = 0d;
                Mode = AWSimulationMode.Native;
                Cancelled = false;
                PathfindingStart = default;
            }

            private static void ResetMetrics(
                Dictionary<string, Metric> pEntries)
            {
                foreach (Metric metric in pEntries.Values)
                {
                    metric.Seconds = 0d;
                    metric.Counter = 0L;
                }
            }
        }

        internal sealed class Metric
        {
            internal double Seconds;
            internal long Counter;
        }

        private sealed class BenchmarkGroupState
        {
            internal BenchmarkGroupState(string pGroupId)
            {
                GroupId = pGroupId;
            }

            internal string GroupId { get; }
            internal HashSet<string> KnownEntries { get; } =
                new HashSet<string>(StringComparer.Ordinal);
        }

        private readonly struct BenchmarkRow
        {
            internal BenchmarkRow(string pId, double pSeconds, long pCounter)
            {
                Id = pId;
                Seconds = pSeconds;
                Counter = pCounter;
            }

            internal string Id { get; }
            internal double Seconds { get; }
            internal long Counter { get; }
        }
    }
}
