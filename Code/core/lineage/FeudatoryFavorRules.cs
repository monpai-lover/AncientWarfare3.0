using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum FeudatoryFavorAction
    {
        None = 0,
        ReclaimCity = 1,
        ReduceAutonomy = 2
    }

    public readonly struct FeudatoryFavorCityCandidate
    {
        public FeudatoryFavorCityCandidate(long cityId,
            int distanceToCapital)
        {
            CityId = cityId;
            DistanceToCapital = Math.Max(0, distanceToCapital);
        }

        public long CityId { get; }
        public int DistanceToCapital { get; }
    }

    public static class FeudatoryFavorRules
    {
        public const int AutonomyReduction = 15;

        public static bool CanEnable(bool isMandateKingdom,
            bool alreadyEnabled, bool canRaiseCentralization,
            int activeFeudatoryCount)
        {
            return isMandateKingdom && !alreadyEnabled &&
                   canRaiseCentralization && activeFeudatoryCount > 0;
        }

        public static FeudatoryFavorAction ResolveSuccessionEffect(
            bool enabled, int cityCount)
        {
            if (!enabled || cityCount <= 0) return FeudatoryFavorAction.None;
            return cityCount > 1
                ? FeudatoryFavorAction.ReclaimCity
                : FeudatoryFavorAction.ReduceAutonomy;
        }

        public static long SelectReclaimedCity(long seatCityId,
            IReadOnlyList<FeudatoryFavorCityCandidate> candidates)
        {
            long selectedId = -1L;
            int selectedDistance = int.MaxValue;
            int count = candidates?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                FeudatoryFavorCityCandidate candidate = candidates[i];
                if (candidate.CityId < 0 || candidate.CityId == seatCityId)
                    continue;
                if (candidate.DistanceToCapital > selectedDistance) continue;
                if (candidate.DistanceToCapital == selectedDistance &&
                    selectedId >= 0 && candidate.CityId >= selectedId)
                    continue;
                selectedId = candidate.CityId;
                selectedDistance = candidate.DistanceToCapital;
            }
            return selectedId;
        }

        public static int ReduceAutonomy(int autonomy)
        {
            return Math.Max(0, Math.Min(100, autonomy) - AutonomyReduction);
        }
    }
}
