using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;

namespace AncientWarfare3.core.performance
{
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
        private static double _lastFrameDeltaMilliseconds;
        private static double _frameBudgetMilliseconds;
        private static int _lastSimulationRunFrame =
            -AWPerformanceSettings.StarvationFrameInterval;
        private static string _currentPhase = "idle";
        private static string _longestPhase = string.Empty;
        private static double _longestPhaseMilliseconds;
        private static bool _faulted;
        private static string _faultMessage = string.Empty;
        private static int _simulationPhaseDepth;

        public static long CyclesStarted { get; private set; }
        public static long CyclesCompleted { get; private set; }
        public static bool Faulted => _faulted;
        public static string FaultMessage => _faultMessage;
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
                return CanUseStarvationSlice();

            double remaining = _frameBudgetMilliseconds -
                               _simulationCpuMilliseconds;
            if (remaining >= Math.Max(
                    AWPerformanceSettings.MinimumSliceMilliseconds,
                    estimate))
                return true;
            return CanUseStarvationSlice();
        }

        public static void RunPhase(string pPhase, Action pAction)
        {
            AWSimulationTickBenchmark.TickCapture benchmarkTick =
                AWSimulationTickBenchmark.CapturePhaseTarget();
            long startedAt = Stopwatch.GetTimestamp();
            _simulationPhaseDepth++;
            try
            {
                pAction();
            }
            finally
            {
                _simulationPhaseDepth--;
                double elapsed = ElapsedMilliseconds(startedAt);
                _simulationCpuMilliseconds += elapsed;
                _lastSimulationRunFrame = _frameId;
                _currentPhase = pPhase;
                if (elapsed > _longestPhaseMilliseconds)
                {
                    _longestPhaseMilliseconds = elapsed;
                    _longestPhase = pPhase;
                }

                if (PhaseEstimates.TryGetValue(pPhase,
                        out double previous))
                    PhaseEstimates[pPhase] =
                        previous * 0.8d + elapsed * 0.2d;
                else
                    PhaseEstimates[pPhase] = Math.Max(
                        AWPerformanceSettings.MinimumSliceMilliseconds,
                        elapsed);

                AWSimulationTickBenchmark.RecordPhase(benchmarkTick,
                    pPhase, elapsed);
                AWSimulationTickBenchmark.FlushCompleted();
            }
        }

        public static void SetPhase(string pPhase)
        {
            _currentPhase = pPhase ?? "idle";
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
                "baselineP90={3:0.00}ms sim={4:0.00}ms phase={5} " +
                "cycles={6}/{7} speed={8:0.#}x/{9:0.00}x " +
                "credits={10:0.0} ticks={11}/{12} longest={13}:{14:0.00}ms",
                AWPerformanceSettings.Mode.ToString().ToLowerInvariant(),
                AWPerformanceSettings.TargetRenderFps,
                _frameBudgetMilliseconds, _baselineP90,
                _simulationCpuMilliseconds, _currentPhase,
                CyclesStarted, CyclesCompleted, runner.RequestedSpeed,
                runner.ActualSpeed, runner.AdmissionCredits,
                runner.LogicalTicksAdmitted, runner.LogicalTicksCompleted,
                _longestPhase, _longestPhaseMilliseconds);
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
        }

        private static void RecalculateBudget()
        {
            double targetMilliseconds =
                1000d / AWPerformanceSettings.TargetRenderFps;
            double rawBudget = targetMilliseconds -
                               AWPerformanceSettings.RenderReserveMilliseconds -
                               _baselineP90;
            double previousFrameOverrun = Math.Max(0d,
                _lastFrameDeltaMilliseconds - targetMilliseconds - 1d);
            rawBudget -= previousFrameOverrun;
            _frameBudgetMilliseconds = Math.Max(0d,
                Math.Min(AWPerformanceSettings
                    .MaxSimulationMillisecondsPerFrame, rawBudget));
        }

        private static bool CanUseStarvationSlice()
        {
            if (_frameId - _lastSimulationRunFrame <
                AWPerformanceSettings.StarvationFrameInterval)
                return false;
            return _simulationCpuMilliseconds <
                   _frameBudgetMilliseconds +
                   AWPerformanceSettings.StarvationSliceMilliseconds;
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
