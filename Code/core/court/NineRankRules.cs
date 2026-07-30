using System;

namespace AncientWarfare3.core.court
{
    public static class NineRankRules
    {
        public const int ReviewIntervalYears = 6;
        public const int Unranked = 0;
        public const int HighestGrade = 1;
        public const int LowestGrade = 9;

        public static bool ShouldReview(int pLastReviewYear,
            int pCurrentYear)
        {
            return pLastReviewYear < 0 ||
                   pCurrentYear - pLastReviewYear >= ReviewIntervalYears;
        }

        public static int ResolveGrade(bool noble, int ability,
            int schoolYears, float schoolReputation, int schoolStanding,
            int evaluationGrade, float meritRatio)
        {
            int score = noble ? 18 : 0;
            score += Math.Max(0, Math.Min(40, ability));
            score += Math.Min(10, Math.Max(0, schoolYears) / 2);
            score += (int)Math.Round(Math.Max(0f,
                Math.Min(100f, schoolReputation)) * 0.10f);
            score += StandingBonus(schoolStanding);
            score += EvaluationBonus(evaluationGrade);
            score += (int)Math.Round(Math.Max(0f,
                Math.Min(1f, meritRatio)) * 14f);

            if (score >= 72) return 1;
            if (score >= 62) return 2;
            if (score >= 52) return 3;
            if (score >= 44) return 4;
            if (score >= 36) return 5;
            if (score >= 28) return 6;
            if (score >= 20) return 7;
            if (score >= 12) return 8;
            return 9;
        }

        public static float AppointmentScore(int pGrade)
        {
            switch (ClampGrade(pGrade))
            {
                case 1: return 20f;
                case 2: return 16f;
                case 3: return 12f;
                case 4: return 8f;
                case 5: return 5f;
                case 6: return 3f;
                default: return 0f;
            }
        }

        public static int EntryRankBonus(int pGrade)
        {
            switch (ClampGrade(pGrade))
            {
                case 1: return 5;
                case 2: return 4;
                case 3: return 3;
                case 4: return 2;
                case 5: return 1;
                default: return 0;
            }
        }

        public static int ClampGrade(int pGrade)
        {
            if (pGrade <= Unranked) return Unranked;
            return Math.Max(HighestGrade, Math.Min(LowestGrade, pGrade));
        }

        public static string GradeNameKey(int pGrade)
        {
            if (ClampGrade(pGrade) == Unranked)
                return "aw_court_local_grade_unranked";
            return "aw_court_local_grade_" + ClampGrade(pGrade);
        }

        public static string GradeFallbackEnglish(int pGrade)
        {
            if (ClampGrade(pGrade) == Unranked) return "Unranked";
            switch (ClampGrade(pGrade))
            {
                case 1: return "Upper-upper";
                case 2: return "Upper-middle";
                case 3: return "Upper-lower";
                case 4: return "Middle-upper";
                case 5: return "Middle-middle";
                case 6: return "Middle-lower";
                case 7: return "Lower-upper";
                case 8: return "Lower-middle";
                default: return "Lower-lower";
            }
        }

        private static int StandingBonus(int pStanding)
        {
            if (pStanding >= 4) return 8;
            if (pStanding == 3) return 6;
            if (pStanding == 2) return 4;
            if (pStanding == 1) return 2;
            return 0;
        }

        private static int EvaluationBonus(int pGrade)
        {
            switch (pGrade)
            {
                case 0: return 12;
                case 1: return 9;
                case 2: return 5;
                case 3: return 1;
                default: return -5;
            }
        }
    }
}
