using System;

namespace AncientWarfare3.core.court
{
    public static class WarCourtMultiplierRules
    {
        public static float OffensiveWarMultiplier(float pAggression,
            float pPeace, float pLivelihood, float pWar,
            bool pProtectedWar)
        {
            if (pProtectedWar) return 1f;
            float value = 1f + (pAggression - .5f) * .45f -
                          (pPeace - .5f) * .35f -
                          (pLivelihood - .5f) * .15f +
                          (pWar - .5f) * .25f;
            return Math.Max(.5f, Math.Min(1.5f, value));
        }
    }
}
