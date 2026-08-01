using System.Reflection;
using AncientWarfare3.core.lineage;
using ai.behaviours;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_WartimeMilitaryTaskPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(AiSystemActor), "setTask");
        }

        [HarmonyPrefix]
        private static bool SetTask_Prefix(AiSystemActor __instance,
            string pTaskId)
        {
            return WartimeMilitaryTaskGate.Allows(
                FindActor(__instance), pTaskId);
        }

        private static Actor FindActor(AiSystemActor pSystem)
        {
            try
            {
                return Traverse.Create(pSystem).Field("ai_object")
                    .GetValue<Actor>();
            }
            catch
            {
                return null;
            }
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
            Actor actor = FindActor(__instance);
            string taskId = FindTaskId(__instance);
            if (WartimeMilitaryTaskGate.Allows(actor, taskId)) return true;
            try { __instance.setTaskBehFinished(); }
            catch { }
            return false;
        }

        private static Actor FindActor(AiSystemActor pSystem)
        {
            try
            {
                return Traverse.Create(pSystem).Field("ai_object")
                    .GetValue<Actor>();
            }
            catch
            {
                return null;
            }
        }

        private static string FindTaskId(AiSystemActor pSystem)
        {
            try { return Traverse.Create(pSystem).Field("task")
                .GetValue<BehaviourTaskActor>()?.id; }
            catch { return null; }
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
