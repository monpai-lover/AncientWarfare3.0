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

        public static int ResolveDeferredRetryDelay(int pAttempts)
        {
            if (pAttempts <= 0) return 1;
            int shift = pAttempts - 1;
            if (shift > 5) shift = 5;
            return 1 << shift;
        }
    }
}
