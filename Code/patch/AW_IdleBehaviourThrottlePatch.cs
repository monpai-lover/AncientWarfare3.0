using AncientWarfare3.core.performance;
using ai.behaviours;
using HarmonyLib;

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
        [HarmonyPatch(typeof(Actor), nameof(Actor.Dispose))]
        private static void ActorDispose_Prefix(Actor __instance)
        {
            AWIdleBehaviourThrottleService.Forget(__instance);
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
}
