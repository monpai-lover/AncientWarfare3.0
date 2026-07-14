using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    public enum HistoricalSchoolVenueSourceKind
    {
        None,
        Academy,
        PublicCity,
        Local
    }

    public static class HistoricalSchoolVenueRules
    {
        public const int IdleRoamMinDistanceSquared = 6 * 6;
        public const int IdleRoamMaxDistanceSquared = 18 * 18;

        public static HistoricalSchoolVenueSourceKind SelectSource(
            bool pAcademyAvailable,
            bool pPublicAvailable,
            bool pLocalAvailable)
        {
            if (pAcademyAvailable) return HistoricalSchoolVenueSourceKind.Academy;
            if (pPublicAvailable) return HistoricalSchoolVenueSourceKind.PublicCity;
            return pLocalAvailable
                ? HistoricalSchoolVenueSourceKind.Local
                : HistoricalSchoolVenueSourceKind.None;
        }

        public static bool IsPublicCandidate(
            bool pInsideCity,
            bool pWalkable,
            bool pCityCenter)
        {
            return pInsideCity && pWalkable && !pCityCenter;
        }

        public static bool IsIdleRoamCandidate(
            bool pInsideResidenceCity,
            bool pWalkable,
            bool pCityCenter,
            bool pBorderZone,
            int pDistanceSquared)
        {
            return pInsideResidenceCity && pWalkable && !pCityCenter && !pBorderZone &&
                   pDistanceSquared >= IdleRoamMinDistanceSquared &&
                   pDistanceSquared <= IdleRoamMaxDistanceSquared;
        }

        public static bool TrySelect(long pStableKey, int pCandidateCount,
            ISet<int> pOccupied, out int pIndex)
        {
            pIndex = -1;
            if (pCandidateCount <= 0) return false;
            int start = StableIndex(pStableKey, pCandidateCount);
            for (int offset = 0; offset < pCandidateCount; offset++)
            {
                int index = (start + offset) % pCandidateCount;
                if (pOccupied != null && pOccupied.Contains(index)) continue;
                pIndex = index;
                return true;
            }
            return false;
        }

        private static int StableIndex(long pStableKey, int pCount)
        {
            unchecked
            {
                long mixed = (pStableKey ^ (pStableKey >> 32)) * 1103515245L + 12345L;
                return (int)(Math.Abs(mixed % pCount));
            }
        }
    }
}
