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
            RuntimePerformanceDiagnostic.BeginFrame();
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.Update))]
        private static void MapUpdate_Postfix()
        {
            RuntimePerformanceDiagnostic.FlushFrame();
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
