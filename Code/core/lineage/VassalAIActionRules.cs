using System;

namespace AncientWarfare3.core.lineage
{
    public static class VassalAIActionRules
    {
        public const int MinimumAbsorptionYears = 12;
        public const float MinimumPowerRatio = 1.25f;
        private const float MinimumAbsorptionPriority = 50f;

        public static bool CanEvaluateRealm(bool pValidCivilizedRealm,
            bool pHasKing, int pCityCount)
        {
            return pValidCivilizedRealm && pHasKing && pCityCount > 0;
        }

        public static bool ShouldAttemptAbsorption(int yearsAsVassal,
            float lordToVassalPowerRatio, int autonomy,
            float courtAggression)
        {
            if (yearsAsVassal < MinimumAbsorptionYears ||
                lordToVassalPowerRatio < MinimumPowerRatio) return false;
            return AbsorptionPriority(yearsAsVassal,
                       lordToVassalPowerRatio, autonomy,
                       courtAggression) >= MinimumAbsorptionPriority;
        }

        public static float AbsorptionPriority(int yearsAsVassal,
            float lordToVassalPowerRatio, int autonomy,
            float courtAggression)
        {
            float tenure = Math.Max(0, yearsAsVassal -
                                       MinimumAbsorptionYears) * 3f;
            float dominance = Math.Max(0f, lordToVassalPowerRatio -
                                           MinimumPowerRatio) * 100f;
            float control = (70 - Math.Max(0, Math.Min(100, autonomy))) * 2f;
            float aggression = Math.Max(0f, Math.Min(1f,
                courtAggression)) * 40f;
            return tenure + dominance + control + aggression;
        }
    }
}
