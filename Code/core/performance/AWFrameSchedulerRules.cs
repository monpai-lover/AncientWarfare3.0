using System;

namespace AncientWarfare3.core.performance
{
    public enum AWSimulationDomain
    {
        Vanilla,
        Aw3Authority,
        RtsP0
    }

    public enum AWSimulationMode
    {
        Native = 0,
        Fixed = 1,
        Large = 2
    }

    public readonly struct AWSimulationCycle
    {
        public AWSimulationCycle(float pElapsedSeconds, int pPasses,
            bool pNormalizeTimeScale)
        {
            ElapsedSeconds = pElapsedSeconds;
            Passes = pPasses;
            NormalizeTimeScale = pNormalizeTimeScale;
        }

        public float ElapsedSeconds { get; }
        public int Passes { get; }
        public bool NormalizeTimeScale { get; }
    }

    public enum AWSaveBoundaryAction
    {
        Ready,
        DrainActiveCycle,
        AbortReplicaCycle
    }

    public enum AWPausedFrameAction
    {
        Continue,
        RefreshPresentation,
        CompleteActiveCycle,
        AbortReplicaCycle
    }

    public readonly struct AWPathWorkerAllocation
    {
        public AWPathWorkerAllocation(int pTotalBudget,
            int pActorPathWorkers, int pArmyRouteWorkers,
            int pForegroundParallelism)
        {
            TotalBudget = pTotalBudget;
            ActorPathWorkers = pActorPathWorkers;
            ArmyRouteWorkers = pArmyRouteWorkers;
            ForegroundParallelism = pForegroundParallelism;
        }

        public int TotalBudget { get; }
        public int ActorPathWorkers { get; }
        public int ArmyRouteWorkers { get; }
        public int ForegroundParallelism { get; }
    }

    public sealed class AWAuthorityCycleGate
    {
        private long _lastToken;
        private bool _hasLastToken;

        public bool TryEnter(long pToken, bool authorityAllowed)
        {
            if (!authorityAllowed ||
                (_hasLastToken && _lastToken == pToken))
                return false;
            _lastToken = pToken;
            _hasLastToken = true;
            return true;
        }

        public void Reset()
        {
            _lastToken = 0L;
            _hasLastToken = false;
        }
    }

    public sealed class AWPresentationRefreshGate<T> where T : class
    {
        private T _world;
        private int _frame;

        public void Request(T pWorld, int frame)
        {
            if (pWorld == null)
            {
                Clear();
                return;
            }
            _world = pWorld;
            _frame = frame;
        }

        public bool TryConsume(T pWorld, int frame)
        {
            bool matches = _world != null &&
                           ReferenceEquals(_world, pWorld) &&
                           _frame == frame;
            Clear();
            return matches;
        }

        public void ClearIfWorldMismatch(T pWorld)
        {
            if (_world != null && !ReferenceEquals(_world, pWorld)) Clear();
        }

        public void Clear()
        {
            _world = null;
            _frame = 0;
        }
    }

    public static class AWFrameSchedulerRules
    {
        public const float FixedSimulationStepSeconds = 0.02f;
        public const double BaseSimulationTicksPerSecond = 50d;
        // Vanilla and ordinary AW3 authority stages follow Cultiway's
        // starvation guard. RTS P0 is the only domain that keeps a tighter
        // cadence, so ordinary mod logic cannot steal its frame budget.
        public const double VanillaStarvationSliceMilliseconds = 0.5d;
        public const int VanillaStarvationFrameInterval = 8;
        public const double Aw3StarvationSliceMilliseconds = 2d;
        public const int Aw3StarvationFrameInterval = 1;

        public static AWSimulationMode ResolveMode(bool pEnabled)
        {
            return ResolveCachedMode(pEnabled, false, null);
        }

        public static AWSimulationMode ResolveCachedMode(bool pSchedulerEnabled,
            bool pLargeStepEnabled, AWSimulationMode? pEnvironmentOverride)
        {
            if (pEnvironmentOverride.HasValue)
                return pEnvironmentOverride.Value;
            if (!pSchedulerEnabled) return AWSimulationMode.Native;
            return pLargeStepEnabled ? AWSimulationMode.Large :
                AWSimulationMode.Fixed;
        }

        public static AWSimulationMode? ParseEnvironmentOverride(string pValue)
        {
            if (string.IsNullOrEmpty(pValue)) return null;
            switch (pValue)
            {
                case "0":
                case "false":
                case "native":
                    return AWSimulationMode.Native;
                case "1":
                case "true":
                case "fixed":
                    return AWSimulationMode.Fixed;
                case "large":
                    return AWSimulationMode.Large;
                default:
                    return null;
            }
        }

        public static float ClampTargetFps(float pValue)
        {
            if (float.IsNaN(pValue) || float.IsInfinity(pValue)) return 60f;
            return Math.Max(30f, Math.Min(144f, pValue));
        }

        public static float ClampSimulationBudget(float pValue)
        {
            if (float.IsNaN(pValue) || float.IsInfinity(pValue)) return 8f;
            return Math.Max(0.5f, Math.Min(1000f, pValue));
        }

        public static int TotalParallelBudget(int pProcessorCount)
        {
            return Math.Max(1, pProcessorCount - 2);
        }

        public static int PathfindingWorkerCount(int pProcessorCount)
        {
            int total = TotalParallelBudget(pProcessorCount);
            return Math.Min(8, Math.Max(1, (total + 2) / 3));
        }

        public static int ForegroundParallelism(int pProcessorCount)
        {
            return Math.Max(1, TotalParallelBudget(pProcessorCount) -
                               PathfindingWorkerCount(pProcessorCount));
        }

        public static bool ShouldParallelizeBatchRunner(
            bool pParallelJobsEnabled, bool pAllowWorkerParallelism)
        {
            return pParallelJobsEnabled && pAllowWorkerParallelism;
        }

        public static AWPathWorkerAllocation AllocateWorkers(
            int pProcessorCount)
        {
            int total = TotalParallelBudget(pProcessorCount);
            int pathWorkers = PathfindingWorkerCount(pProcessorCount);
            int armyRouteWorkers = 0;
            int actorPathWorkers = pathWorkers;
            int foregroundParallelism = ForegroundParallelism(pProcessorCount);
            return new AWPathWorkerAllocation(total, actorPathWorkers,
                armyRouteWorkers, foregroundParallelism);
        }

        public static int Percentile90Index(int pSampleCount)
        {
            if (pSampleCount <= 1) return 0;
            return Math.Max(0,
                (int)Math.Ceiling(pSampleCount * 0.9d) - 1);
        }

        public static bool ShouldMeasureHost(bool pRequiresControl,
            bool pDiagnosticsEnabled)
        {
            return pRequiresControl || pDiagnosticsEnabled;
        }

        public static int ResolveStarvationFrameInterval(
            AWSimulationDomain pDomain)
        {
            return pDomain == AWSimulationDomain.RtsP0
                ? Aw3StarvationFrameInterval
                : VanillaStarvationFrameInterval;
        }

        public static double ResolveStarvationSliceMilliseconds(
            AWSimulationDomain pDomain)
        {
            return pDomain == AWSimulationDomain.RtsP0
                ? Aw3StarvationSliceMilliseconds
                : VanillaStarvationSliceMilliseconds;
        }

        public static bool ShouldAdvancePresentationClock(
            bool schedulerRequiresControl, bool replicaSession)
        {
            return schedulerRequiresControl || replicaSession;
        }

        public static double RequestedSpeed(float pMultiplier, int pTicks)
        {
            return Math.Max(0f, pMultiplier) * Math.Max(1, pTicks);
        }

        public static double AdmissionRate(AWSimulationMode pMode,
            double requestedSpeed)
        {
            if (pMode == AWSimulationMode.Native) return 0d;
            return BaseSimulationTicksPerSecond *
                   (pMode == AWSimulationMode.Large
                       ? 1d
                       : Math.Max(0d, requestedSpeed));
        }

        public static double CreditCapacity(AWSimulationMode pMode,
            double requestedSpeed)
        {
            return Math.Max(1d, AdmissionRate(pMode, requestedSpeed));
        }

        public static double AddCredits(double current,
            double unscaledDeltaSeconds, AWSimulationMode pMode,
            double requestedSpeed)
        {
            double rate = AdmissionRate(pMode, requestedSpeed);
            if (rate <= 0d) return 0d;
            double capacity = Math.Max(1d, rate);
            double generated = Math.Max(0d, unscaledDeltaSeconds) * rate;
            return Math.Min(capacity, Math.Max(0d, current) + generated);
        }

        public static AWSimulationCycle BuildCycle(AWSimulationMode pMode,
            float multiplier, int ticks)
        {
            if (pMode == AWSimulationMode.Large)
                return new AWSimulationCycle(
                    FixedSimulationStepSeconds * Math.Max(0f, multiplier),
                    Math.Max(1, ticks), pNormalizeTimeScale: false);
            if (pMode == AWSimulationMode.Fixed)
                return new AWSimulationCycle(FixedSimulationStepSeconds, 1,
                    pNormalizeTimeScale: true);
            return new AWSimulationCycle(0f, 0,
                pNormalizeTimeScale: false);
        }

        public static bool CanAdmit(AWSimulationMode pMode,
            bool allowNewCycles, bool paused, bool replicaSession,
            double credits, bool modCycleActive)
        {
            return pMode != AWSimulationMode.Native &&
                   allowNewCycles &&
                   !paused &&
                   !replicaSession &&
                   credits >= 1d &&
                   !modCycleActive;
        }

        public static bool ShouldRefreshPausedPresentation(
            AWSimulationMode pMode, bool paused, bool replicaSession,
            bool modCycleActive)
        {
            return ResolvePausedFrameAction(pMode, paused, replicaSession,
                       modCycleActive) ==
                   AWPausedFrameAction.RefreshPresentation;
        }

        public static AWPausedFrameAction ResolvePausedFrameAction(
            AWSimulationMode pMode, bool paused, bool replicaSession,
            bool modCycleActive)
        {
            if (replicaSession)
                return modCycleActive
                    ? AWPausedFrameAction.AbortReplicaCycle
                    : AWPausedFrameAction.RefreshPresentation;
            // A UI pause (Esc/menu/window) must not drain an in-flight large
            // step synchronously on the render thread.  Large mode may have
            // several vanilla passes in one logical cycle; completing them
            // here makes opening a menu look like a hang and can allocate
            // unbounded presentation state while the UI is being opened.
            // Keep the cycle resumable and only refresh the paused view. Save
            // boundaries use the explicit DrainToBoundary path instead.
            if (paused && modCycleActive)
                return AWPausedFrameAction.RefreshPresentation;
            if (paused && pMode != AWSimulationMode.Native)
                return AWPausedFrameAction.RefreshPresentation;
            return AWPausedFrameAction.Continue;
        }

        public static bool ShouldRunAuthorityCycle(bool gameLoaded,
            bool loading, bool paused, bool replicaSession)
        {
            return gameLoaded && !loading && !paused && !replicaSession;
        }

        public static string ResolveAuthorityBlockReason(bool gameLoaded,
            bool loading, bool paused, bool replicaSession,
            bool initializationPending)
        {
            if (!gameLoaded) return "not_loaded";
            if (loading) return "loading";
            if (paused) return "paused";
            if (replicaSession) return "replica";
            if (initializationPending) return "initializing";
            return "none";
        }

        public static AWSaveBoundaryAction ResolveSaveBoundary(
            bool cycleActive, bool paused)
        {
            return ResolveSaveBoundary(cycleActive, paused,
                replicaSession: false);
        }

        public static AWSaveBoundaryAction ResolveSaveBoundary(
            bool cycleActive, bool paused, bool replicaSession)
        {
            if (!cycleActive) return AWSaveBoundaryAction.Ready;
            if (replicaSession)
                return AWSaveBoundaryAction.AbortReplicaCycle;
            return AWSaveBoundaryAction.DrainActiveCycle;
        }

    }
}
