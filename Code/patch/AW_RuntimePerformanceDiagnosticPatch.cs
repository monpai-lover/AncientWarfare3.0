using System;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_RuntimePerformanceDiagnosticPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.Update))]
        private static void MapUpdate_Prefix()
        {
            long benchmark = RecentFeatureBenchmark.BeginOutsideFrameStage();
            try { RuntimePerformanceDiagnostic.BeginFrame(); }
            finally
            {
                RecentFeatureBenchmark.EndOutsideFrameStage(
                    RecentFeatureBenchmarkRules.PerformanceDiagnosticIndex,
                    benchmark);
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.Update))]
        private static void MapUpdate_Postfix()
        {
            long benchmark = RecentFeatureBenchmark.BeginOutsideFrameStage();
            try { RuntimePerformanceDiagnostic.FlushFrame(); }
            finally
            {
                try
                {
                    RecentFeatureBenchmark.EndOutsideFrameStage(
                        RecentFeatureBenchmarkRules.PerformanceDiagnosticIndex,
                        benchmark);
                }
                finally
                {
                    MapBoxFrameStageGuard.Run(
                        "recent_feature_benchmark_flush",
                        RecentFeatureBenchmark.Flush);
                }
            }
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.Update))]
        private static Exception FlushFailedFrame(Exception __exception)
        {
            if (__exception == null) return null;
            MapBoxFrameStageGuard.Run(
                "runtime_performance_diagnostic_fault_flush",
                RuntimePerformanceDiagnostic.FlushFrame);
            MapBoxFrameStageGuard.Run("recent_feature_benchmark_fault_flush",
                RecentFeatureBenchmark.Flush);
            return __exception;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.update))]
        private static void ActorManagerUpdate_Prefix(out long __state)
        {
            __state = RuntimePerformanceDiagnostic.BeginScope();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.update))]
        private static void ActorManagerUpdate_Postfix(long __state)
        {
            RuntimePerformanceDiagnostic.EndActorWall(__state);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BuildingManager), nameof(BuildingManager.update))]
        private static void BuildingManagerUpdate_Prefix(out long __state)
        {
            __state = RuntimePerformanceDiagnostic.BeginScope();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BuildingManager), nameof(BuildingManager.update))]
        private static void BuildingManagerUpdate_Postfix(long __state)
        {
            RuntimePerformanceDiagnostic.EndBuildingWall(__state);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), "updateObjectAge")]
        private static void UpdateObjectAge_Prefix(out long __state)
        {
            __state = RuntimePerformanceDiagnostic.BeginScope();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), "updateObjectAge")]
        private static void UpdateObjectAge_Postfix(long __state)
        {
            RuntimePerformanceDiagnostic.EndUpdateAgeWall(__state);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BatchActors), "updateDeathCheck")]
        private static void UpdateDeathCheck_Prefix(out long __state)
        {
            __state = RuntimePerformanceDiagnostic.BeginScope();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BatchActors), "updateDeathCheck")]
        private static void UpdateDeathCheck_Postfix(long __state)
        {
            RuntimePerformanceDiagnostic.EndDeathCheck(__state);
        }
    }
}
