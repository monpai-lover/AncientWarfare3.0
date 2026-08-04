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
    }
}
