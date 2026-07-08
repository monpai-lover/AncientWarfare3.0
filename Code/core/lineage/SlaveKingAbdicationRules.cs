namespace AncientWarfare3.core.lineage
{
    public static class SlaveKingAbdicationRules
    {
        public static bool ShouldForceAbdicate(bool pIsKing, bool pWasSlave, bool pIsSlaveNow,
            bool pHasKingdom)
        {
            return pIsKing && pIsSlaveNow && pHasKingdom;
        }
    }
}
