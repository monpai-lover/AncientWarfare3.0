namespace AncientWarfare3.core.lineage
{
    public static class AccessionIdentityRules
    {
        public static bool ShouldDeferForInitialKingdomCreation(
            bool pUsesManagedSuccession, bool pHasCurrentKing,
            bool pHasCapital, bool pCandidateJoinedKingdom)
        {
            return pUsesManagedSuccession && !pHasCurrentKing &&
                   !pHasCapital && pCandidateJoinedKingdom;
        }

        public static bool ShouldFinalizeDeferredFounding(
            bool pUsesManagedSuccession, bool pHasLivingKing,
            bool pHasValidCapital, bool pKingJoinedKingdom,
            bool pKingLivesInCapital, bool pMonarchyEstablished,
            bool pIsRepublic, bool pIsRepublicLeader)
        {
            return pUsesManagedSuccession && pHasLivingKing &&
                   pHasValidCapital && pKingJoinedKingdom &&
                   pKingLivesInCapital && !pMonarchyEstablished &&
                   !pIsRepublic && !pIsRepublicLeader;
        }
    }
}
