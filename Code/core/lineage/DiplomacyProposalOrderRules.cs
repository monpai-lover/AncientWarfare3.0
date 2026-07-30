namespace AncientWarfare3.core.lineage
{
    public static class DiplomacyProposalOrderRules
    {
        public static int Compare(double pFirstScore, int pFirstTypeOrder,
            long pFirstTargetId, double pSecondScore, int pSecondTypeOrder,
            long pSecondTargetId)
        {
            int score = pSecondScore.CompareTo(pFirstScore);
            if (score != 0) return score;
            int type = pFirstTypeOrder.CompareTo(pSecondTypeOrder);
            return type != 0
                ? type
                : pFirstTargetId.CompareTo(pSecondTargetId);
        }
    }
}
