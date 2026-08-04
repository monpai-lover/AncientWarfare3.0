namespace AncientWarfare3.core.lineage
{
    public enum ArmyMembershipOwnershipDecision
    {
        Keep,
        Defer,
        Release
    }

    public static class ArmyMembershipOwnershipRules
    {
        public static ArmyMembershipOwnershipDecision Decide(
            bool runtimeStable, long intendedKingdomId,
            long actorKingdomId)
        {
            if (!runtimeStable || intendedKingdomId < 0L)
                return ArmyMembershipOwnershipDecision.Defer;
            return actorKingdomId == intendedKingdomId
                ? ArmyMembershipOwnershipDecision.Keep
                : ArmyMembershipOwnershipDecision.Release;
        }

        public static bool ShouldReleaseRosterEntry(bool actorValid,
            bool backlinkMatches,
            ArmyMembershipOwnershipDecision ownershipDecision)
        {
            return !actorValid || !backlinkMatches ||
                   ownershipDecision ==
                   ArmyMembershipOwnershipDecision.Release;
        }

        public static int UnknownOwnerRetryDelayFrames(int attempt)
        {
            int shift = System.Math.Max(0,
                System.Math.Min(5, attempt - 1));
            return 1 << shift;
        }
    }
}
