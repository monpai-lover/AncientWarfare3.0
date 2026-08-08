namespace AncientWarfare3.core.lineage
{
    public static class MandatePowerRules
    {
        public const float VassalPowerWeight = 0.6f;
        public const float CityPower = 100f;
        public const float TerritoryZonePower = 1.5f;
        public const float KingStewardshipPower = 10f;
        public const float RequiredMandateLeadRatio = 1.15f;

        public static float CalculateRealmPower(float pPopulation, int pCityCount, float pArmyPower,
            float pKingStewardship, int pTerritoryZones = 0)
        {
            if (pPopulation < 0f) pPopulation = 0f;
            if (pCityCount < 0) pCityCount = 0;
            if (pArmyPower < 0f) pArmyPower = 0f;
            if (pKingStewardship < 0f) pKingStewardship = 0f;
            if (pTerritoryZones < 0) pTerritoryZones = 0;

            return pPopulation +
                   pCityCount * CityPower +
                   pTerritoryZones * TerritoryZonePower +
                   pArmyPower +
                   pKingStewardship * KingStewardshipPower;
        }

        public static float CalculateCompetitionPower(float pOwnPower, float pVassalPower)
        {
            if (pOwnPower < 0f) pOwnPower = 0f;
            if (pVassalPower < 0f) pVassalPower = 0f;
            return pOwnPower + pVassalPower * VassalPowerWeight;
        }

        public static float[] AggregateCompetitionPowersByRoot(
            float[] pRealmPowers, int[] pParentIndices)
        {
            if (pRealmPowers == null || pParentIndices == null ||
                pRealmPowers.Length != pParentIndices.Length)
                return System.Array.Empty<float>();

            var result = new float[pRealmPowers.Length];
            for (int index = 0; index < pRealmPowers.Length; index++)
                result[index] = pRealmPowers[index] < 0f
                    ? 0f
                    : pRealmPowers[index];

            for (int index = 0; index < pRealmPowers.Length; index++)
            {
                float realmPower = pRealmPowers[index] < 0f
                    ? 0f
                    : pRealmPowers[index];
                if (realmPower <= 0f) continue;

                int rootIndex = index;
                int steps = 0;
                while (pParentIndices[rootIndex] >= 0 &&
                       pParentIndices[rootIndex] < pRealmPowers.Length &&
                       steps++ < pRealmPowers.Length)
                    rootIndex = pParentIndices[rootIndex];

                // A malformed suzerain cycle has no independent root.
                if (steps > pRealmPowers.Length || rootIndex == index)
                    continue;
                result[rootIndex] += realmPower * VassalPowerWeight;
            }

            return result;
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

        public static int SelectWinningCandidateIndex(float[] pPowers, bool[] pEligible)
        {
            if (pPowers == null || pEligible == null || pPowers.Length == 0 || pPowers.Length != pEligible.Length)
                return -1;

            int bestIndex = -1;
            float bestPower = 0f;
            for (int i = 0; i < pPowers.Length; i++)
            {
                if (!pEligible[i]) continue;
                float power = pPowers[i];
                if (power <= bestPower) continue;
                bestPower = power;
                bestIndex = i;
            }
            if (bestIndex < 0) return -1;

            float strongestOther = 0f;
            float weakestOther = float.MaxValue;
            for (int i = 0; i < pPowers.Length; i++)
            {
                if (i == bestIndex || !pEligible[i]) continue;
                float power = pPowers[i];
                if (power > strongestOther) strongestOther = power;
                if (power > 0f && power < weakestOther) weakestOther = power;
            }
            if (weakestOther == float.MaxValue) weakestOther = 0f;
            return HasRequiredLeadForMandate(bestPower, strongestOther, weakestOther) ? bestIndex : -1;
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
