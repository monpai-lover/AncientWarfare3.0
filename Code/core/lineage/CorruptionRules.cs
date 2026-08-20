using System;

namespace AncientWarfare3.core.lineage
{
    internal enum CorruptionSeverity
    {
        Controlled,
        Elevated,
        High,
        Extreme
    }

    internal static class CorruptionRules
    {
        public const int HighThreshold = 60;
        public const int VeryHighThreshold = 80;
        public const int CleanupAvailabilityThreshold = 40;
        public const int CleanupCountryReduction = 20;
        public const int CleanupCityReduction = 10;
        public const int CleanupCityCount = 3;
        public const int CleanupCost = 50;
        public const float Inertia = 0.25f;
        public const float CleanupPressureMultiplier = 0.85f;

        public static int ClampScore(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0;
            return (int)Math.Round(Math.Max(0f, Math.Min(100f, value)));
        }

        public static CorruptionSeverity GetSeverity(int score)
        {
            int value = ClampScore(score);
            if (value >= VeryHighThreshold) return CorruptionSeverity.Extreme;
            if (value >= HighThreshold) return CorruptionSeverity.High;
            if (value >= 31) return CorruptionSeverity.Elevated;
            return CorruptionSeverity.Controlled;
        }

        public static int AdvanceInertia(int previous, float target)
        {
            float next = ClampScore(previous) +
                         (ClampScore(target) - ClampScore(previous)) * Inertia;
            return ClampScore(next);
        }

        public static int AdvanceStreak(int previous, int score, int threshold,
            int cap)
        {
            if (ClampScore(score) < threshold) return 0;
            return Math.Min(Math.Max(0, cap), Math.Max(0, previous) + 1);
        }

        public static int WeightedAverage(long weightedScore, long population)
        {
            if (population <= 0) return 0;
            return ClampScore((float)(weightedScore / (double)population));
        }

        public static int ConversionChanceBasisPoints(int countryScore)
        {
            int score = ClampScore(countryScore);
            if (score >= VeryHighThreshold) return 6000;
            if (score >= HighThreshold) return 3500;
            if (score >= 31) return 1500;
            return 500;
        }

        public static bool ShouldConvertCandidate(long originId,
            long candidateId, int year, int countryScore)
        {
            int chance = ConversionChanceBasisPoints(countryScore);
            uint hash = StableHash(originId, candidateId, year);
            return hash % 10000u < chance;
        }

        public static uint StableHash(long originId, long candidateId, int year)
        {
            unchecked
            {
                uint hash = 2166136261u;
                Mix(ref hash, (ulong)originId);
                Mix(ref hash, (ulong)candidateId);
                Mix(ref hash, (ulong)(uint)year);
                return hash;
            }
        }

        public static int ReduceScore(int score, int reduction)
        {
            return Math.Max(0, ClampScore(score) - Math.Max(0, reduction));
        }

        public static float LocalOfficialPressure(bool hasBureau,
            int officerCount, int slots, float efficiency)
        {
            if (!hasBureau) return 0f;
            float boundedEfficiency = Math.Max(0f,
                Math.Min(100f, float.IsNaN(efficiency) ? 0f : efficiency));
            int safeSlots = Math.Max(1, slots);
            float vacancyRatio = 1f - Math.Min(1f,
                Math.Max(0, officerCount) / (float)safeSlots);
            return Math.Max(0f, Math.Min(28f,
                (100f - boundedEfficiency) * 0.18f +
                vacancyRatio * 10f));
        }

        private static void Mix(ref uint hash, ulong value)
        {
            unchecked
            {
                for (int index = 0; index < 8; index++)
                {
                    hash ^= (byte)(value >> (index * 8));
                    hash *= 16777619u;
                }
            }
        }
    }
}
