namespace AncientWarfare3.core.lineage
{
    public static class NonCoreLoyaltyRules
    {
        public static int CalculatePenalty(bool pOwnedNonCore, bool pIsCapital)
        {
            if (!pOwnedNonCore) return 0;
            return pIsCapital ? -18 : -35;
        }
    }
}
