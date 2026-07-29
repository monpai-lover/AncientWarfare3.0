namespace AncientWarfare3.core.lineage
{
    public static class DynasticMaleLineContinuityRules
    {
        public static bool IsEligibleRole(bool isKing,
            bool isRegisteredHeir, bool isFeudatoryPrince,
            bool isFeudatorySuccessor, bool holdsActiveMaleTitle,
            bool isExpectedMaleTitleSuccessor)
        {
            return isKing || isRegisteredHeir || isFeudatoryPrince ||
                   isFeudatorySuccessor || holdsActiveMaleTitle ||
                   isExpectedMaleTitleSuccessor;
        }

        public static bool ShouldBypassPersonalOffspringLimit(
            bool eligibleRole, bool alive, bool adult, bool breedingAge,
            bool canProduceBabies, bool hasLivingSon)
        {
            return eligibleRole && alive && adult && breedingAge &&
                   canProduceBabies && !hasLivingSon;
        }

        public static bool HasPersonalOffspringRoom(bool vanillaRoom,
            bool continuationBypass)
        {
            return vanillaRoom || continuationBypass;
        }
    }
}
