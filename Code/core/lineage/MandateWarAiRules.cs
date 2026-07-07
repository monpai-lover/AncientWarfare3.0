namespace AncientWarfare3.core.lineage
{
    public static class MandateWarAiRules
    {
        public static bool ShouldConsiderTakeMandate(bool pTargetIsCurrentMandate, bool pVassalBlocked,
            float pAttackerPower, float pDefenderPower, int pMandateValue)
        {
            if (!pTargetIsCurrentMandate || pVassalBlocked) return false;
            float defender = pDefenderPower < 1f ? 1f : pDefenderPower;
            float ratio = pAttackerPower / defender;
            if (pMandateValue >= 70) return ratio >= 1.75f;
            if (pMandateValue >= 40) return ratio >= 1.35f;
            return ratio >= 1.15f;
        }

        public static int ScoreTakeMandate(float pAttackerPower, float pDefenderPower, int pMandateValue)
        {
            float defender = pDefenderPower < 1f ? 1f : pDefenderPower;
            float ratio = pAttackerPower / defender;
            int weakMandateBonus = System.Math.Max(0, 100 - pMandateValue) * 3;
            return 260 + (int)(ratio * 80f) + weakMandateBonus;
        }
    }
}
