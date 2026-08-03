using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ActorRacePerformancePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), "updateAge")]
        private static void UpdateAge_Prefix(Actor __instance,
            out RuntimePerformanceDiagnostic.ActorRaceScopeToken __state)
        {
            __state = default;
            if (!RuntimePerformanceDiagnostic.IsSampling ||
                !RuntimePerformanceDiagnostic.TryConsumeActorDetailSample())
                return;
            __state = RuntimePerformanceDiagnostic.BeginActorRaceScope(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Actor), "updateAge")]
        private static void UpdateAge_Postfix(
            RuntimePerformanceDiagnostic.ActorRaceScopeToken __state)
        {
            if (__state.Started == 0L) return;
            RuntimePerformanceDiagnostic.EndActorRaceScope(
                ActorRacePerformanceMetric.UpdateAge, __state);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), nameof(Actor.calculateMainSprite))]
        private static void MainSprite_Prefix(Actor __instance,
            out RuntimePerformanceDiagnostic.ActorRaceScopeToken __state)
        {
            __state = default;
            if (!RuntimePerformanceDiagnostic.IsSampling ||
                !RuntimePerformanceDiagnostic.TryConsumeActorDetailSample())
                return;
            __state = RuntimePerformanceDiagnostic.BeginActorRaceScope(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Actor), nameof(Actor.calculateMainSprite))]
        private static void MainSprite_Postfix(
            RuntimePerformanceDiagnostic.ActorRaceScopeToken __state)
        {
            if (__state.Started == 0L) return;
            RuntimePerformanceDiagnostic.EndActorRaceScope(
                ActorRacePerformanceMetric.MainSprite, __state);
        }
    }
}
