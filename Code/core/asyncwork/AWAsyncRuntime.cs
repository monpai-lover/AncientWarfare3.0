using System;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.asyncwork
{
    internal static class AWAsyncRuntime
    {
        private static readonly bool DatabaseSwitch =
            AWPerformanceSettings.EnableAsyncDatabaseWrites;
        private static readonly bool AiSwitch =
            AWPerformanceSettings.EnableAsyncStrategyPlanning;
        private static readonly bool TraversalSwitch =
            AWPerformanceSettings.EnableAsyncTraversalBuilds;
        private static readonly bool UiSwitch =
            AWPerformanceSettings.EnableAsyncUiQueries;
        private static readonly bool ShadowSwitch =
            AWPerformanceSettings.EnableAsyncShadowChecks;
        private static readonly AWAsyncWorkCoordinator Coordinator =
            new AWAsyncWorkCoordinator(AWAsyncFeatureRules.ShouldStartCompute(
                DatabaseSwitch, AiSwitch, TraversalSwitch, UiSwitch,
                ShadowSwitch));

        public static bool DatabaseEnabled => DatabaseSwitch;
        public static bool AiEnabled => AiSwitch;
        public static bool TraversalEnabled => TraversalSwitch;
        public static bool UiEnabled => UiSwitch;
        public static bool ShadowEnabled => ShadowSwitch;
        public static long WorldGeneration => Coordinator.WorldGeneration;
        public static AWAsyncLifecycleState State => Coordinator.State;
        public static bool WorkerAlive => Coordinator.WorkerAlive;

        public static void Initialize()
        {
            _ = Coordinator.State;
        }

        public static long StartWorld()
        {
            return Coordinator.StartWorld();
        }

        public static bool TrySchedule(AWAsyncWorkRequest pRequest)
        {
            if (pRequest == null) return false;
            Func<System.Threading.CancellationToken, object> execute =
                pRequest.Execute;
            var measured = new AWAsyncWorkRequest(pRequest.Key, pRequest.Lane,
                pRequest.Stamp, token =>
                {
                    long benchmark = RecentFeatureBenchmark.Begin();
                    try { return execute(token); }
                    finally
                    {
                        RecentFeatureBenchmark.End(
                            RecentFeatureBenchmarkRules.AsyncComputeIndex,
                            benchmark);
                    }
                }, pRequest.Commit, pRequest.Fault, pRequest.TryAdmit);
            return Coordinator.TrySchedule(measured);
        }

        public static bool CanSchedule(string pKey, AWAsyncLane pLane,
            AWAsyncStamp pStamp)
        {
            return Coordinator.CanSchedule(pKey, pLane, pStamp);
        }

        public static void DrainMainThread(double pMilliseconds,
            int pMaxBatches)
        {
            Coordinator.DrainMainThread(pMilliseconds, pMaxBatches);
        }

        public static bool TryEnterSaveBarrier(TimeSpan pTimeout,
            out string pError)
        {
            return Coordinator.TryEnterSaveBarrier(pTimeout, out pError);
        }

        public static bool TryEnterSaveBarrier(TimeSpan pTimeout,
            Action pPendingOwnerWork, out string pError)
        {
            return Coordinator.TryEnterSaveBarrier(pTimeout,
                pPendingOwnerWork, out pError);
        }

        public static void ExitSaveBarrier()
        {
            Coordinator.ExitSaveBarrier();
        }

        public static void ClearWorld(TimeSpan pTimeout)
        {
            Coordinator.ClearWorld(pTimeout);
        }

        public static AWAsyncDiagnosticsSnapshot SnapshotDiagnostics()
        {
            return Coordinator.SnapshotDiagnostics();
        }

        public static AWAsyncFaultRecord[] SnapshotFaults()
        {
            return Coordinator.SnapshotFaults();
        }

        public static void Shutdown(TimeSpan pTimeout)
        {
            Coordinator.Shutdown(pTimeout);
        }

        public static bool TryShutdown(TimeSpan pTimeout, out string pError)
        {
            return Coordinator.TryShutdown(pTimeout, out pError);
        }
    }
}
