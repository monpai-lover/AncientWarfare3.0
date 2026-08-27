using System;

namespace AncientWarfare3.core.court
{
    internal static class CountyCourtRules
    {
        public const int CountyMagistrateGrade = 30;
        public const int MinimumRank = 1;
        public const int DefaultTermYears = 5;

        internal static bool IsCountyMagistrate(string pLayer,
            string pOfficeId)
        {
            return string.Equals(pLayer, CourtOfficeLayer.County,
                       StringComparison.Ordinal) &&
                   string.Equals(pOfficeId, CourtOfficeId.CountyMagistrate,
                       StringComparison.Ordinal);
        }

        internal static int ResolveTermEndYear(int pStartYear,
            int pRequestedTermYears = DefaultTermYears)
        {
            return pStartYear + Math.Max(1, pRequestedTermYears);
        }
    }
}
