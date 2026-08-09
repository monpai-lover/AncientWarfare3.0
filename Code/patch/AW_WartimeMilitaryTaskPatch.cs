using System.Reflection;
using AncientWarfare3.core.lineage;
using ai.behaviours;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_WartimeMilitaryJobPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(AiSystemActor), "setJob");
        }

        [HarmonyPrefix]
        private static bool SetJob_Prefix(AiSystemActor __instance,
            string __0)
        {
            Actor actor = __instance?.ai_object;
            return !SyntheticLevyService.IsSynthetic(actor) ||
                   SyntheticLevyRules.AllowTaskId(true, __0);
        }
    }

    [HarmonyPatch]
    internal static class AW_WartimeMilitaryTaskPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(AiSystemActor), "setTask");
        }

        [HarmonyPrefix]
        private static bool SetTask_Prefix(AiSystemActor __instance,
            string __0)
        {
            Actor actor = __instance?.ai_object;
            if (SyntheticLevyService.IsSynthetic(actor))
                return SyntheticLevyRules.AllowTaskId(true, __0);
            if (!WartimeMilitaryTaskRules.
                    ShouldEvaluateMilitaryState(__0)) return true;
            return WartimeMilitaryTaskGate.Allows(
                __instance.ai_object, __0);
        }
    }

    [HarmonyPatch]
    internal static class AW_WartimeMilitaryActiveTaskPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(AiSystemActor), "update");
        }

        [HarmonyPrefix]
        private static bool Update_Prefix(AiSystemActor __instance)
        {
            string taskId = __instance.task?.id;
            Actor actor = __instance?.ai_object;
            if (SyntheticLevyService.IsSynthetic(actor) &&
                !SyntheticLevyRules.AllowTaskId(true, taskId))
            {
                try { __instance.setTaskBehFinished(); }
                catch { }
                return false;
            }
            if (!WartimeMilitaryTaskRules.
                    ShouldEvaluateMilitaryState(taskId)) return true;
            if (WartimeMilitaryTaskGate.Allows(
                    __instance.ai_object, taskId)) return true;
            try { __instance.setTaskBehFinished(); }
            catch { }
            return false;
        }
    }

    [HarmonyPatch]
    internal static class AW_WartimeMilitaryLeisureActionPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehSocializeStartCheck),
            nameof(BehSocializeStartCheck.execute))]
        private static bool SocializeStart_Prefix(Actor __0,
            ref BehResult __result)
        {
            return AllowOrStop(__0, ref __result);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehTryToSocialize),
            nameof(BehTryToSocialize.execute))]
        private static bool Socialize_Prefix(Actor __0,
            ref BehResult __result)
        {
            return AllowOrStop(__0, ref __result);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehDoTalk), nameof(BehDoTalk.execute))]
        private static bool Talk_Prefix(Actor __0, ref BehResult __result)
        {
            return AllowOrStop(__0, ref __result);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehDecideWhereToSleep),
            nameof(BehDecideWhereToSleep.execute))]
        private static bool SleepDecision_Prefix(Actor __0,
            ref BehResult __result)
        {
            return AllowOrStop(__0, ref __result);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehTrySleep), nameof(BehTrySleep.execute))]
        private static bool Sleep_Prefix(Actor __0, ref BehResult __result)
        {
            return AllowOrStop(__0, ref __result);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehTryFindTargetWithStatusNearby),
            nameof(BehTryFindTargetWithStatusNearby.execute))]
        private static bool Emotion_Prefix(Actor __0,
            ref BehResult __result)
        {
            return AllowOrStop(__0, ref __result);
        }

        private static bool AllowOrStop(Actor pActor, ref BehResult pResult)
        {
            string taskId = null;
            try { taskId = pActor?.ai?.task?.id; }
            catch { }
            if (WartimeMilitaryTaskGate.Allows(pActor, taskId)) return true;
            pResult = BehResult.Stop;
            return false;
        }
    }
}
