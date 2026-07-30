using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum WarPeaceTermKind
    {
        WhitePeace,
        GoldPayment,
        MaterialPayment,
        Reparations,
        ReleaseCaptives,
        RenounceClaims,
        ForceTributary,
        CedeCity,
        ForceVassal,
        TakeMandate,
        RestoreKingdom,
        Independence,
        ReunifySuccession,
        NoCbOutcome
    }

    public struct WarPeaceCityValueFacts
    {
        public WarPeaceCityValueFacts(float development, int population,
            int zoneCount, int buildingCount, bool isCapital)
            : this(development, population, zoneCount, buildingCount,
                isCapital, false, false, false)
        {
        }

        public WarPeaceCityValueFacts(float development, int population,
            int zoneCount, int buildingCount, bool isCapital,
            bool demandingSideHasCore, bool demandingSideHasClaim,
            bool ownerHasCore)
        {
            Development = development;
            Population = population;
            ZoneCount = zoneCount;
            BuildingCount = buildingCount;
            IsCapital = isCapital;
            DemandingSideHasCore = demandingSideHasCore;
            DemandingSideHasClaim = demandingSideHasClaim;
            OwnerHasCore = ownerHasCore;
        }

        public float Development;
        public int Population;
        public int ZoneCount;
        public int BuildingCount;
        public bool IsCapital;
        public bool DemandingSideHasCore;
        public bool DemandingSideHasClaim;
        public bool OwnerHasCore;
    }

    public struct WarPeaceAcceptanceFacts
    {
        public WarPeaceAcceptanceFacts(int recipientWarScore,
            int netTermValueForRecipient, int recipientResolve,
            int recipientWarExhaustion, int recipientMilitaryPressure)
            : this(recipientWarScore, netTermValueForRecipient,
                recipientResolve, recipientWarExhaustion,
                recipientMilitaryPressure, false)
        {
        }

        public WarPeaceAcceptanceFacts(int recipientWarScore,
            int netTermValueForRecipient, int recipientResolve,
            int recipientWarExhaustion, int recipientMilitaryPressure,
            bool completeSurrender)
        {
            RecipientWarScore = WarPeaceTermsRules.ClampSignedWarScore(
                recipientWarScore);
            NetTermValueForRecipient = netTermValueForRecipient;
            RecipientResolve = WarPeaceTermsRules.ClampPercent(
                recipientResolve);
            RecipientWarExhaustion = WarPeaceTermsRules.ClampPercent(
                recipientWarExhaustion);
            RecipientMilitaryPressure = WarPeaceTermsRules.ClampPercent(
                recipientMilitaryPressure);
            CompleteSurrender = completeSurrender;
        }

        public int RecipientWarScore;
        public int NetTermValueForRecipient;
        public int RecipientResolve;
        public int RecipientWarExhaustion;
        public int RecipientMilitaryPressure;
        public bool CompleteSurrender;
    }

    public sealed class WarPeaceAcceptanceResult
    {
        public WarPeaceAcceptanceResult(bool accept, bool forced,
            int margin)
        {
            Accept = accept;
            Forced = forced;
            Margin = margin;
        }

        public bool Accept { get; private set; }
        public bool Forced { get; private set; }
        public int Margin { get; private set; }
    }

    public static class WarPeaceTreatySurvivalRules
    {
        public const string FailureReason =
            "full_annexation_conflicts_with_survival_term";

        public static bool RequiresSourceSurvival(WarPeaceTermKind kind)
        {
            return kind == WarPeaceTermKind.ForceVassal ||
                   kind == WarPeaceTermKind.ForceTributary ||
                   kind == WarPeaceTermKind.Reparations;
        }

        public static bool LeavesRequiredSourceAlive(int sourceCityCount,
            int cededCityCount, bool requiresSourceSurvival)
        {
            if (!requiresSourceSurvival || sourceCityCount < 0) return true;
            return sourceCityCount > Math.Max(0, cededCityCount);
        }
    }

    public sealed class WarPeaceTreatySurvivalLedger
    {
        private readonly Dictionary<long, int> _sourceCityCounts =
            new Dictionary<long, int>();
        private readonly Dictionary<long, int> _cededCityCounts =
            new Dictionary<long, int>();
        private readonly HashSet<long> _survivalSources =
            new HashSet<long>();

        public void Observe(WarPeaceTermKind kind, long sourceKingdomId,
            int sourceCityCount)
        {
            if (sourceKingdomId < 0) return;
            if (sourceCityCount >= 0)
            {
                int current;
                if (_sourceCityCounts.TryGetValue(sourceKingdomId,
                        out current))
                    _sourceCityCounts[sourceKingdomId] =
                        Math.Min(current, sourceCityCount);
                else
                    _sourceCityCounts[sourceKingdomId] = sourceCityCount;
            }
            if (kind == WarPeaceTermKind.CedeCity)
            {
                int ceded;
                _cededCityCounts.TryGetValue(sourceKingdomId,
                    out ceded);
                _cededCityCounts[sourceKingdomId] = ceded + 1;
            }
            if (WarPeaceTreatySurvivalRules.RequiresSourceSurvival(kind))
                _survivalSources.Add(sourceKingdomId);
        }

        public bool Validate(out string reason)
        {
            foreach (long source in _survivalSources)
            {
                int cityCount;
                int ceded;
                _sourceCityCounts.TryGetValue(source, out cityCount);
                if (!_sourceCityCounts.ContainsKey(source)) cityCount = -1;
                _cededCityCounts.TryGetValue(source, out ceded);
                if (WarPeaceTreatySurvivalRules.LeavesRequiredSourceAlive(
                        cityCount, ceded, true)) continue;
                reason = WarPeaceTreatySurvivalRules.FailureReason;
                return false;
            }
            reason = string.Empty;
            return true;
        }
    }

    public static class WarPeaceTermsRules
    {
        public const int MaximumWarScore = 100;
        public const int MaximumImmediatePaymentAmount = 1000;
        public const int MaximumReparationsAnnualAmount = 500;
        public const int MaximumReparationsDurationYears = 10;

        public static int MinimumTermCost(WarPeaceTermKind kind)
        {
            switch (kind)
            {
                case WarPeaceTermKind.WhitePeace:
                    return 0;
                case WarPeaceTermKind.GoldPayment:
                case WarPeaceTermKind.MaterialPayment:
                    return 5;
                case WarPeaceTermKind.ReleaseCaptives:
                    return 10;
                case WarPeaceTermKind.Reparations:
                case WarPeaceTermKind.RenounceClaims:
                    return 15;
                case WarPeaceTermKind.ForceTributary:
                    return 30;
                case WarPeaceTermKind.ForceVassal:
                    return 70;
                case WarPeaceTermKind.TakeMandate:
                    return WarGoalSettlementRules.TakeMandateRequiredScore;
                case WarPeaceTermKind.RestoreKingdom:
                    return WarGoalSettlementRules.RestoreKingdomRequiredScore;
                case WarPeaceTermKind.Independence:
                    return WarGoalSettlementRules.IndependenceRequiredScore;
                case WarPeaceTermKind.ReunifySuccession:
                    return WarGoalSettlementRules.
                        ReunifySuccessionRequiredScore;
                case WarPeaceTermKind.NoCbOutcome:
                    return WarGoalSettlementRules.NoCbOutcomeRequiredScore;
                default:
                    return 0;
            }
        }

        public static int NormalizeTermCost(WarPeaceTermKind kind,
            int requestedCost)
        {
            int minimum = MinimumTermCost(kind);
            return Math.Max(minimum, Math.Min(MaximumWarScore,
                Math.Max(0, requestedCost)));
        }

        public static int CanonicalTermCost(WarPeaceTermKind kind,
            int amount, int durationYears,
            WarPeaceCityValueFacts cityFacts)
        {
            switch (kind)
            {
                case WarPeaceTermKind.WhitePeace:
                    return 0;
                case WarPeaceTermKind.GoldPayment:
                case WarPeaceTermKind.MaterialPayment:
                    return Math.Min(25, 5 + Math.Max(0, amount) / 50);
                case WarPeaceTermKind.Reparations:
                    long burden = (long)Math.Max(0, amount) *
                                  Math.Max(0, durationYears);
                    return (int)Math.Min(MaximumWarScore,
                        MinimumTermCost(kind) + (burden + 9L) / 10L);
                case WarPeaceTermKind.CedeCity:
                    return CityCessionCost(cityFacts);
                default:
                    return MinimumTermCost(kind);
            }
        }

        public static bool TryReparationsSchedule(int currentYear,
            int durationYears, out int startYear, out int endYear)
        {
            startYear = -1;
            endYear = -1;
            if (currentYear < 0 || durationYears <= 0 ||
                durationYears > MaximumReparationsDurationYears)
                return false;
            long start = (long)currentYear + 1L;
            long end = start + durationYears - 1L;
            if (start > int.MaxValue || end > int.MaxValue) return false;
            startYear = (int)start;
            endYear = (int)end;
            return true;
        }

        public static int CityCessionCost(WarPeaceCityValueFacts facts)
        {
            float development = Clamp01(facts.Development);
            float population = Clamp01(Math.Max(0, facts.Population) / 180f);
            float zones = Clamp01(Math.Max(0, facts.ZoneCount) / 25f);
            float buildings = Clamp01(Math.Max(0, facts.BuildingCount) / 30f);
            float raw = 2f + development * 10f + population * 6f +
                        zones * 5f + buildings * 3f +
                        (facts.IsCapital ? 8f : 0f);
            if (!facts.DemandingSideHasCore && facts.OwnerHasCore)
                raw += 3f;
            if (facts.DemandingSideHasCore)
                raw = Math.Max(1f, raw * .30f);
            else if (facts.DemandingSideHasClaim)
                raw *= .65f;
            return Math.Max(1, Math.Min(45, (int)Math.Round(raw,
                MidpointRounding.AwayFromZero)));
        }

        public static bool CanDemandCity(int remainingCapacity,
            int cityCost, bool occupiedByDemandingSide,
            bool hasCoreOrClaim)
        {
            if (!occupiedByDemandingSide && !hasCoreOrClaim) return false;
            return cityCost > 0 &&
                   cityCost <= Math.Max(0, remainingCapacity);
        }

        public static WarPeaceAcceptanceResult EvaluateAcceptance(
            WarPeaceAcceptanceFacts facts)
        {
            if (facts.NetTermValueForRecipient < -MaximumWarScore ||
                facts.NetTermValueForRecipient > MaximumWarScore)
                return new WarPeaceAcceptanceResult(false, false,
                    int.MinValue);
            bool forced = facts.RecipientWarScore <= -MaximumWarScore &&
                          facts.NetTermValueForRecipient >= -MaximumWarScore;
            forced |= facts.CompleteSurrender &&
                      facts.RecipientWarScore >= MaximumWarScore &&
                      facts.NetTermValueForRecipient > 0;
            int margin = -facts.RecipientWarScore +
                         facts.NetTermValueForRecipient +
                         facts.RecipientWarExhaustion / 4 +
                         facts.RecipientMilitaryPressure / 4 -
                         facts.RecipientResolve / 4;
            return new WarPeaceAcceptanceResult(forced || margin >= 0,
                forced, margin);
        }

        internal static int ClampSignedWarScore(int value)
        {
            return Math.Max(-MaximumWarScore,
                Math.Min(MaximumWarScore, value));
        }

        internal static int ClampPercent(int value)
        {
            return Math.Max(0, Math.Min(100, value));
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
