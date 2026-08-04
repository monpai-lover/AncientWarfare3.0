using System;

namespace AncientWarfare3.core.lineage
{
    public enum ArmyRtsAssignmentReconciliationAction
    {
        None = 0,
        RepairCaptainTask = 1,
        QueueAssignment = 2
    }

    public static class ArmyRtsAssignmentReconciliationRules
    {
        public const double AssignmentRetryWorldSeconds = 30d;

        public static bool IsValidWait(string reason, double deadline,
            double now)
        {
            return !string.IsNullOrWhiteSpace(reason) &&
                   !double.IsNaN(deadline) &&
                   !double.IsInfinity(deadline) &&
                   !double.IsNaN(now) && !double.IsInfinity(now) &&
                   deadline > now;
        }

        public static ArmyRtsAssignmentReconciliationAction Resolve(
            bool eligible, bool warReturnActive, bool hasMission,
            bool ownsTacticalActors, bool expectedCaptainTask,
            string waitReason, double waitDeadline, double now)
        {
            if (!eligible || warReturnActive)
                return ArmyRtsAssignmentReconciliationAction.None;
            if (hasMission)
                return ownsTacticalActors && !expectedCaptainTask
                    ? ArmyRtsAssignmentReconciliationAction.
                        RepairCaptainTask
                    : ArmyRtsAssignmentReconciliationAction.None;
            return IsValidWait(waitReason, waitDeadline, now)
                ? ArmyRtsAssignmentReconciliationAction.None
                : ArmyRtsAssignmentReconciliationAction.QueueAssignment;
        }
    }
}
