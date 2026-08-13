namespace AncientWarfare3.core.lineage
{
    public enum ArmyRtsMissionReleaseCause
    {
        TargetInvalid = 0,
        TargetCompleted = 1,
        WarEnded = 2,
        ArmyInvalid = 3,
        ExplicitPlayerOrder = 4,
        ExplicitRetreat = 5,
        PathFailed = 6,
        MemberStalled = 7,
        SchedulerDelayed = 8
    }

    internal static class ArmyRtsMissionLockRules
    {
        internal static bool CanReplaceTarget(
            ArmyRtsMissionReleaseCause pCause)
        {
            return pCause == ArmyRtsMissionReleaseCause.TargetInvalid ||
                   pCause == ArmyRtsMissionReleaseCause.TargetCompleted ||
                   pCause == ArmyRtsMissionReleaseCause.WarEnded ||
                   pCause == ArmyRtsMissionReleaseCause.ArmyInvalid ||
                   pCause == ArmyRtsMissionReleaseCause.ExplicitPlayerOrder ||
                   pCause == ArmyRtsMissionReleaseCause.ExplicitRetreat;
        }

        internal static bool CanHandoffAfterRecovery(
            ArmyRtsMissionReleaseCause pCause, bool objectiveOpen)
        {
            return !objectiveOpen && CanReplaceTarget(pCause);
        }
    }
}
