namespace AncientWarfare3.core.lineage
{
    public static class MandateConquestRules
    {
        private const float RequiredPowerRatio = 1.25f;

        public static bool CanUseMandateConquest(bool pAttackerIsCurrentMandate,
            bool pVassalBlocked,
            bool pSameAlliance,
            float pAttackerSystemPower,
            float pDefenderAlliancePower)
        {
            if (!pAttackerIsCurrentMandate || pVassalBlocked || pSameAlliance) return false;
            float defender = pDefenderAlliancePower < 1f ? 1f : pDefenderAlliancePower;
            return pAttackerSystemPower / defender >= RequiredPowerRatio;
        }

        public static int ScoreMandateConquest(float pAttackerSystemPower, float pDefenderAlliancePower,
            bool pNeighbor)
        {
            float defender = pDefenderAlliancePower < 1f ? 1f : pDefenderAlliancePower;
            float ratio = pAttackerSystemPower / defender;
            int score = 210 + (int)(ratio * 95f);
            if (pNeighbor) score += 110;
            return score;
        }
    }
}
