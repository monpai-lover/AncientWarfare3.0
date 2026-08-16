namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelBanditAnnexationRules
    {
        internal static bool CanAnnex(bool pAttackerActiveBandit,
            bool pDefenderActiveBandit, bool pDefenderStronghold,
            bool pDistinctKingdoms)
        {
            return pAttackerActiveBandit && pDefenderActiveBandit &&
                   pDefenderStronghold && pDistinctKingdoms;
        }

        internal static bool ShouldPreserveStronghold(bool pBanditAnnexation)
        {
            return pBanditAnnexation;
        }
    }
}
