using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    public readonly struct MandateDeclineRebellionFacts
    {
        public MandateDeclineRebellionFacts(MandatePhase phase,
            int currentYear, int lastRebellionYear, long kingdomId,
            int mandateValue, int authority, int catalystScore,
            int cityCount, int activeRebellions)
        {
            Phase = phase;
            CurrentYear = currentYear;
            LastRebellionYear = lastRebellionYear;
            KingdomId = kingdomId;
            MandateValue = mandateValue;
            Authority = authority;
            CatalystScore = catalystScore;
            CityCount = cityCount;
            ActiveRebellions = activeRebellions;
        }

        public MandatePhase Phase { get; }
        public int CurrentYear { get; }
        public int LastRebellionYear { get; }
        public long KingdomId { get; }
        public int MandateValue { get; }
        public int Authority { get; }
        public int CatalystScore { get; }
        public int CityCount { get; }
        public int ActiveRebellions { get; }
    }

    public readonly struct MandateDeclineCityFacts
    {
        public MandateDeclineCityFacts(bool alive, bool ownedByMandate,
            bool capital, bool capitalRing, bool feudatory, int population,
            bool founderEligible)
        {
            Alive = alive;
            OwnedByMandate = ownedByMandate;
            Capital = capital;
            CapitalRing = capitalRing;
            Feudatory = feudatory;
            Population = population;
            FounderEligible = founderEligible;
        }

        public bool Alive { get; }
        public bool OwnedByMandate { get; }
        public bool Capital { get; }
        public bool CapitalRing { get; }
        public bool Feudatory { get; }
        public int Population { get; }
        public bool FounderEligible { get; }
    }

    public static class MandateDeclineRebellionRules
    {
        public const int SuccessfulRebellionCooldownYears = 4;
        public const int MaximumActiveRebellions = 2;
        public const int CityScanBudget = 4;
        public const int MinimumCityCount = 4;
        public const int MinimumCityPopulation = 25;
        public const int SuccessCatalystPressure = 6;

        public static bool ShouldAttempt(MandateDeclineRebellionFacts pFacts,
            int forcedRoll = -1)
        {
            if (pFacts.Phase != MandatePhase.Decline ||
                pFacts.MandateValue <= 0 ||
                pFacts.CityCount < MinimumCityCount ||
                pFacts.ActiveRebellions >= MaximumActiveRebellions)
                return false;
            if (pFacts.LastRebellionYear >= 0 &&
                pFacts.CurrentYear - pFacts.LastRebellionYear <
                SuccessfulRebellionCooldownYears) return false;
            int roll = forcedRoll >= 0
                ? NormalizePercentage(forcedRoll)
                : DeterministicRoll(pFacts.KingdomId, pFacts.CurrentYear);
            return roll < AttemptChance(pFacts.MandateValue,
                pFacts.Authority, pFacts.CatalystScore);
        }

        public static int AttemptChance(int pMandateValue, int pAuthority,
            int pCatalystScore)
        {
            int pressure = Math.Max(0, 50 - pMandateValue) +
                           Math.Max(0, 50 - pAuthority) +
                           Math.Max(0, pCatalystScore) / 2;
            return Math.Max(15, Math.Min(55, 15 + pressure / 2));
        }

        public static bool IsEligibleCity(MandateDeclineCityFacts pFacts)
        {
            return pFacts.Alive && pFacts.OwnedByMandate &&
                   !pFacts.Capital && !pFacts.CapitalRing &&
                   !pFacts.Feudatory &&
                   pFacts.Population >= MinimumCityPopulation &&
                   pFacts.FounderEligible;
        }

        public static int NextCityCursor(int currentCursor,
            int inspectedCount, int cityCount)
        {
            if (cityCount <= 0) return 0;
            int cursor = Math.Max(0, currentCursor) % cityCount;
            return (cursor + Math.Max(0, inspectedCount)) % cityCount;
        }

        public static string EncodeRoster(IEnumerable<long> pKingdomIds)
        {
            if (pKingdomIds == null) return "";
            return string.Join(",", pKingdomIds
                .Where(p => p >= 0)
                .Distinct()
                .OrderBy(p => p)
                .Take(MaximumActiveRebellions));
        }

        public static IReadOnlyList<long> DecodeRoster(string pRaw)
        {
            if (string.IsNullOrWhiteSpace(pRaw)) return Array.Empty<long>();
            var result = new SortedSet<long>();
            foreach (string part in pRaw.Split(','))
            {
                if (!long.TryParse(part, out long id) || id < 0) continue;
                result.Add(id);
                if (result.Count >= MaximumActiveRebellions) break;
            }
            return result.ToArray();
        }

        public static int DeterministicRoll(long pKingdomId, int pYear)
        {
            unchecked
            {
                ulong value = (ulong)pKingdomId;
                value ^= (ulong)(uint)pYear * 0x9E3779B185EBCA87UL;
                value ^= value >> 33;
                value *= 0xC2B2AE3D27D4EB4FUL;
                value ^= value >> 29;
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
