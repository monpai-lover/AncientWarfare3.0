using System;

namespace AncientWarfare3.core.performance
{
    public static class AWPerformanceSettings
    {
        private static bool _configSchedulerEnabled;
        private static bool _configArmyRtsEnabled = true;
        private static bool _configShowArmyRtsVisuals;
        private static bool _configShowArmyMapInformation;
        private static bool _configAw3ArmyRtsScheduler;
        private static bool _configDiplomacyAiEnabled = true;
        private static bool _configAiAllianceActionsEnabled = true;
        private static bool _configAiVassalActionsEnabled = true;

        internal static event Action ArmyRtsDiagnosticsDisabled;
        internal static event Action ArmyMapInformationDisabled;

        public static float TargetRenderFps { get; private set; } = 60f;
        public static float MaxSimulationMillisecondsPerFrame { get; private set; } = 8f;
        public static bool EnablePresentationSmoothing { get; private set; } = true;
        public static bool EnableSchedulerDiagnostics { get; private set; }
        public static bool EnablePerformanceDiagnostics { get; private set; }
        public static bool EnableAsyncDatabaseWrites { get; private set; }
        public static bool EnableAsyncStrategyPlanning { get; private set; }
        public static bool EnableAsyncTraversalBuilds { get; private set; }
        public static bool EnableAsyncUiQueries { get; private set; }
        public static bool EnableAsyncShadowChecks { get; private set; }
        public static bool ArmyRtsDiagnosticsEnabled { get; private set; }
        public static bool EnableArmyRts => _configArmyRtsEnabled;
        public static bool ShowArmyRtsVisuals =>
            _configShowArmyRtsVisuals;
        public static bool ShowArmyMapInformation =>
            _configShowArmyMapInformation;
        public static bool UseAw3ArmyRtsScheduler =>
            _configAw3ArmyRtsScheduler;
        public static bool EnableDiplomacyAi => _configDiplomacyAiEnabled;
        public static bool EnableAiAllianceActions =>
            _configAiAllianceActionsEnabled;
        public static bool EnableAiVassalActions =>
            _configAiVassalActionsEnabled;

        public const float RenderReserveMilliseconds = 2f;
        public const float MinimumSliceMilliseconds = 0.15f;
        public const float BackgroundJoinMilliseconds = 0.2f;
        public const float StarvationSliceMilliseconds = 2f;
        public const int StarvationFrameInterval = 1;
        public const int SimulationBatchSize = 256;

        public static AWSimulationMode Mode =>
            AWFrameSchedulerRules.ResolveMode(_configSchedulerEnabled);

        public static bool EnableFramePriorityScheduler =>
            Mode != AWSimulationMode.Native;

        public static int TotalParallelBudget =>
            WorkerAllocation.TotalBudget;

        public static int PathfindingWorkerCount =>
            WorkerAllocation.ActorPathWorkers +
            WorkerAllocation.ArmyRouteWorkers;

        public static int ActorPathfindingWorkerCount =>
            WorkerAllocation.ActorPathWorkers;

        public static int ArmyRouteWorkerCount =>
            WorkerAllocation.ArmyRouteWorkers;

        public static int ForegroundParallelism =>
            WorkerAllocation.ForegroundParallelism;

        private static AWPathWorkerAllocation WorkerAllocation =>
            AWFrameSchedulerRules.AllocateWorkers(
                Environment.ProcessorCount);

        public static void SwitchFramePriorityScheduler(bool pValue)
        {
            _configSchedulerEnabled = pValue;
        }

        public static void SwitchLargeSimulationStep(bool pValue)
        {
            // Kept for existing saved configs; scheduler mode is now singular.
            _ = pValue;
        }

        public static void SwitchArmyRtsScheduler(bool pValue)
        {
            _configAw3ArmyRtsScheduler = pValue;
        }

        public static void SwitchArmyRts(bool pValue)
        {
            _configArmyRtsEnabled = pValue;
        }

        public static void SwitchArmyRtsVisuals(bool pValue)
        {
            _configShowArmyRtsVisuals = pValue;
        }

        public static void SwitchArmyMapInformation(bool pValue)
        {
            _configShowArmyMapInformation = pValue;
            if (!pValue) ArmyMapInformationDisabled?.Invoke();
        }

        public static void SwitchArmyRtsDiagnostics(bool pValue)
        {
            ArmyRtsDiagnosticsEnabled = pValue;
            if (!pValue) ArmyRtsDiagnosticsDisabled?.Invoke();
        }

        public static void SwitchDiplomacyAi(bool pValue)
        {
            _configDiplomacyAiEnabled = pValue;
        }

        public static void SwitchAiAllianceActions(bool pValue)
        {
            _configAiAllianceActionsEnabled = pValue;
        }

        public static void SwitchAiVassalActions(bool pValue)
        {
            _configAiVassalActionsEnabled = pValue;
        }

        public static void SetTargetRenderFps(float pValue)
        {
            TargetRenderFps = AWFrameSchedulerRules.ClampTargetFps(pValue);
        }

        public static void SetMaxSimulationMillisecondsPerFrame(float pValue)
        {
            MaxSimulationMillisecondsPerFrame =
                AWFrameSchedulerRules.ClampSimulationBudget(pValue);
        }

        public static void SwitchPresentationSmoothing(bool pValue)
        {
            EnablePresentationSmoothing = pValue;
        }

        public static void SwitchSchedulerDiagnostics(bool pValue)
        {
            EnableSchedulerDiagnostics = pValue;
        }

        public static void SwitchPerformanceDiagnostics(bool pValue)
        {
            EnablePerformanceDiagnostics = pValue;
        }

        public static void SwitchAsyncDatabaseWrites(bool pValue)
        {
            EnableAsyncDatabaseWrites = pValue;
        }

        public static void SwitchAsyncStrategyPlanning(bool pValue)
        {
            EnableAsyncStrategyPlanning = pValue;
        }

        public static void SwitchAsyncTraversalBuilds(bool pValue)
        {
            EnableAsyncTraversalBuilds = pValue;
        }

        public static void SwitchAsyncUiQueries(bool pValue)
        {
            EnableAsyncUiQueries = pValue;
        }

        public static void SwitchAsyncShadowChecks(bool pValue)
        {
            EnableAsyncShadowChecks = pValue;
        }
    }
}
