using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    public enum HistoricalSchoolVenueKind
    {
        Lecture,
        Debate,
        TravelArrival,
        IdleRoam
    }

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
        public const int IdleRoamProbeBudget = 64;

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

        public static bool RequiresAcademy(HistoricalSchoolVenueKind pKind)
        {
            return pKind == HistoricalSchoolVenueKind.Lecture ||
                   pKind == HistoricalSchoolVenueKind.Debate;
        }

        public static bool IsAcademyUsable(
            bool buildingExists,
            bool buildingUsable,
            bool underConstruction,
            bool attachedToCity,
            bool belongsToRequestedCity)
        {
            return buildingExists && buildingUsable && !underConstruction &&
                   attachedToCity && belongsToRequestedCity;
        }

        public static bool IsAttachedToRequestedCity(
            bool directAttachment, bool tileAttachment)
        {
            return directAttachment || tileAttachment;
        }

        public static bool IsDebateLayoutValid(
            bool hasAcademy,
            bool primaryPresent,
            bool secondaryPresent,
            bool sameTile)
        {
            return primaryPresent && secondaryPresent &&
                   (hasAcademy ? sameTile : !sameTile);
        }

        public static bool CanReserveAcademy(
            bool academyUsable,
            bool occupiedByAnotherActivity)
        {
            return academyUsable && !occupiedByAnotherActivity;
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

        public static int IdleRoamProbeCount(int pCandidateCount)
        {
            return Math.Max(0, Math.Min(IdleRoamProbeBudget,
                pCandidateCount));
        }

        public static int IdleRoamProbeIndex(long pStableKey, int pProbe,
            int pCandidateCount)
        {
            int probeCount = IdleRoamProbeCount(pCandidateCount);
            if (pProbe < 0 || pProbe >= probeCount) return -1;
            int start = StableIndex(pStableKey, pCandidateCount);
            int stride = CoprimeProbeStride(pCandidateCount);
            return (int)((start + (long)pProbe * stride) %
                         pCandidateCount);
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

        private static int CoprimeProbeStride(int pCount)
        {
            if (pCount <= 1) return 1;
            int stride = Math.Min(31, pCount - 1);
            while (stride > 1 && GreatestCommonDivisor(stride, pCount) != 1)
                stride--;
            return Math.Max(1, stride);
        }

        private static int GreatestCommonDivisor(int pFirst, int pSecond)
        {
            int first = Math.Abs(pFirst);
            int second = Math.Abs(pSecond);
            while (second != 0)
            {
                int remainder = first % second;
                first = second;
                second = remainder;
            }
            return first;
        }
    }
}
