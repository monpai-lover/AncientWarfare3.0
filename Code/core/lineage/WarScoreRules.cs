using System;

namespace AncientWarfare3.core.lineage
{
    public enum WarScoreSide
    {
        None = 0,
        Attackers = 1,
        Defenders = 2
    }

    public readonly struct WarScoreCityFacts
    {
        public WarScoreCityFacts(long pCityId, float pDevelopment,
            int pPopulation, int pZoneCount, int pBuildingCount,
            bool pIsCapital, bool pMatchesActiveWarGoal,
            bool isOnlyLiveCity = false,
            int pInitialOwnerCityCount = 1)
        {
            CityId = pCityId;
            Development = pDevelopment;
            Population = pPopulation;
            ZoneCount = pZoneCount;
            BuildingCount = pBuildingCount;
            IsCapital = pIsCapital;
            MatchesActiveWarGoal = pMatchesActiveWarGoal;
            IsOnlyLiveCity = isOnlyLiveCity;
            InitialOwnerCityCount = WarParticipantCityBaselineRules.
                NormalizeInitialCityCount(pInitialOwnerCityCount);
        }

        public long CityId { get; }
        public float Development { get; }
        public int Population { get; }
        public int ZoneCount { get; }
        public int BuildingCount { get; }
        public bool IsCapital { get; }
        public bool MatchesActiveWarGoal { get; }
        public bool IsOnlyLiveCity { get; }
        public int InitialOwnerCityCount { get; }
    }

    public static class WarScoreRules
    {
        public const int MaximumScore = 100;
        public const int CapitalOccupationMinimum = 20;
        public const int CapitalWarGoalOccupationMinimum = 25;
        public const int MaximumCityEvent = CapitalWarGoalOccupationMinimum;
        public const int MinimumCityScoreBudget = 60;
        public const int LimitedTerritorialCityScoreBudget = 75;
        public const int DefaultCityScoreBudget = 85;
        public const int MaximumCityScoreBudget = 100;
        public const int MaximumCityScore = DefaultCityScoreBudget;
        public const int MaximumBattleEvent = 8;
        public const int MaximumBattleScore = 20;
        public const int MaximumGoalEvent = 25;
        public const int MaximumGoalScore = 25;
        public const int MaximumLossScore = 20;
        public const int MaximumBaseDurationExhaustion = 40;
        public const int MaximumDurationExhaustion = 100;
        public const int MaximumLossExhaustion = 60;
        public const int LongWarGraceYears = 15;
        public const int LongWarAnnualExhaustion = 20;
        public const int NonNegotiableWarTargetYears = 40;

        public static int CityScoreBudgetForWarType(string pWarType)
        {
            return pWarType switch
            {
                "tributary_war" or "vassal_war" =>
                    MinimumCityScoreBudget,
                "reclaim" or "restoration_war" =>
                    LimitedTerritorialCityScoreBudget,
                "tianming" or "tianmingrebel" =>
                    MaximumCityScoreBudget,
                _ => DefaultCityScoreBudget
            };
        }

        public static int NormalizeCityScoreBudget(int pBudget)
        {
            return Math.Max(MinimumCityScoreBudget,
                Math.Min(MaximumCityScoreBudget, pBudget));
        }

        public static int CityControlValue(WarScoreCityFacts pFacts)
        {
            return CityControlValue(pFacts, DefaultCityScoreBudget);
        }

        public static int CityControlValue(WarScoreCityFacts pFacts,
            int pCityScoreBudget)
        {
            int cityScoreBudget = NormalizeCityScoreBudget(pCityScoreBudget);
            float development = Clamp01(pFacts.Development);
            float population = Clamp01(Math.Max(0, pFacts.Population) / 180f);
            float zones = Clamp01(Math.Max(0, pFacts.ZoneCount) / 25f);
            float buildings = Clamp01(Math.Max(0, pFacts.BuildingCount) / 30f);
            float scale = population * .55f + zones * .25f + buildings * .20f;
            float quality = development * .60f + scale * .40f;
            int qualityBonus = (int)Math.Round(quality * 3f,
                MidpointRounding.AwayFromZero);
            int initialCityCount = WarParticipantCityBaselineRules.
                NormalizeInitialCityCount(pFacts.InitialOwnerCityCount);
            int territoryShare = (cityScoreBudget + initialCityCount - 1) /
                                 initialCityCount;
            int value = territoryShare + qualityBonus +
                        (pFacts.MatchesActiveWarGoal ? 2 : 0);
            if (pFacts.IsCapital && pFacts.MatchesActiveWarGoal)
                return CapitalWarGoalOccupationMinimum;
            if (pFacts.IsCapital)
                value = Math.Max(value, CapitalOccupationMinimum);
            return Math.Max(2, Math.Min(MaximumCityEvent, value));
        }

        public static int CityControlContribution(WarScoreCityFacts pFacts,
            WarScoreSide pHomeSide, WarScoreSide pControllerSide)
        {
            return CityControlContribution(pFacts, pHomeSide,
                pControllerSide, DefaultCityScoreBudget);
        }

        public static int CityControlContribution(WarScoreCityFacts pFacts,
            WarScoreSide pHomeSide, WarScoreSide pControllerSide,
            int pCityScoreBudget)
        {
            if (!IsParticipantSide(pHomeSide) ||
                !IsParticipantOrNone(pControllerSide) ||
                pHomeSide == pControllerSide ||
                pControllerSide == WarScoreSide.None) return 0;
            int value = CityControlValue(pFacts, pCityScoreBudget);
            return pControllerSide == WarScoreSide.Attackers ? value : -value;
        }

        public static int BattleDelta(WarScoreSide pWinnerSide,
            int pIntensity)
        {
            if (!IsParticipantSide(pWinnerSide) || pIntensity <= 0) return 0;
            double scaled = Math.Sqrt(pIntensity) / 4d;
            int magnitude = Math.Max(1, Math.Min(MaximumBattleEvent,
                (int)Math.Ceiling(scaled)));
            return pWinnerSide == WarScoreSide.Attackers
                ? magnitude
                : -magnitude;
        }

        public static int BattleScoreFromCasualties(int pAttackerLosses,
            int pDefenderLosses)
        {
            long difference = (long)Math.Max(0, pDefenderLosses) -
                              Math.Max(0, pAttackerLosses);
            if (difference == 0) return 0;
            long magnitude = (Math.Abs(difference) + 4L) / 5L;
            int score = (int)Math.Min(MaximumBattleScore, magnitude);
            return difference > 0 ? score : -score;
        }

        public static int LossScore(int pAttackerLosses,
            int pDefenderLosses)
        {
            long attacker = Math.Max(0, pAttackerLosses);
            long defender = Math.Max(0, pDefenderLosses);
            long total = attacker + defender;
            if (total <= 0) return 0;
            double balance = (defender - attacker) / (double)total;
            double saturation = Math.Min(1d, total / 25d);
            return ClampSigned((int)Math.Round(
                balance * saturation * MaximumLossScore,
                MidpointRounding.AwayFromZero), MaximumLossScore);
        }

        public static int WarExhaustion(int pDurationYears, int pOwnLosses)
        {
            long years = Math.Max(0L, pDurationYears);
            int baseDuration = (int)Math.Min(
                MaximumBaseDurationExhaustion, years * 3L);
            long longWarYears = Math.Max(0L,
                years - LongWarGraceYears);
            int longWar = (int)Math.Min(MaximumDurationExhaustion,
                longWarYears * LongWarAnnualExhaustion);
            int duration = Math.Min(MaximumDurationExhaustion,
                baseDuration + longWar);
            int losses = Math.Min(MaximumLossExhaustion,
                (int)Math.Round(Math.Sqrt(Math.Max(0, pOwnLosses)) * 4d,
                    MidpointRounding.AwayFromZero));
            return Math.Min(100, duration + losses);
        }

        public static int WarExhaustion(int pDurationYears, int pOwnLosses,
            int pMobilizationBaseline)
        {
            long years = Math.Max(0L, pDurationYears);
            int baseDuration = (int)Math.Min(
                MaximumBaseDurationExhaustion, years * 3L);
            long longWarYears = Math.Max(0L,
                years - LongWarGraceYears);
            int longWar = (int)Math.Min(MaximumDurationExhaustion,
                longWarYears * LongWarAnnualExhaustion);
            int duration = Math.Min(MaximumDurationExhaustion,
                baseDuration + longWar);
            return Math.Min(100, duration +
                CasualtyExhaustion(pOwnLosses, pMobilizationBaseline));
        }

        public static int NonNegotiableWarExhaustion(int pDurationYears,
            int pOwnLosses, int pMobilizationBaseline)
        {
            long years = Math.Max(0L, pDurationYears);
            int duration = (int)Math.Min(MaximumDurationExhaustion,
                years * MaximumDurationExhaustion /
                NonNegotiableWarTargetYears);
            return Math.Min(100, duration +
                CasualtyExhaustion(pOwnLosses, pMobilizationBaseline));
        }

        public static int CasualtyExhaustion(int pOwnLosses,
            int pMobilizationBaseline)
        {
            if (pMobilizationBaseline <= 0) return 0;
            double ratio = Math.Max(0, pOwnLosses) /
                           (double)Math.Max(1, pMobilizationBaseline);
            return Math.Min(MaximumLossExhaustion, (int)Math.Round(
                ratio * MaximumLossExhaustion,
                MidpointRounding.AwayFromZero));
        }

        public static int ComposeSignedScore(params int[] pComponents)
        {
            if (pComponents == null) return 0;
            long total = 0;
            for (int i = 0; i < pComponents.Length; i++)
                total += pComponents[i];
            return (int)Math.Max(-MaximumScore,
                Math.Min(MaximumScore, total));
        }

        public static int ResolveStickyDecisiveScore(int pExistingDecisive,
            int pComposedScore)
        {
            if (pExistingDecisive >= MaximumScore)
                return MaximumScore;
            if (pExistingDecisive <= -MaximumScore)
                return -MaximumScore;
            if (pComposedScore >= MaximumScore)
                return MaximumScore;
            if (pComposedScore <= -MaximumScore)
                return -MaximumScore;
            return 0;
        }

        public static int ResolveDecisiveOccupationScore(int pBaseScore,
            WarScoreSide pHomeSide, WarScoreSide pControllerSide,
            bool isOnlyLiveCity, bool captureComplete)
        {
            if (!isOnlyLiveCity || !captureComplete ||
                !IsParticipantSide(pHomeSide) ||
                !IsParticipantSide(pControllerSide) ||
                pHomeSide == pControllerSide)
                return ClampSigned(pBaseScore, MaximumScore);
            return pControllerSide == WarScoreSide.Attackers
                ? MaximumScore
                : -MaximumScore;
        }

        public static int ResolveRealmOccupationDecisiveScore(
            WarScoreSide pHomeSide, int occupiedHostileCityCount,
            int remainingHomeCityCount)
        {
            if (!IsParticipantSide(pHomeSide) ||
                remainingHomeCityCount < 0 || occupiedHostileCityCount <= 0 ||
                occupiedHostileCityCount < Math.Max(1,
                    remainingHomeCityCount))
                return 0;
            return pHomeSide == WarScoreSide.Defenders
                ? MaximumScore
                : -MaximumScore;
        }

        public static int ForSide(int pAttackerSignedScore,
            WarScoreSide pSide)
        {
            int score = ClampSigned(pAttackerSignedScore, MaximumScore);
            if (pSide == WarScoreSide.Defenders) return -score;
            return pSide == WarScoreSide.Attackers ? score : 0;
        }

        public static int ClampCityScore(int pValue)
        {
            return ClampCityScore(pValue, DefaultCityScoreBudget);
        }

        public static int ClampCityScore(int pValue, int pCityScoreBudget)
        {
            return ClampSigned(pValue,
                NormalizeCityScoreBudget(pCityScoreBudget));
        }

        public static int ClampBattleScore(int pValue)
        {
            return ClampSigned(pValue, MaximumBattleScore);
        }

        public static int ClampGoalScore(int pValue)
        {
            return ClampSigned(pValue, MaximumGoalScore);
        }

        public static int NormalizeGoalValue(int pValue)
        {
            return Math.Max(1, Math.Min(MaximumGoalEvent, pValue));
        }

        public static bool IsParticipantSide(WarScoreSide pSide)
        {
            return pSide == WarScoreSide.Attackers ||
                   pSide == WarScoreSide.Defenders;
        }

        public static bool ShouldHoldFrozenControl(bool pFrozen,
            bool pControllerAlive, bool pControllerStillParticipant,
            bool pDominantPresence)
        {
            return pFrozen && pControllerAlive &&
                   pControllerStillParticipant && !pDominantPresence;
        }

        private static bool IsParticipantOrNone(WarScoreSide pSide)
        {
            return pSide == WarScoreSide.None || IsParticipantSide(pSide);
        }

        private static int ClampSigned(int pValue, int pMaximum)
        {
            return Math.Max(-pMaximum, Math.Min(pMaximum, pValue));
        }

        private static float Clamp01(float pValue)
        {
            return Math.Max(0f, Math.Min(1f, pValue));
        }
    }
}
