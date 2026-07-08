namespace AncientWarfare3.core.lineage
{
    public static class SlaveKingAbdicationRules
    {
        public static bool ShouldForceAbdicate(bool pIsKing, bool pWasSlave, bool pIsSlaveNow,
            bool pHasKingdom)
        {
            return pIsKing && pIsSlaveNow && pHasKingdom;
        }

        public static bool ShouldConvertSlaveOnlyKingdomToRebel(bool pIsKing, bool pIsSlaveNow,
            bool pHasKingdom, int pLivingCandidates, int pFreeCandidates)
        {
            return pIsKing &&
                   pIsSlaveNow &&
                   pHasKingdom &&
                   pLivingCandidates > 0 &&
                   pFreeCandidates <= 0;
        }
    }
}
