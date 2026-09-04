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

        /// <summary>
        ///     监察官(censor 层)的中央腐败压力折减。每有一位在任监察官按影响力
        ///     折减,边际递减 —— 第一位监察官作用最大,之后递减,符合「监察是
        ///     降低腐败的制度性设计」。影响力为 0 或无人时折减为 0。
        /// </summary>
        public static float CensorialPressureRelief(int censorialCount,
            float censorialInfluence)
        {
            if (censorialCount <= 0) return 0f;
            float influence = Math.Max(0f, censorialInfluence);
            // 影响力折算成等效监察压力折减:单官上限 4 分,影响力越高越接近上限。
            float perOfficial = influence > 0f
                ? 4f * (1f - (float)Math.Exp(-influence / 20f))
                : 1f;
            // 边际递减:第 N 位的贡献按 1/N 递减,总数逼近 6 分封顶。
            float total = 0f;
            for (int i = 0; i < censorialCount; i++)
                total += perOfficial / (i + 1f);
            return Math.Max(0f, Math.Min(6f, total));
        }

        /// <summary>
        ///     地方监察官对**城层官方腐败压力**的折减。与中央不同,地方反腐
        ///     难度加大:折减上限低(2 分 vs 中央 6 分),且受官府效率制约 ——
        ///     官府效率越低(越糜烂),同样的监察官能查出的越少,折减越有限。
        ///     这体现「天高皇帝远,地方监察的执行力先天弱于中央都察院」。
        /// </summary>
        public static float ApplyLocalCensorRelief(float officialPressure,
            int localCensorCount, float localCensorInfluence,
            float bureauEfficiency)
        {
            if (localCensorCount <= 0) return officialPressure;
            float influence = Math.Max(0f, localCensorInfluence);
            float perOfficial = influence > 0f
                ? 2f * (1f - (float)Math.Exp(-influence / 25f))
                : 0.5f;
            float total = 0f;
            for (int i = 0; i < localCensorCount; i++)
                total += perOfficial / (i + 1f);
            total = Math.Max(0f, Math.Min(2f, total));
            // 官府效率制约:效率越高(官府越清明),监察官越能发挥;效率低
            // (官府糜烂)时监察官难有作为,折减按 (100-效率)/100 折扣。
            float boundedEfficiency = Math.Max(0f,
                Math.Min(100f, float.IsNaN(bureauEfficiency) ? 0f
                    : bureauEfficiency));
            float effectiveness = 0.5f + 0.5f * boundedEfficiency / 100f;
            float relief = total * effectiveness;
            return Math.Max(0f, officialPressure - relief);
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
