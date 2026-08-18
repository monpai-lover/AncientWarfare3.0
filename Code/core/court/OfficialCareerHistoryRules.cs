using System;

namespace AncientWarfare3.core.court
{
    public static class OfficialCareerHistoryRules
    {
        public static string YearRange(OfficialCareerHistoryRow pRow,
            string pCurrentLabel, string pUnknownLabel)
        {
            string unknown = string.IsNullOrWhiteSpace(pUnknownLabel)
                ? "?"
                : pUnknownLabel;
            if (pRow == null) return unknown + "—" + unknown;
            string start = pRow.StartYear >= 0
                ? pRow.StartYear.ToString()
                : unknown;
            string end = pRow.IsCurrent
                ? (string.IsNullOrWhiteSpace(pCurrentLabel)
                    ? unknown
                    : pCurrentLabel)
                : pRow.EndYear >= 0
                    ? pRow.EndYear.ToString()
                    : unknown;
            return start + "—" + end;
        }

        public static bool IsNewer(OfficialCareerHistoryRow pCandidate,
            OfficialCareerHistoryRow pCurrent)
        {
            if (pCandidate == null) return false;
            if (pCurrent == null) return true;
            int start = pCandidate.StartYear.CompareTo(pCurrent.StartYear);
            if (start != 0) return start > 0;
            int time = pCandidate.AppointedTime.CompareTo(
                pCurrent.AppointedTime);
            if (time != 0) return time > 0;
            int identity = pCandidate.OfficerId.CompareTo(pCurrent.OfficerId);
            if (identity != 0) return identity > 0;
            return pCurrent.IsCurrent && !pCandidate.IsCurrent;
        }
    }
}
