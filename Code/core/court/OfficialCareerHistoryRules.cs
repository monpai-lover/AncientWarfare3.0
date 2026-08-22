using System;
using System.Collections.Generic;

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

        public static bool IsTechnicalLeaderTransition(string pReason)
        {
            return pReason == "city_leader_mismatch" ||
                   pReason == "promoted_city_leader";
        }

        public static IReadOnlyList<OfficialCareerHistoryRow>
            CollapseTechnicalTransitions(
                IReadOnlyList<OfficialCareerHistoryRow> pRows)
        {
            var result = new List<OfficialCareerHistoryRow>();
            for (int i = 0; i < (pRows?.Count ?? 0); i++)
            {
                OfficialCareerHistoryRow older = pRows[i];
                if (result.Count == 0 ||
                    !CanMergeTechnical(result[result.Count - 1], older))
                {
                    result.Add(older);
                    continue;
                }
                OfficialCareerHistoryRow newer = result[result.Count - 1];
                result[result.Count - 1] = new OfficialCareerHistoryRow(
                    newer.KingdomId, newer.OfficerId, newer.ActorId,
                    newer.CityId, newer.Layer, newer.OfficeId, newer.ActorName,
                    Math.Min(newer.StartYear, older.StartYear), newer.EndYear,
                    newer.IsCurrent, newer.EndReason, newer.AppointedTime,
                    newer.KingdomName, newer.CityName, newer.RankId,
                    newer.Grade);
            }
            return result;
        }

        private static bool CanMergeTechnical(
            OfficialCareerHistoryRow pNewer, OfficialCareerHistoryRow pOlder)
        {
            if (pNewer == null || pOlder == null ||
                pNewer.KingdomId != pOlder.KingdomId ||
                pNewer.ActorId != pOlder.ActorId ||
                pNewer.CityId != pOlder.CityId ||
                pNewer.Layer != pOlder.Layer ||
                pNewer.OfficeId != pOlder.OfficeId)
                return false;
            bool technical = IsTechnicalLeaderTransition(pNewer.EndReason) ||
                             IsTechnicalLeaderTransition(pOlder.EndReason);
            if (!technical) return false;
            return pOlder.EndYear < 0 || pNewer.StartYear < 0 ||
                   pOlder.EndYear >= pNewer.StartYear;
        }
    }
}
