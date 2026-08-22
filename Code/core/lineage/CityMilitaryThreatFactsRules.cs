using System;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct CityMilitaryThreatKey :
        IEquatable<CityMilitaryThreatKey>
    {
        internal CityMilitaryThreatKey(long pWarId, long pCityId,
            long pKingdomId)
        {
            WarId = pWarId;
            CityId = pCityId;
            KingdomId = pKingdomId;
        }

        internal long WarId { get; }
        internal long CityId { get; }
        internal long KingdomId { get; }

        internal bool Matches(CityMilitaryThreatKey pOther)
        {
            return WarId == pOther.WarId && CityId == pOther.CityId &&
                   KingdomId == pOther.KingdomId;
        }

        public bool Equals(CityMilitaryThreatKey pOther)
        {
            return Matches(pOther);
        }

        public override bool Equals(object pObject)
        {
            return pObject is CityMilitaryThreatKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)WarId;
                hash = hash * 397 ^ (int)CityId;
                return hash * 397 ^ (int)KingdomId;
            }
        }
    }

    internal static class CityMilitaryThreatFactsRules
    {
        internal const double CacheLifetimeSeconds = 0.35d;

        internal static bool CanCache(long pWarId, long pCityId,
            long pKingdomId)
        {
            return pWarId >= 0L && pCityId >= 0L &&
                   pKingdomId >= 0L;
        }

        internal static bool CanCachePresence(long pWarId, long pCityId)
        {
            return pWarId >= 0L && pCityId >= 0L;
        }

        internal static bool ShouldReuse(long pCachedRevision,
            long pCurrentRevision, double pNow, double pCachedAt)
        {
            if (pCachedRevision != pCurrentRevision ||
                double.IsNaN(pNow) || double.IsInfinity(pNow) ||
                double.IsNaN(pCachedAt) || double.IsInfinity(pCachedAt) ||
                pNow < pCachedAt) return false;
            return pNow - pCachedAt < CacheLifetimeSeconds;
        }

        internal static bool ShouldReusePresence(long pCachedRevision,
            long pCurrentRevision, double pNow, double pCachedAt)
        {
            return ShouldReuse(pCachedRevision, pCurrentRevision, pNow,
                pCachedAt);
        }

        internal static bool KeyMatches(CityMilitaryThreatKey pLeft,
            CityMilitaryThreatKey pRight)
        {
            return pLeft.Equals(pRight);
        }

        internal static bool ShouldInvalidate(long pCachedCityId,
            long pChangedCityId)
        {
            return pCachedCityId >= 0L && pCachedCityId == pChangedCityId;
        }

        internal static bool ShouldAdvanceRevisionForInvalidation(
            long pChangedId)
        {
            return pChangedId >= 0L;
        }
    }
}
