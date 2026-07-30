using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ActorAiBenchmarkPatch
    {
        private readonly struct ActorAiBenchmarkState
        {
            public ActorAiBenchmarkState(int pIndex, long pStarted,
                long pDiagnosticStarted, string pTaskId,
                RuntimePerformanceDiagnostic.ActorRaceScopeToken pRaceToken)
            {
                Index = pIndex;
                Started = pStarted;
                DiagnosticStarted = pDiagnosticStarted;
                TaskId = pTaskId;
                RaceToken = pRaceToken;
            }

            public int Index { get; }
            public long Started { get; }
            public long DiagnosticStarted { get; }
            public string TaskId { get; }
            public RuntimePerformanceDiagnostic.ActorRaceScopeToken RaceToken { get; }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.b6_updateAI))]
        private static void UpdateAi_Prefix(Actor __instance,
            out ActorAiBenchmarkState __state)
        {
            long diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            RuntimePerformanceDiagnostic.ActorRaceScopeToken raceToken =
                RuntimePerformanceDiagnostic.BeginActorRaceScope(__instance);
            string taskId = __instance?.ai?.task?.id;
            __state = new ActorAiBenchmarkState(-1, 0L, diagnostic,
                taskId, raceToken);
            if ((!Bench.bench_enabled &&
                 !RuntimePerformanceDiagnostic.IsSampling) ||
                __instance?.ai?.task == null) return;
            RecentActorAiCategory category = RecentFeatureBenchmarkRules
                .ClassifyActorAiTask(__instance.ai.task.id);
            int index = RecentFeatureBenchmarkRules.ActorAiIndex(category);
            if (index < 0) return;
            __state = new ActorAiBenchmarkState(index,
                RecentFeatureBenchmark.Begin(), diagnostic, taskId,
                raceToken);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.b6_updateAI))]
        private static void UpdateAi_Postfix(ActorAiBenchmarkState __state)
        {
            try
            {
                if (__state.Started != 0L)
                    RecentFeatureBenchmark.End(__state.Index,
                        __state.Started);
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndActorAi(
                    __state.DiagnosticStarted, __state.TaskId);
                RuntimePerformanceDiagnostic.EndActorRaceScope(
                    ActorRacePerformanceMetric.ActorAi,
                    __state.RaceToken);
            }
        }
    }
}
