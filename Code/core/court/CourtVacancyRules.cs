using System;

namespace AncientWarfare3.core.court
{
    internal enum CourtVacancyPriority : byte
    {
        Central = 0,
        LocalChief = 1,
        LocalOffice = 2
    }

    internal enum CourtVacancyOutcome : byte
    {
        Filled = 0,
        NoCandidate = 1,
        TechnicalFailure = 2,
        Invalid = 3
    }

    internal readonly struct CourtVacancyKey : IEquatable<CourtVacancyKey>
    {
        internal CourtVacancyKey(long pKingdomId, long pCityId,
            long pCountyId, string pLayer, string pOfficeId,
            bool pIsLocalChief = false)
        {
            KingdomId = pKingdomId;
            CityId = pCityId;
            CountyId = pCountyId;
            Layer = pLayer ?? string.Empty;
            OfficeId = pOfficeId ?? string.Empty;
            IsLocalChief = pIsLocalChief;
        }

        internal long KingdomId { get; }
        internal long CityId { get; }
        internal long CountyId { get; }
        internal string Layer { get; }
        internal string OfficeId { get; }
        internal bool IsLocalChief { get; }

        public bool Equals(CourtVacancyKey pOther)
        {
            return KingdomId == pOther.KingdomId &&
                   CityId == pOther.CityId &&
                   CountyId == pOther.CountyId &&
                   string.Equals(Layer, pOther.Layer,
                       StringComparison.Ordinal) &&
                   string.Equals(OfficeId, pOther.OfficeId,
                       StringComparison.Ordinal);
        }

        public override bool Equals(object pObject)
        {
            return pObject is CourtVacancyKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = KingdomId.GetHashCode();
                hash = (hash * 397) ^ CityId.GetHashCode();
                hash = (hash * 397) ^ CountyId.GetHashCode();
                hash = (hash * 397) ^
                    StringComparer.Ordinal.GetHashCode(Layer);
                hash = (hash * 397) ^
                    StringComparer.Ordinal.GetHashCode(OfficeId);
                return hash;
            }
        }
    }

    internal static class CourtVacancyRules
    {
        internal static CourtVacancyPriority Priority(CourtVacancyKey pKey)
        {
            if (pKey.Layer == CourtOfficeLayer.Central ||
                pKey.Layer == CourtOfficeLayer.Military)
                return CourtVacancyPriority.Central;
            return pKey.IsLocalChief
                ? CourtVacancyPriority.LocalChief
                : CourtVacancyPriority.LocalOffice;
        }

        internal static int MissingSeats(int pDesired, int pOccupied)
        {
            return Math.Max(0, pDesired - pOccupied);
        }

        internal static int CascadeLimit(int pValidOfficeCount)
        {
            return Math.Max(0, pValidOfficeCount);
        }

        internal static bool ShouldRetry(CourtVacancyOutcome pOutcome,
            int pAttempt)
        {
            return pOutcome == CourtVacancyOutcome.TechnicalFailure &&
                   pAttempt == 0;
        }
    }
}
