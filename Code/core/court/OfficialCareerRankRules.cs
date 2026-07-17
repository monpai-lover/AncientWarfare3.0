using System;

namespace AncientWarfare3.core.court
{
    public static class OfficialCareerRankRules
    {
        public const int CivilTrack = 0;
        public const int MilitaryTrack = 1;
        public const int MinimumRank = 1;
        public const int AutomaticRankCeiling = 17;
        public const int MaximumRank = 18;

        public static int ClampRank(int pRank)
        {
            return Math.Max(MinimumRank, Math.Min(MaximumRank, pRank));
        }

        public static int EntryRank(bool cityLeaderOrGeneral, bool schoolGuest,
            int age, bool royal, bool highPrestige)
        {
            int rank = cityLeaderOrGeneral ? 5 : schoolGuest ? 3 : 1;
            if (age >= 50) rank += 3;
            else if (age >= 40) rank += 2;
            else if (age >= 30) rank += 1;
            if (royal) rank += 3;
            if (highPrestige) rank += 2;
            return Math.Min(13, ClampRank(rank));
        }

        public static int ResolveTrack(bool militaryOffice, bool activeGeneral)
        {
            return militaryOffice || activeGeneral ? MilitaryTrack : CivilTrack;
        }

        public static int TermLength(int age, int lastEvaluation, long actorId,
            int currentYear)
        {
            if (lastEvaluation == 0) return 3;
            int minimum;
            int maximum;
            if (age >= 60) { minimum = 3; maximum = 4; }
            else if (age >= 50) { minimum = 3; maximum = 5; }
            else if (age >= 40) { minimum = 4; maximum = 5; }
            else if (age >= 30) { minimum = 4; maximum = 6; }
            else { minimum = 5; maximum = 6; }

            int range = maximum - minimum + 1;
            int length = minimum + StablePercentage(actorId, currentYear,
                age + lastEvaluation * 31) % range;
            return lastEvaluation == 1 ? Math.Max(3, length - 1) : length;
        }

        public static int MeritCap(int pOfficeGrade)
        {
            if (pOfficeGrade == 10) return 9;
            if (pOfficeGrade == 20) return 6;
            if (pOfficeGrade == 30) return 3;
            return 1;
        }

        public static int EvaluationGrade(int mainAttribute, bool privileged,
            bool purpleRank, bool positiveGrowth, bool negativeGrowth, int roll)
        {
            int upper;
            int middle;
            if (privileged) { upper = 55; middle = 40; }
            else if (purpleRank) { upper = 40; middle = 50; }
            else if (mainAttribute >= 28 || positiveGrowth) { upper = 25; middle = 60; }
            else if (mainAttribute >= 17 && !negativeGrowth) { upper = 15; middle = 50; }
            else { upper = 0; middle = 30; }

            int normalizedRoll = NormalizePercentage(roll);
            if (normalizedRoll < upper)
                return mainAttribute >= 28 ? 0 : 1;
            if (normalizedRoll < upper + middle) return 2;
            return mainAttribute < 17 || negativeGrowth ? 4 : 3;
        }

        public static int RankDelta(int evaluationGrade, bool privileged,
            int roll)
        {
            if (evaluationGrade >= 4) return -2;
            if (evaluationGrade == 3) return -1;
            int normalizedRoll = NormalizePercentage(roll);
            if (privileged)
            {
                if (normalizedRoll < 20) return 1;
                return normalizedRoll < 70 ? 2 : 3;
            }
            if (evaluationGrade <= 0)
            {
                if (normalizedRoll < 35) return 1;
                return normalizedRoll < 80 ? 2 : 3;
            }
            if (evaluationGrade == 1)
                return normalizedRoll < 70 ? 1 : 2;
            return 1;
        }

        public static int ApplyAutomaticRankChange(int currentRank, int delta)
        {
            int current = ClampRank(currentRank);
            if (current == MaximumRank && delta >= 0) return MaximumRank;
            int next = current + delta;
            return Math.Max(MinimumRank, Math.Min(AutomaticRankCeiling, next));
        }

        public static float InfluenceMultiplier(int pRank)
        {
            int rank = ClampRank(pRank);
            if (rank <= 4) return 1.02f;
            if (rank <= 8) return 1.05f;
            if (rank <= 12) return 1.08f;
            if (rank <= 16) return 1.11f;
            return rank == 17 ? 1.14f : 1.17f;
        }

        public static int RequiredRankForOfficeGrade(int pOfficeGrade)
        {
            if (pOfficeGrade == 10) return 13;
            if (pOfficeGrade == 20) return 9;
            if (pOfficeGrade == 30) return 5;
            return MinimumRank;
        }

        public static float OfficeRankMatchScore(int pRank, int pOfficeGrade)
        {
            int difference = ClampRank(pRank) - RequiredRankForOfficeGrade(pOfficeGrade);
            if (difference == 0) return 8f;
            if (difference < 0) return Math.Max(-24f, difference * 3f);
            return Math.Max(2f, 8f - difference * 0.5f);
        }

        public static int DeterministicRoll(long pActorId, int pYear, int pSalt)
        {
            return StablePercentage(pActorId, pYear, pSalt);
        }

        public static float EvaluationMeritMultiplier(int pEvaluationGrade)
        {
            switch (pEvaluationGrade)
            {
                case 0: return 1.07f;
                case 1: return 1.05f;
                case 2: return 1.03f;
                case 3: return 0.95f;
                default: return 0.90f;
            }
        }

        public static float EvaluationMeritAdjustment(int pEvaluationGrade)
        {
            switch (pEvaluationGrade)
            {
                case 0: return 0.50f;
                case 1: return 0.35f;
                case 2: return 0.15f;
                case 3: return -0.15f;
                default: return -0.35f;
            }
        }

        public static float AnnualCivilMerit(float pTaxValue,
            float pFoodStability, float pUnrestRisk)
        {
            float value = 0.15f + Math.Max(0f, pTaxValue) * 0.002f +
                          Math.Max(0f, pFoodStability) * 0.004f -
                          Math.Max(0f, pUnrestRisk) * 0.002f;
            return Math.Max(0f, Math.Min(1f, value));
        }

        public static float AnnualMilitaryMerit(int pGeneralMerit, int pTroopPower)
        {
            float value = 0.10f + Math.Max(0, pGeneralMerit) * 0.005f +
                          Math.Max(0, pTroopPower) * 0.002f;
            return Math.Max(0f, Math.Min(1f, value));
        }

        public static float ApplyMerit(float pCurrent, float pDelta, int pCap)
        {
            return Math.Max(0f, Math.Min(Math.Max(0, pCap), pCurrent + pDelta));
        }

        private static int StablePercentage(long pActorId, int pYear, int pSalt)
        {
            unchecked
            {
                ulong value = (ulong)pActorId;
                value ^= (ulong)(uint)pYear * 0x9E3779B185EBCA87UL;
                value ^= (ulong)(uint)pSalt * 0xC2B2AE3D27D4EB4FUL;
                value ^= value >> 33;
                value *= 0xFF51AFD7ED558CCDUL;
                value ^= value >> 33;
                return (int)(value % 100UL);
            }
        }

        private static int NormalizePercentage(int pValue)
        {
            int value = pValue % 100;
            return value < 0 ? value + 100 : value;
        }
    }
}
