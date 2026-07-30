using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CivilServiceWaitingPoolQuery
    {
        private const string AtHomeState = "AtHome";
        private const string ResidentState = "Resident";

        public static bool TryLoadActorIds(SQLiteConnection pDb,
            string pCandidateTable, string pSessionTable,
            string pArchiveTable, string pOfficerTable,
            string pAffiliationTable, long hostKingdomId,
            IReadOnlyList<long> hostCityIds, int pLimit,
            out IReadOnlyList<long> pActorIds)
        {
            var result = new List<long>();
            pActorIds = result;
            if (pDb == null || hostKingdomId < 0L || pLimit <= 0 ||
                !IsIdentifier(pCandidateTable) ||
                !IsIdentifier(pSessionTable) ||
                !IsIdentifier(pArchiveTable) ||
                !IsIdentifier(pOfficerTable) ||
                !IsIdentifier(pAffiliationTable))
                return false;

            try
            {
                using var command = new SQLiteCommand(pDb);
                var cityParameters = new List<string>();
                if (hostCityIds != null)
                {
                    for (int index = 0; index < hostCityIds.Count; index++)
                    {
                        string parameter = "@city" + index;
                        cityParameters.Add(parameter);
                        command.Parameters.AddWithValue(parameter,
                            hostCityIds[index]);
                    }
                }

                string residenceClause = cityParameters.Count == 0
                    ? "(A.KINGDOM_ID=@kingdom OR " +
                      "R.HOME_KINGDOM_ID=@kingdom)"
                    : "(A.KINGDOM_ID=@kingdom OR " +
                      "R.HOME_KINGDOM_ID=@kingdom OR (" +
                      "R.HOME_KINGDOM_ID<>@kingdom AND " +
                      "R.RESIDENCE_CITY_ID IN (" +
                      string.Join(",", cityParameters) + ") AND " +
                      "R.LIFECYCLE_STATE=@resident AND " +
                      "R.SERVICE_KINGDOM_ID<0))";

                command.CommandText =
                    "SELECT C.ACTOR_ID FROM " + pCandidateTable + " C JOIN " +
                    pSessionTable + " S ON S.ID=C.SESSION_ID JOIN " +
                    pArchiveTable + " A ON A.ID=C.ACTOR_ID LEFT JOIN " +
                    pAffiliationTable + " R ON R.ACTOR_ID=C.ACTOR_ID " +
                    "WHERE C.KINGDOM_ID=@kingdom AND " +
                    "S.KINGDOM_ID=@kingdom AND S.STATUS='completed' AND " +
                    "C.QUALIFICATION IN ('gongshi','jinshi') AND " +
                    "A.IS_ALIVE=1 AND A.SEX=0 AND " +
                    "IFNULL(A.STATUS,'')<>@slave AND " +
                    "NOT EXISTS (SELECT 1 FROM " + pCandidateTable +
                    " C2 JOIN " + pSessionTable +
                    " S2 ON S2.ID=C2.SESSION_ID WHERE " +
                    "C2.ACTOR_ID=C.ACTOR_ID AND " +
                    "C2.KINGDOM_ID=C.KINGDOM_ID AND " +
                    "S2.STATUS='completed' AND " +
                    "(S2.CYCLE_YEAR>S.CYCLE_YEAR OR " +
                    "(S2.CYCLE_YEAR=S.CYCLE_YEAR AND C2.ID>C.ID))) AND " +
                    "NOT EXISTS (SELECT 1 FROM " + pOfficerTable +
                    " O WHERE O.ACTOR_ID=C.ACTOR_ID AND O.ACTIVE=1) AND " +
                    "(R.ACTOR_ID IS NULL OR " +
                    "R.LIFECYCLE_STATE IN (@at_home,@resident)) AND " +
                    residenceClause + " GROUP BY C.ACTOR_ID " +
                    "ORDER BY MAX(S.CYCLE_YEAR) DESC,C.ACTOR_ID LIMIT @limit";
                command.Parameters.AddWithValue("@kingdom", hostKingdomId);
                command.Parameters.AddWithValue("@slave", LineageStatus.SLAVE);
                command.Parameters.AddWithValue("@at_home", AtHomeState);
                command.Parameters.AddWithValue("@resident", ResidentState);
                command.Parameters.AddWithValue("@limit", Math.Min(
                    CivilServiceExamRules.CandidateLimit, pLimit));
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    result.Add(Convert.ToInt64(reader.GetValue(0)));
                return true;
            }
            catch
            {
                result.Clear();
                return false;
            }
        }

        private static bool IsIdentifier(string pValue)
        {
            if (string.IsNullOrWhiteSpace(pValue)) return false;
            for (int index = 0; index < pValue.Length; index++)
            {
                char value = pValue[index];
                if (value == '_' || char.IsLetterOrDigit(value)) continue;
                return false;
            }
            return true;
        }
    }
}
