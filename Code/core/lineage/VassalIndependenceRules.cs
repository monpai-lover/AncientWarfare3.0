namespace AncientWarfare3.core.lineage
{
    public static class VassalIndependenceRules
    {
        private const float StrongBreakawayRatio = 1.5f;
        private const int StrongBreakawayMinYears = 5;
        private const int NormalBreakawayMinYears = 18;
        private const float NormalPowerRatio = 1.0f;
        private const float NormalChance = 0.30f;

        public static bool ShouldUseSuzerainPersonalPowerForBreakaway(float pOwnPower,
            float pSuzerainPersonalPower, float pSuzerainNetworkPower)
        {
            if (pSuzerainPersonalPower <= 0f) return false;
            return pOwnPower >= pSuzerainPersonalPower * StrongBreakawayRatio &&
                   pOwnPower < pSuzerainNetworkPower;
        }

        public static bool ShouldAttemptIndependence(float pOwnPower, float pSuzerainPersonalPower,
            int pYearsAsVassal, int pOpinion, float pRandomRoll)
        {
            float lord = pSuzerainPersonalPower <= 0f ? 1f : pSuzerainPersonalPower;
            if (pYearsAsVassal >= StrongBreakawayMinYears && pOwnPower >= lord * StrongBreakawayRatio)
                return true;

            if (pYearsAsVassal >= 0 && pYearsAsVassal < NormalBreakawayMinYears) return false;

            bool wantsOut = pOwnPower >= lord * NormalPowerRatio || pOpinion <= -80;
            return wantsOut && pRandomRoll < NormalChance;
        }
    }
}
