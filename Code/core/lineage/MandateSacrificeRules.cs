using System;

namespace AncientWarfare3.core.lineage
{
    public enum MandateSacrificeLevel
    {
        Gamble,
        Moderate,
        Conservative
    }

    public enum MandateSacrificeOutcome
    {
        Auspicious,
        Neutral,
        Ominous
    }

    public readonly struct MandateSacrificeEffects
    {
        public MandateSacrificeEffects(int pMandateDelta, int pAuthorityDelta,
            int pPrestigeDelta, int pAnnualMandateDelta)
        {
            MandateDelta = pMandateDelta;
            AuthorityDelta = pAuthorityDelta;
            PrestigeDelta = pPrestigeDelta;
            AnnualMandateDelta = pAnnualMandateDelta;
        }

        public int MandateDelta { get; }
        public int AuthorityDelta { get; }
        public int PrestigeDelta { get; }
        public int AnnualMandateDelta { get; }
    }

    public static class MandateSacrificeRules
    {
        public const int CooldownYears = 5;
        public const int BuffYears = 5;

        public static MandateSacrificeOutcome ResolveOutcome(
            MandateSacrificeLevel pLevel, bool pQualified, int pRollBasisPoints)
        {
            int roll = Math.Max(0, Math.Min(9999, pRollBasisPoints));
            switch (pLevel)
            {
                case MandateSacrificeLevel.Gamble:
                    if (pQualified)
                    {
                        if (roll < 3900) return MandateSacrificeOutcome.Auspicious;
                        return roll < 9900
                            ? MandateSacrificeOutcome.Neutral
                            : MandateSacrificeOutcome.Ominous;
                    }
                    if (roll < 2500) return MandateSacrificeOutcome.Auspicious;
                    return roll < 9000
                        ? MandateSacrificeOutcome.Neutral
                        : MandateSacrificeOutcome.Ominous;
                case MandateSacrificeLevel.Moderate:
                    if (pQualified)
                    {
                        if (roll < 1500) return MandateSacrificeOutcome.Auspicious;
                        return roll < 9000
                            ? MandateSacrificeOutcome.Neutral
                            : MandateSacrificeOutcome.Ominous;
                    }
                    if (roll < 500) return MandateSacrificeOutcome.Auspicious;
                    return roll < 7500
                        ? MandateSacrificeOutcome.Neutral
                        : MandateSacrificeOutcome.Ominous;
                default:
                    int auspiciousThreshold = pQualified ? 5000 : 4000;
                    return roll < auspiciousThreshold
                        ? MandateSacrificeOutcome.Auspicious
                        : MandateSacrificeOutcome.Neutral;
            }
        }

        public static MandateSacrificeEffects Effects(MandateSacrificeLevel pLevel,
            MandateSacrificeOutcome pOutcome)
        {
            switch (pLevel)
            {
                case MandateSacrificeLevel.Gamble:
                    return pOutcome switch
                    {
                        MandateSacrificeOutcome.Auspicious =>
                            new MandateSacrificeEffects(12, 6, 8, 2),
                        MandateSacrificeOutcome.Ominous =>
                            new MandateSacrificeEffects(-12, -6, -6, -2),
                        _ => new MandateSacrificeEffects(3, 2, 2, 0)
                    };
                case MandateSacrificeLevel.Moderate:
                    return pOutcome switch
                    {
                        MandateSacrificeOutcome.Auspicious =>
                            new MandateSacrificeEffects(8, 4, 5, 1),
                        MandateSacrificeOutcome.Ominous =>
                            new MandateSacrificeEffects(-8, -4, -4, -1),
                        _ => new MandateSacrificeEffects(3, 2, 2, 0)
                    };
                default:
                    return pOutcome == MandateSacrificeOutcome.Auspicious
                        ? new MandateSacrificeEffects(6, 3, 4, 1)
                        : new MandateSacrificeEffects(2, 1, 1, 0);
            }
        }

        public static int Cost(MandateSacrificeLevel pLevel)
        {
            return pLevel switch
            {
                MandateSacrificeLevel.Gamble => 55,
                MandateSacrificeLevel.Moderate => 40,
                MandateSacrificeLevel.Conservative => 75,
                _ => 40
            };
        }

        public static bool CooldownReady(int pCurrentYear, int pLastYear)
        {
            if (pLastYear == int.MinValue) return true;
            return (long)pCurrentYear - pLastYear >= CooldownYears;
        }

        public static float SpendForYear(float pPoliticalPoints, float pRemainingCost,
            float pMaximumYearlySpend)
        {
            float available = Math.Max(0f, pPoliticalPoints);
            float remaining = Math.Max(0f, pRemainingCost);
            float maximum = Math.Max(0f, pMaximumYearlySpend);
            return Math.Min(available, Math.Min(remaining, maximum));
        }

        public static int ActiveAnnualDelta(int pCurrentYear, int pBuffUntilYear,
            int pStoredDelta)
        {
            return pBuffUntilYear >= pCurrentYear ? pStoredDelta : 0;
        }

        public static bool ShouldAutoSacrifice(MandatePhase phase, int mandateValue,
            int catalystScore)
        {
            return phase != MandatePhase.Golden ||
                   mandateValue < 60 || catalystScore >= 40;
        }

        public static MandateSacrificeLevel PreferredAiLevel(MandatePhase phase,
            bool qualified)
        {
            if (!qualified) return MandateSacrificeLevel.Conservative;
            return phase switch
            {
                MandatePhase.Chaos => MandateSacrificeLevel.Gamble,
                MandatePhase.Decline => MandateSacrificeLevel.Moderate,
                MandatePhase.Renewal => MandateSacrificeLevel.Conservative,
                _ => MandateSacrificeLevel.Moderate
            };
        }
    }
}
