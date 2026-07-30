namespace AncientWarfare3.core.lineage
{
    public enum RestorationRebellionStartOutcome
    {
        NotStarted = 0,
        Started = 1,
        ConsumedAfterCommit = 2
    }

    public static class RestorationRebellionRedirectRules
    {
        public static bool ShouldInspect(bool restorationCreationActive,
            bool actorValid, bool cityValid)
        {
            return !restorationCreationActive && actorValid && cityValid;
        }

        public static bool IsMatchingClaimCity(bool originalKingdomDead,
            bool isOriginalCapital, bool isPersistedCore)
        {
            return originalKingdomDead &&
                   (isOriginalCapital || isPersistedCore);
        }

        public static bool IsPeacefulHostCity(bool ownerIsClaimantHost,
            bool rebellionTriggered)
        {
            return ownerIsClaimantHost && !rebellionTriggered;
        }

        public static RestorationRebellionStartOutcome ResolveOutcome(
            bool started, bool identityCreationCommitted)
        {
            if (started) return RestorationRebellionStartOutcome.Started;
            return identityCreationCommitted
                ? RestorationRebellionStartOutcome.ConsumedAfterCommit
                : RestorationRebellionStartOutcome.NotStarted;
        }

        public static bool ShouldSuppressVanilla(
            RestorationRebellionStartOutcome outcome)
        {
            return outcome != RestorationRebellionStartOutcome.NotStarted;
        }

        public static int CompareClaimPriority(int leftStrength,
            long leftClaimId, int rightStrength, long rightClaimId)
        {
            int strength = rightStrength.CompareTo(leftStrength);
            return strength != 0
                ? strength
                : leftClaimId.CompareTo(rightClaimId);
        }
    }
}
