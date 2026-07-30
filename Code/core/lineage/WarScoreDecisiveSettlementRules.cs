namespace AncientWarfare3.core.lineage
{
    public static class WarScoreDecisiveSettlementRules
    {
        public static WarScoreSide WinnerSide(int pAttackerPerspectiveScore)
        {
            if (pAttackerPerspectiveScore == WarScoreRules.MaximumScore)
                return WarScoreSide.Attackers;
            if (pAttackerPerspectiveScore == -WarScoreRules.MaximumScore)
                return WarScoreSide.Defenders;
            return WarScoreSide.None;
        }
    }
}
