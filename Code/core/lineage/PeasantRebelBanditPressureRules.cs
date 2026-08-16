using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct BanditPressureTargetCandidate
    {
        public BanditPressureTargetCandidate(long cityId, int loyalty,
            bool adjacent, bool ownedByOrigin, bool live)
        {
            CityId = cityId;
            Loyalty = loyalty;
            Adjacent = adjacent;
            OwnedByOrigin = ownedByOrigin;
            Live = live;
        }

        public long CityId { get; }
        public int Loyalty { get; }
        public bool Adjacent { get; }
        public bool OwnedByOrigin { get; }
        public bool Live { get; }
    }

    public static class PeasantRebelBanditPressureRules
    {
        public const int AnnualPressure = 6;
        public const int MaximumPressure = 300;
        public const int ActiveTargetLoyaltyPenalty = -25;

        public static int AdvancePressure(int pressure, int lastYear,
            int currentYear)
        {
            int years = Math.Max(0, currentYear - lastYear);
            long next = Math.Max(0, pressure) +
                        (long)years * AnnualPressure;
            return (int)Math.Min(MaximumPressure, next);
        }

        public static int LoyaltyPenalty(bool active, bool targetMatches)
        {
            return active && targetMatches
                ? ActiveTargetLoyaltyPenalty
                : 0;
        }

        public static bool ShouldStartRevolution(int banditStrength,
            int originStrength, bool originViable)
        {
            if (!originViable) return true;
            return (long)Math.Max(0, banditStrength) * 2L >=
                   Math.Max(0, originStrength);
        }

        public static long SelectTargetCityId(
            IEnumerable<BanditPressureTargetCandidate> candidates)
        {
            if (candidates == null) return -1L;
            long selectedId = -1L;
            int selectedLoyalty = int.MaxValue;
            foreach (BanditPressureTargetCandidate candidate in candidates)
            {
                if (!candidate.Live || !candidate.Adjacent ||
                    !candidate.OwnedByOrigin || candidate.CityId <= 0)
                    continue;
                if (candidate.Loyalty > selectedLoyalty ||
                    candidate.Loyalty == selectedLoyalty &&
                    selectedId > 0 && candidate.CityId >= selectedId)
                    continue;
                selectedId = candidate.CityId;
                selectedLoyalty = candidate.Loyalty;
            }
            return selectedId;
        }

        public static bool ShouldQueueOrphanCleanup(int liveCityCount)
        {
            return liveCityCount <= 0;
        }
    }
}
