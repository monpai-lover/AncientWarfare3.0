using System.Collections.Generic;
using System.Reflection;
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ActorBatchBenchmarkPatch
    {
        private static readonly string[] MethodNames =
        {
            "updateParallelChecks",
            "updateVisibility",
            "updateStats",
            "updateNutritionDecay",
            "updateEventsBecomeAdult",
            "updateEventsEggHatched",
            "updateActionLanded",
            "u1_checkInside",
            "u2_updateChildren",
            "u3_spriteAnimation",
            "u4_deadCheck",
            "u5_curTileAction",
            "u5_checkTileDeath",
            "u6_checkFrozen",
            "u7_checkAugmentationEffects",
            "u8_checkUpdateTimers",
            "b1_checkUnderForce",
            "b2_checkCurrentEnemyTarget",
            "b3_findEnemyTarget",
            "b4_checkTaskVerifier",
            "b5_checkPathMovement",
            "b6_0_updateDecision",
            "b55_updateNaturalDeaths",
            "b6_updateAI",
            "u10_checkSmoothMovement",
            "updateShake",
            "updateHovering",
            "updatePollinating",
            "updateDeathCheck"
        };

        private readonly struct SampleState
        {
            public SampleState(ActorBatchPerformanceStage pStage,
                long pStarted)
            {
                Stage = pStage;
                Started = pStarted;
            }

            public ActorBatchPerformanceStage Stage { get; }
            public long Started { get; }
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string methodName in MethodNames)
            {
                MethodBase method = AccessTools.Method(typeof(BatchActors),
                    methodName);
                if (method != null) yield return method;
            }
        }

        [HarmonyPrefix]
        private static void Prefix(MethodBase __originalMethod,
            out SampleState __state)
        {
            ActorBatchPerformanceStage stage = ActorBatchPerformanceRules
                .StageForMethod(__originalMethod?.Name);
            __state = new SampleState(stage,
                RuntimePerformanceDiagnostic.BeginActorBatch(stage));
        }

        [HarmonyPostfix]
        private static void Postfix(SampleState __state)
        {
            RuntimePerformanceDiagnostic.EndActorBatch(__state.Stage,
                __state.Started);
        }
    }
}
