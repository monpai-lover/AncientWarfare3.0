namespace AncientWarfare3.core.lineage
{
    public static class WarExhaustionSettlementRules
    {
        public static bool CanForceSettlement(int pAttackerExhaustion,
            int pDefenderExhaustion)
        {
            return pAttackerExhaustion >= 100 &&
                   pDefenderExhaustion >= 100;
        }

        public static WarScoreSide WinnerSide(int pAttackerSignedScore)
        {
            if (pAttackerSignedScore > 0) return WarScoreSide.Attackers;
            if (pAttackerSignedScore < 0) return WarScoreSide.Defenders;
            return WarScoreSide.None;
        }
    }
}
