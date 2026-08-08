using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;

namespace AncientWarfare3.core.performance
{
    internal enum AWSimulationDomain
    {
        Vanilla,
        Aw3Authority
    }

    internal static class AWFramePriorityGovernor
    {
        private const int BaselineWindowSize = 120;
        private const double BootstrapBaselineMilliseconds = 6d;

        private static readonly double[] BaselineSamples =
            new double[BaselineWindowSize];
        private static readonly double[] BaselineScratch =
            new double[BaselineWindowSize];
        private static readonly Dictionary<string, double> PhaseEstimates =
            new Dictionary<string, double>(StringComparer.Ordinal);

        private static int _frameId = -1;
        private static long _frameStartedAt;
        private static int _baselineSampleCount;
        private static int _baselineSampleCursor;
        private static double _baselineP90 = BootstrapBaselineMilliseconds;
        private static double _hostCpuMilliseconds;
        private static double _simulationCpuMilliseconds;
        private static double _vanillaCpuMilliseconds;
        private static double _aw3CpuMilliseconds;
        private static double _lastFrameDeltaMilliseconds;
        private static double _lastFrameSimulationMilliseconds;
        private static double _frameBudgetMilliseconds;
        private static int _lastVanillaRunFrame =
            -AWPerformanceSettings.StarvationFrameInterval;
        private static int _lastAw3RunFrame =
            -AWPerformanceSettings.StarvationFrameInterval;
        private static string _currentPhase = "idle";
        private static string _currentVanillaPhase = "idle";
        private static string _currentAw3Phase = "idle";
        private static string _longestPhase = string.Empty;
        private static double _longestPhaseMilliseconds;
        private static bool _faulted;
        private static string _faultMessage = string.Empty;
        private static bool _criticalHookInstalled;
        private static int _simulationPhaseDepth;

        public static long CyclesStarted { get; private set; }
        public static long CyclesCompleted { get; private set; }
        public static bool Faulted => _faulted;
        public static string FaultMessage => _faultMessage;
        public static bool CriticalHookInstalled => _criticalHookInstalled;
        public static bool IsExecutingSimulationPhase =>
            _simulationPhaseDepth > 0;
        public static string CurrentPhase => _currentPhase;
        public static string LongestPhase => _longestPhase;
        public static double LongestPhaseMilliseconds =>
            _longestPhaseMilliseconds;
        public static double FrameBudgetMilliseconds =>
            _frameBudgetMilliseconds;

        public static void Initialize()
        {
            JobConst.MAX_ELEMENTS = AWPerformanceSettings.SimulationBatchSize;
            BeginFrame();
        }

        public static void BeginFrame()
        {
            AWSimulationTickBenchmark.SyncCaptureState();
            int currentFrame = Time.frameCount;
            if (_frameId == currentFrame) return;
            if (_frameId >= 0) FinalizePreviousFrame();

            _frameId = currentFrame;
            _frameStartedAt = Stopwatch.GetTimestamp();
            _hostCpuMilliseconds = 0d;
            _simulationCpuMilliseconds = 0d;
            _vanillaCpuMilliseconds = 0d;
            _aw3CpuMilliseconds = 0d;
            RecalculateBudget();
        }

        public static long StartHostMeasurement()
        {
            BeginFrame();
            return Stopwatch.GetTimestamp();
        }

        public static void EndHostMeasurement(long pStartedAt)
        {
            _hostCpuMilliseconds += ElapsedMilliseconds(pStartedAt);
        }

        public static bool CanRun(string pPhase)
        {
            return CanRun(AWSimulationDomain.Vanilla, pPhase);
        }

        public static bool CanRun(AWSimulationDomain pDomain,
            string pPhase)
        {
            BeginFrame();
            if (_faulted) return false;

            double estimate = PhaseEstimates.TryGetValue(pPhase,
                out double previous)
                ? previous
                : AWPerformanceSettings.MinimumSliceMilliseconds;
            double currentFrameElapsed =
                ElapsedMilliseconds(_frameStartedAt);
            double targetMilliseconds =
                1000d / AWPerformanceSettings.TargetRenderFps;
            if (currentFrameElapsed >= targetMilliseconds -
                AWPerformanceSettings.RenderReserveMilliseconds)
                return CanUseStarvationSlice(pDomain);

            double remaining = _frameBudgetMilliseconds -
                               _simulationCpuMilliseconds;
            if (remaining >= Math.Max(
                    AWPerformanceSettings.MinimumSliceMilliseconds,
                    estimate))
                return true;
            return CanUseStarvationSlice(pDomain);
        }

        public static double GetRemainingSimulationBudgetMilliseconds()
        {
            BeginFrame();
            double targetMilliseconds =
                1000d / AWPerformanceSettings.TargetRenderFps;
            double deadlineRemaining = targetMilliseconds -
                AWPerformanceSettings.RenderReserveMilliseconds -
                ElapsedMilliseconds(_frameStartedAt);
            double budgetRemaining = _frameBudgetMilliseconds -
                                     _simulationCpuMilliseconds;
            return Math.Max(0d,
                Math.Min(deadlineRemaining, budgetRemaining));
        }

        public static void RunPhase(string pPhase, Action pAction)
        {
            RunPhase(AWSimulationDomain.Vanilla, pPhase, pAction);
        }

        public static void RunPhase(AWSimulationDomain pDomain,
            string pPhase, Action pAction)
        {
            AWSimulationTickBenchmark.TickCapture benchmarkTick =
                AWSimulationTickBenchmark.CapturePhaseTarget();
            long startedAt = Stopwatch.GetTimestamp();
            _simulationPhaseDepth++;
            double elapsed;
            try
            {
                pAction();
            }
            finally
            {
                _simulationPhaseDepth--;
                elapsed = ElapsedMilliseconds(startedAt);
            }

            _simulationCpuMilliseconds += elapsed;
            if (pDomain == AWSimulationDomain.Vanilla)
            {
                _vanillaCpuMilliseconds += elapsed;
                _lastVanillaRunFrame = _frameId;
                _currentVanillaPhase = pPhase;
            }
            else
            {
                _aw3CpuMilliseconds += elapsed;
                _lastAw3RunFrame = _frameId;
                _currentAw3Phase = pPhase;
            }

            _currentPhase = pPhase;
            if (elapsed > _longestPhaseMilliseconds)
            {
                _longestPhaseMilliseconds = elapsed;
                _longestPhase = pPhase;
            }

            if (PhaseEstimates.TryGetValue(pPhase, out double previous))
                PhaseEstimates[pPhase] = previous * 0.8d + elapsed * 0.2d;
            else
                PhaseEstimates[pPhase] = Math.Max(
                    AWPerformanceSettings.MinimumSliceMilliseconds,
                    elapsed);

            AWSimulationTickBenchmark.RecordPhase(benchmarkTick,
                pPhase, elapsed);
            AWSimulationTickBenchmark.FlushCompleted();
        }

        public static void SetPhase(string pPhase)
        {
            SetPhase(AWSimulationDomain.Vanilla, pPhase);
        }

        public static void SetPhase(AWSimulationDomain pDomain,
            string pPhase)
        {
            string phase = pPhase ?? "idle";
            _currentPhase = phase;
            if (pDomain == AWSimulationDomain.Vanilla)
                _currentVanillaPhase = phase;
            else
                _currentAw3Phase = phase;
        }

        public static void MarkFault(Exception pException)
        {
            _faulted = true;
            _faultMessage = pException == null
                ? "unknown"
                : pException.GetType().Name + ": " + pException.Message;
        }

        public static void ResetFault()
        {
            _faulted = false;
            _faultMessage = string.Empty;
        }

        public static void MarkCriticalHookInstalled()
        {
            _criticalHookInstalled = true;
        }

        public static void RecordCycleStarted()
        {
            CyclesStarted++;
        }

        public static void RecordCycleCompleted()
        {
            CyclesCompleted++;
        }

        public static string GetDiagnostics()
        {
            BeginFrame();
            AWCooperativeSimulationRunner runner =
                AWCooperativeSimulationRunner.Instance;
            return string.Format(CultureInfo.InvariantCulture,
                "mode={0} target={1:0.#}fps budget={2:0.00}ms " +
                "baselineP90={3:0.00}ms sim={4:0.00}ms" +
                "(vanilla={5:0.00},aw3={6:0.00}) phase={7}/{8} " +
                "cycles={9}/{10} speed={11:0.#}x/{12:0.00}x " +
                "credits={13:0.0} ticks={14}/{15} " +
                "longest={16}:{17:0.00}ms workers={18}/{19}/{20}" +
                "(total/fg/path) world={21}:{22}@{23:0.00}",
                AWPerformanceSettings.Mode.ToString().ToLowerInvariant(),
                AWPerformanceSettings.TargetRenderFps,
                _frameBudgetMilliseconds, _baselineP90,
                _simulationCpuMilliseconds, _vanillaCpuMilliseconds,
                _aw3CpuMilliseconds, _currentVanillaPhase,
                _currentAw3Phase, CyclesStarted, CyclesCompleted,
                runner.RequestedSpeed, runner.ActualSpeed,
                runner.AdmissionCredits, runner.LogicalTicksAdmitted,
                runner.LogicalTicksCompleted, _longestPhase,
                _longestPhaseMilliseconds,
                AWPerformanceSettings.TotalParallelBudget,
                AWPerformanceSettings.ForegroundParallelism,
                AWPerformanceSettings.PathfindingWorkerCount,
                AWSimulationTime.BoundWorldSeedId,
                AWSimulationTime.Generation, AWSimulationTime.DiagnosticTime);
        }

        private static void FinalizePreviousFrame()
        {
            double baseline = Math.Max(0d,
                _hostCpuMilliseconds - _simulationCpuMilliseconds);
            if (baseline > 0d && baseline < 1000d)
            {
                BaselineSamples[_baselineSampleCursor] = baseline;
                _baselineSampleCursor =
                    (_baselineSampleCursor + 1) % BaselineWindowSize;
                _baselineSampleCount = Math.Min(BaselineWindowSize,
                    _baselineSampleCount + 1);
                _baselineP90 = CalculatePercentile90();
            }

            _lastFrameDeltaMilliseconds = Time.unscaledDeltaTime * 1000d;
            _lastFrameSimulationMilliseconds = _simulationCpuMilliseconds;
        }

        private static void RecalculateBudget()
        {
            double targetMilliseconds =
                1000d / AWPerformanceSettings.TargetRenderFps;
            double rawBudget = targetMilliseconds -
                               AWPerformanceSettings.RenderReserveMilliseconds -
                               _baselineP90;
            double previousFrameOverrun = Math.Min(
                _lastFrameSimulationMilliseconds,
                Math.Max(0d,
                    _lastFrameDeltaMilliseconds - targetMilliseconds - 1d));
            rawBudget -= previousFrameOverrun;
            _frameBudgetMilliseconds = Math.Max(0d,
                Math.Min(AWPerformanceSettings
                    .MaxSimulationMillisecondsPerFrame, rawBudget));
        }

        private static bool CanUseStarvationSlice(
            AWSimulationDomain pDomain)
        {
            int lastRunFrame = pDomain == AWSimulationDomain.Vanilla
                ? _lastVanillaRunFrame
                : _lastAw3RunFrame;
            if (lastRunFrame != _frameId &&
                _frameId - lastRunFrame <
                    AWPerformanceSettings.StarvationFrameInterval)
                return false;

            double starvationBudget = Math.Min(
                AWPerformanceSettings.StarvationSliceMilliseconds,
                AWPerformanceSettings.MaxSimulationMillisecondsPerFrame);
            double domainSpent = pDomain == AWSimulationDomain.Vanilla
                ? _vanillaCpuMilliseconds
                : _aw3CpuMilliseconds;
            return domainSpent < starvationBudget &&
                   _simulationCpuMilliseconds <
                   _frameBudgetMilliseconds + starvationBudget;
        }

        private static double CalculatePercentile90()
        {
            Array.Copy(BaselineSamples, BaselineScratch,
                _baselineSampleCount);
            Array.Sort(BaselineScratch, 0, _baselineSampleCount);
            int index = AWFrameSchedulerRules.Percentile90Index(
                _baselineSampleCount);
            return BaselineScratch[index];
        }

        private static double ElapsedMilliseconds(long pStartedAt)
        {
            return (Stopwatch.GetTimestamp() - pStartedAt) * 1000d /
                   Stopwatch.Frequency;
        }
    }
}
