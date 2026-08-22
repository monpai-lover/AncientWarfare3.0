using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.performance;
using ai.behaviours;
using HarmonyLib;
using System.Reflection;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_IdleBehaviourThrottlePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehTryToSocialize), nameof(BehTryToSocialize.execute))]
        private static bool TryToSocialize_Prefix(Actor pActor,
            ref BehResult __result)
        {
            return AllowOrStop(pActor, ref __result);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehTryFindTargetWithStatusNearby),
            nameof(BehTryFindTargetWithStatusNearby.execute))]
        private static bool FindNearbyStatus_Prefix(Actor pActor,
            ref BehResult __result)
        {
            return AllowOrStop(pActor, ref __result);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehDecideWhereToSleep),
            nameof(BehDecideWhereToSleep.execute))]
        private static bool DecideWhereToSleep_Prefix(Actor __0,
            ref BehResult __result)
        {
            if (AWIdleBehaviourThrottleService.ShouldRun(__0,
                    AWIdleBehaviourKind.Sleep)) return true;
            __result = BehResult.Stop;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.Dispose))]
        private static void ActorDispose_Prefix(Actor __instance)
        {
            AWIdleBehaviourThrottleService.Forget(__instance);
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void MapBoxClearWorld_Prefix()
        {
            if (!AWAsyncClearWorldGuard.CleanupAllowed) return;
            AWIdleBehaviourThrottleService.ClearRuntime();
        }

        private static bool AllowOrStop(Actor pActor, ref BehResult pResult)
        {
            string taskId = null;
            try { taskId = pActor?.ai?.task?.id; }
            catch { }
            if (AWIdleBehaviourThrottleService.ShouldRun(pActor, taskId))
                return true;
            pResult = BehResult.Stop;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class AW_IdleBehaviourTaskFinishedPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(AiSystemActor),
                "setTaskBehFinished");
        }

        [HarmonyPrefix]
        private static void Prefix(AiSystemActor __instance)
        {
            AWIdleBehaviourThrottleService.ReleaseAllBudgets(
                __instance?.ai_object);
        }
    }

    [HarmonyPatch]
    internal static class AW_IdleBehaviourTaskSwitchPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(AiSystemActor), "setTask");
        }

        [HarmonyPrefix]
        private static void Prefix(AiSystemActor __instance, string __0)
        {
            AWIdleBehaviourThrottleService.OnTaskSwitch(
                __instance?.ai_object, __0);
        }
    }
}
