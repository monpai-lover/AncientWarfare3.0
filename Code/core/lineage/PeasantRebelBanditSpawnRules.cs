using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct BanditLoyaltyCityCandidate
    {
        internal BanditLoyaltyCityCandidate(long pCityId, int pLoyalty,
            bool pLive)
        {
            CityId = pCityId;
            Loyalty = pLoyalty;
            Live = pLive;
        }

        internal long CityId { get; }
        internal int Loyalty { get; }
        internal bool Live { get; }
    }

    internal static class PeasantRebelBanditSpawnRules
    {
        internal const int LoyaltyThreshold = -50;
        internal const int SuppressionCooldownYears = 50;

        internal static bool IsEligibleKingdom(bool pCivilization,
            bool pBandit, bool pNeutral, bool pRekt)
        {
            return pCivilization && !pBandit && !pNeutral && !pRekt;
        }

        internal static long SelectCandidateCityId(
            IEnumerable<BanditLoyaltyCityCandidate> pCandidates)
        {
            long selected = -1L;
            int selectedLoyalty = int.MaxValue;
            if (pCandidates == null) return selected;
            foreach (BanditLoyaltyCityCandidate candidate in pCandidates)
            {
                if (!candidate.Live || candidate.CityId <= 0 ||
                    candidate.Loyalty >= LoyaltyThreshold) continue;
                if (candidate.Loyalty > selectedLoyalty ||
                    candidate.Loyalty == selectedLoyalty && selected > 0 &&
                    candidate.CityId >= selected) continue;
                selected = candidate.CityId;
                selectedLoyalty = candidate.Loyalty;
            }
            return selected;
        }

        internal static bool CanCreateInCity(int pCurrentYear,
            int pSuppressionUntilYear, bool pManualBypass)
        {
            return pManualBypass || pSuppressionUntilYear <= pCurrentYear;
        }

        internal static int ResolveSuppressionExpiryYear(int pCurrentYear,
            bool pSuppressionCompleted)
        {
            if (!pSuppressionCompleted) return int.MinValue;
            return pCurrentYear > int.MaxValue - SuppressionCooldownYears
                ? int.MaxValue
                : pCurrentYear + SuppressionCooldownYears;
        }
    }
}
