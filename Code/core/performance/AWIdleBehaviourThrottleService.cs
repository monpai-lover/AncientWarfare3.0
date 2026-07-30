using UnityEngine;

namespace AncientWarfare3.core.performance
{
    internal static class AWIdleBehaviourThrottleService
    {
        private static readonly AWIdleBehaviourThrottleGate Gate =
            new AWIdleBehaviourThrottleGate();

        public static bool ShouldRun(Actor pActor, string pTaskId)
        {
            if (pActor?.data == null ||
                !AWIdleBehaviourThrottleRules.TryGetKind(pTaskId,
                    out AWIdleBehaviourKind kind)) return true;
            try
            {
                if (!AWIdleBehaviourThrottleRules.IsEligibleCivilian(
                        pActor.isAlive(), pActor.isRekt(),
                        pActor.is_profession_warrior,
                        pActor.is_profession_king)) return true;
                return Gate.TryBeginScan(pActor.data.id, kind,
                    Time.realtimeSinceStartupAsDouble);
            }
            catch
            {
                return true;
            }
        }

        public static void Forget(Actor pActor)
        {
            if (pActor?.data == null) return;
            Gate.RemoveActor(pActor.data.id);
        }
    }
}
