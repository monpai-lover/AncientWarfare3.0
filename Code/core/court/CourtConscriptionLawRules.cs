namespace AncientWarfare3.core.court
{
    public enum CourtConscriptionLaw
    {
        Limited,
        Standard,
        Expanded,
        FullMobilization
    }

    public static class CourtConscriptionLawRules
    {
        public const CourtConscriptionLaw DefaultLaw =
            CourtConscriptionLaw.Standard;

        public static int ReservePercent(CourtConscriptionLaw pLaw)
        {
            return pLaw switch
            {
                CourtConscriptionLaw.Limited => 30,
                CourtConscriptionLaw.Expanded => 70,
                CourtConscriptionLaw.FullMobilization => 100,
                _ => 50
            };
        }

        public static int Capacity(int pEligibleCivilians, int pPercent)
        {
            long eligible = System.Math.Max(0, pEligibleCivilians);
            long share = System.Math.Max(0, System.Math.Min(100, pPercent));
            return (int)System.Math.Min(int.MaxValue,
                eligible * share / 100L);
        }

        public static int Score(CourtConscriptionLaw pLaw,
            string pDominantSchool, float pLivelihood, float pPeace,
            float pWar, float pAggression, bool pExistentialDefense,
            bool pCapitalThreat, bool pSevereDisadvantage)
        {
            float life = Clamp01(pLivelihood);
            float calm = Clamp01(pPeace);
            float martial = Clamp01(pWar) + Clamp01(pAggression);
            bool restraint = pDominantSchool == CourtSchoolId.Agrarian ||
                pDominantSchool == CourtSchoolId.Dao ||
                pDominantSchool == CourtSchoolId.Medical;
            bool hardLine = pDominantSchool == CourtSchoolId.Military ||
                pDominantSchool == CourtSchoolId.Legalist;
            bool emergency = pExistentialDefense || pCapitalThreat ||
                pSevereDisadvantage;
            return pLaw switch
            {
                CourtConscriptionLaw.Limited => 30 +
                    Round((life + calm) * 35f) + (restraint ? 35 : 0) -
                    (emergency ? 100 : 0),
                CourtConscriptionLaw.Expanded => 35 +
                    Round(martial * 35f) + (hardLine ? 35 : 0) +
                    (emergency ? 20 : 0),
                CourtConscriptionLaw.FullMobilization => emergency
                    ? 145 + (hardLine ? 20 : 0)
                    : -120,
                _ => 75
            };
        }

        private static float Clamp01(float pValue)
        {
            return System.Math.Max(0f, System.Math.Min(1f, pValue));
        }

        private static int Round(float pValue)
        {
            return (int)System.Math.Round(pValue,
                System.MidpointRounding.AwayFromZero);
        }
    }
}
