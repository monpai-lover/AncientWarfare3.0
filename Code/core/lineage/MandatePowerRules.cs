namespace AncientWarfare3.core.lineage
{
    public static class MandatePowerRules
    {
        public const float VassalPowerWeight = 0.6f;
        public const float CityPower = 100f;
        public const float KingStewardshipPower = 10f;
        public const float RequiredMandateLeadRatio = 1.15f;

        public static float CalculateRealmPower(float pPopulation, int pCityCount, float pArmyPower,
            float pKingStewardship)
        {
            if (pPopulation < 0f) pPopulation = 0f;
            if (pCityCount < 0) pCityCount = 0;
            if (pArmyPower < 0f) pArmyPower = 0f;
            if (pKingStewardship < 0f) pKingStewardship = 0f;

            return pPopulation +
                   pCityCount * CityPower +
                   pArmyPower +
                   pKingStewardship * KingStewardshipPower;
        }

        public static float CalculateCompetitionPower(float pOwnPower, float pVassalPower)
        {
            if (pOwnPower < 0f) pOwnPower = 0f;
            if (pVassalPower < 0f) pVassalPower = 0f;
            return pOwnPower + pVassalPower * VassalPowerWeight;
        }

        public static bool HasRequiredLeadForMandate(float pCandidatePower, float pStrongestOtherPower,
            float pWeakestOtherPower)
        {
            if (pCandidatePower <= 0f) return false;
            if (pStrongestOtherPower <= 0f) return true;
            float weakest = pWeakestOtherPower > 0f ? pWeakestOtherPower : pStrongestOtherPower;
            return pCandidatePower + 0.001f >= (pStrongestOtherPower + weakest) * RequiredMandateLeadRatio;
        }

        public static bool IsEligibleCompetitor(bool pIsValidCivilKingdom, bool pIsVassal,
            bool pSupportsMandateSystem)
        {
            return pIsValidCivilKingdom && !pIsVassal && pSupportsMandateSystem;
        }

        public static int CalculateStrongestPowerPenalty(float pMandatePower, float pStrongestPower)
        {
            float mandate = pMandatePower < 1f ? 1f : pMandatePower;
            if (pStrongestPower <= mandate * 1.0001f) return 0;
            if (pStrongestPower >= mandate * 2.5f) return -8;
            if (pStrongestPower >= mandate * 1.75f) return -6;
            if (pStrongestPower >= mandate * 1.25f) return -4;
            return -2;
        }
    }
}
