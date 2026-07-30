using System;

namespace AncientWarfare3.core.court
{
    public static class CourtPetitionRules
    {
        public const int MoneyCost = 5;
        public const int MaximumRank = 8;
        public const int MinimumAmbition = 60;
        public const int CooldownYears = 6;
        public const int FavorDurationYears = 6;
        public const int MaximumPetitionsPerKingdomYear = 4;
        public const float FavorGain = 3f;
        public const float MaximumFavor = 20f;

        public static bool IsEligible(bool activeOfficial, int rank,
            int ambition, int money, int currentYear, int lastPetitionYear)
        {
            return activeOfficial && rank <= MaximumRank &&
                   ambition >= MinimumAmbition && money >= MoneyCost &&
                   (lastPetitionYear < 0 ||
                    currentYear - lastPetitionYear >= CooldownYears);
        }

        public static bool ShouldAttempt(long actorId, int currentYear,
            int ambition, int rank, bool learnedEntry, int forcedRoll = -1)
        {
            int chance = 5 + Math.Max(0, MaximumRank - rank) +
                         Math.Max(0, ambition - MinimumAmbition) / 10;
            if (learnedEntry) chance -= 5;
            chance = Math.Max(1, Math.Min(20, chance));
            int roll = forcedRoll >= 0
                ? NormalizePercentage(forcedRoll)
                : DeterministicRoll(actorId, currentYear);
            return roll < chance;
        }

        public static int Ambition(bool ambitious, bool content,
            bool greedy, bool deceitful)
        {
            int value = 40;
            if (ambitious) value += 35;
            if (content) value -= 30;
            if (greedy) value += 10;
            if (deceitful) value += 10;
            return Math.Max(0, Math.Min(100, value));
        }

        public static float ApplyFavor(float pCurrent)
        {
            return Math.Max(0f, Math.Min(MaximumFavor,
                pCurrent + FavorGain));
        }

        public static float ActiveFavor(float pFavor, int favorUntilYear,
            int currentYear)
        {
            return favorUntilYear >= currentYear
                ? Math.Max(0f, Math.Min(MaximumFavor, pFavor))
                : 0f;
        }

        private static int DeterministicRoll(long pActorId, int pYear)
        {
            unchecked
            {
                ulong value = (ulong)pActorId;
                value ^= (ulong)(uint)pYear * 0x9E3779B185EBCA87UL;
                value ^= 0xC2B2AE3D27D4EB4FUL;
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
