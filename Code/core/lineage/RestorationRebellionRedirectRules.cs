namespace AncientWarfare3.core.lineage
{
    public enum RestorationRebellionSeedMode
    {
        Core = 0,
        ExternalBandit = 1
    }

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
            return CanUseRequiredSeed(RestorationRebellionSeedMode.Core,
                originalKingdomDead, isOriginalCapital, isPersistedCore);
        }

        public static bool CanUseRequiredSeed(
            RestorationRebellionSeedMode pMode,
            bool pOriginalKingdomDead, bool pIsOriginalCapital,
            bool pIsPersistedCore)
        {
            return pOriginalKingdomDead &&
                   (pMode == RestorationRebellionSeedMode.ExternalBandit ||
                    pIsOriginalCapital || pIsPersistedCore);
        }

        public static bool ShouldCountSeedAsCore(
            RestorationRebellionSeedMode pMode, bool pIsPersistedCore)
        {
            return pMode == RestorationRebellionSeedMode.Core &&
                   pIsPersistedCore;
        }

        public static bool ShouldInspectBanditFounder(bool pAllowRedirect,
            bool pActorValid, bool pCityValid)
        {
            return pAllowRedirect && pActorValid && pCityValid;
        }

        public static int CompareCoreTargets(int pLeftDistanceSquared,
            long pLeftCityId, int pRightDistanceSquared,
            long pRightCityId)
        {
            int distance = pLeftDistanceSquared.CompareTo(
                pRightDistanceSquared);
            return distance != 0
                ? distance
                : pLeftCityId.CompareTo(pRightCityId);
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
