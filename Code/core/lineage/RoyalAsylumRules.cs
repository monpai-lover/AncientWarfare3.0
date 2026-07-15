using System;

namespace AncientWarfare3.core.lineage
{
    public static class RoyalAsylumRules
    {
        public static bool IsProtectedFamilyCandidate(bool homeAlive, bool monarchy,
            bool actorAlive, bool actorBelongsToHome, bool actorIsSlave,
            bool actorIsForeignKing, bool actorIsKing, bool actorIsCurrentHeir,
            bool isKingsDirectChild, bool isHeirsDirectChild)
        {
            return homeAlive && monarchy && actorAlive && actorBelongsToHome &&
                   !actorIsSlave && !actorIsForeignKing && !actorIsKing &&
                   !actorIsCurrentHeir &&
                   (isKingsDirectChild || isHeirsDirectChild);
        }

        public static bool IsHostEligible(bool hostAlive, bool hostCivilization,
            bool hostIsForeign, bool hostIsNeutral, bool hostIsWild,
            bool hostHasLivingCity, bool hostAtWar, bool hostIsEnemy)
        {
            return hostAlive && hostCivilization && hostIsForeign &&
                   !hostIsNeutral && !hostIsWild && hostHasLivingCity &&
                   !hostAtWar && !hostIsEnemy;
        }

        public static bool ShouldEvacuate(bool homeAlive, bool monarchy,
            bool hasDefensiveWar, bool hostAvailable)
        {
            return homeAlive && monarchy && hasDefensiveWar && hostAvailable;
        }

        public static bool ShouldReturn(bool homeRealmAlive, bool hasDefensiveWar)
        {
            return homeRealmAlive && !hasDefensiveWar;
        }

        public static bool ShouldNaturalize(bool homeRealmAlive, bool hostCityValid)
        {
            return !homeRealmAlive && hostCityValid;
        }

        public static bool ShouldRelocate(bool asylumActive, bool hostKingdomAlive,
            bool hostCityAlive, bool hostCityStillOwned, bool hostAtWar)
        {
            return asylumActive && (!hostKingdomAlive || !hostCityAlive ||
                                    !hostCityStillOwned || hostAtWar);
        }

        public static bool CanPerformProtectedRole(bool asylumActive)
        {
            return !asylumActive;
        }
    }

    public readonly struct RoyalAsylumHostRank : IComparable<RoyalAsylumHostRank>
    {
        public RoyalAsylumHostRank(bool sameIsland, long distanceSquared,
            long kingdomId, long cityId)
        {
            SameIsland = sameIsland;
            DistanceSquared = Math.Max(0L, distanceSquared);
            KingdomId = kingdomId;
            CityId = cityId;
        }

        public bool SameIsland { get; }
        public long DistanceSquared { get; }
        public long KingdomId { get; }
        public long CityId { get; }

        public int CompareTo(RoyalAsylumHostRank other)
        {
            if (SameIsland != other.SameIsland) return SameIsland ? -1 : 1;
            int result = DistanceSquared.CompareTo(other.DistanceSquared);
            if (result != 0) return result;
            result = KingdomId.CompareTo(other.KingdomId);
            return result != 0 ? result : CityId.CompareTo(other.CityId);
        }
    }
}
