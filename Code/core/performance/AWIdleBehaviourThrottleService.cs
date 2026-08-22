using UnityEngine;

namespace AncientWarfare3.core.performance
{
    internal static class AWIdleBehaviourThrottleService
    {
        private static readonly AWIdleBehaviourThrottleGate Gate =
            new AWIdleBehaviourThrottleGate();
        private static readonly AWIdleBehaviourBudget Budget =
            new AWIdleBehaviourBudget();

        public static bool ShouldRun(Actor pActor, string pTaskId)
        {
            if (pActor?.data == null ||
                !AWIdleBehaviourThrottleRules.TryGetKind(pTaskId,
                    out AWIdleBehaviourKind kind)) return true;
            return ShouldRun(pActor, kind);
        }

        public static bool ShouldRun(Actor pActor,
            AWIdleBehaviourKind pKind)
        {
            if (pActor?.data == null || pKind == AWIdleBehaviourKind.None)
                return true;
            try
            {
                bool militaryMovementOwned =
                    ArmyMilitaryMovementPriorityIndex.TryGetKind(
                        pActor.data.id, out _);
                if (!AWIdleBehaviourThrottleRules.IsEligibleCivilian(
                        pActor.asset?.civ == true,
                        pActor.isAlive(), pActor.isRekt(),
                        pActor.is_profession_warrior,
                        pActor.army != null,
                        pActor.is_profession_king || pActor.isKing(),
                        pActor.asset?.is_boat == true,
                        militaryMovementOwned)) return true;
                AWCooperativeSimulationRunner runner =
                    AWCooperativeSimulationRunner.Instance;
                bool cooperativeControl = runner.RequiresControl;
                double nativeSpeed = cooperativeControl
                    ? 0d
                    : AWWorldTimeRateTracker.GetRequestedSpeed();
                double requestedSpeed =
                    AWIdleBehaviourThrottleRules.ResolveRequestedSpeed(
                        cooperativeControl, runner.RequestedSpeed,
                        nativeSpeed);
                bool allowed = Gate.TryBeginScan(pActor.data.id, pKind,
                    Time.realtimeSinceStartupAsDouble, requestedSpeed);
                AWIdleBehaviourThrottleDiagnostics.Record(pKind, allowed);
                if (!allowed) return false;
                if (!Budget.TryAcquire(pActor.data.id, pKind))
                {
                    AWIdleBehaviourThrottleDiagnostics.RecordBudgetRejected();
                    return false;
                }
                return true;
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
            Budget.ReleaseAll(pActor.data.id);
        }

        public static void ReleaseTask(Actor pActor, string pTaskId)
        {
            if (pActor?.data == null ||
                !AWIdleBehaviourThrottleRules.TryGetKind(pTaskId,
                    out AWIdleBehaviourKind kind)) return;
            Budget.Release(pActor.data.id, kind);
        }

        public static void ReleaseAllBudgets(Actor pActor)
        {
            if (pActor?.data == null) return;
            Budget.ReleaseAll(pActor.data.id);
        }

        public static void OnTaskSwitch(Actor pActor, string pNextTaskId)
        {
            if (pActor?.data == null) return;
            AWIdleBehaviourKind retained = AWIdleBehaviourKind.None;
            AWIdleBehaviourThrottleRules.TryGetActiveKind(
                pNextTaskId, out retained);
            Budget.ReleaseExcept(pActor.data.id, retained);
        }

        public static void ClearRuntime()
        {
            Gate.Clear();
            Budget.Clear();
            AWIdleBehaviourThrottleDiagnostics.Reset();
        }
    }
}
