using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace AncientWarfare3.core.court
{
    internal static class CivilServiceForeignResidentQualificationQuery
    {
        public static List<long> Load(SQLiteConnection pDb,
            string pMembershipTable, string pArchiveTable,
            string pAffiliationTable, string pOfficerTable,
            string pCandidateTable, string pSessionTable,
            IReadOnlyList<long> pHostCityIds, long pHostKingdomId,
            int pYear, string pResidentState, int pLimit)
        {
            var result = new List<long>();
            if (pDb == null || pHostCityIds == null ||
                pHostCityIds.Count == 0 || pHostKingdomId < 0L ||
                pLimit <= 0 || string.IsNullOrEmpty(pMembershipTable) ||
                string.IsNullOrEmpty(pArchiveTable) ||
                string.IsNullOrEmpty(pAffiliationTable) ||
                string.IsNullOrEmpty(pOfficerTable) ||
                string.IsNullOrEmpty(pCandidateTable) ||
                string.IsNullOrEmpty(pSessionTable)) return result;

            try
            {
                using var command = new SQLiteCommand(pDb);
                var cityParameters = new List<string>(pHostCityIds.Count);
                for (int index = 0; index < pHostCityIds.Count; index++)
                {
                    string parameter = "@city" + index;
                    cityParameters.Add(parameter);
                    command.Parameters.AddWithValue(parameter,
                        pHostCityIds[index]);
                }
                command.CommandText =
                    "SELECT R.ACTOR_ID FROM " + pAffiliationTable + " R " +
                    "JOIN " + pMembershipTable + " M ON M.ACTOR_ID=R.ACTOR_ID " +
                    "JOIN " + pArchiveTable + " A ON A.ID=R.ACTOR_ID " +
                    "WHERE M.ACTIVE=1 AND M.START_YEAR<@year AND " +
                    "A.IS_ALIVE=1 AND R.RESIDENCE_CITY_ID IN (" +
                    string.Join(",", cityParameters) + ") AND " +
                    "R.LIFECYCLE_STATE=@resident AND " +
                    "R.SERVICE_KINGDOM_ID<0 AND " +
                    "R.HOME_KINGDOM_ID<>@kingdom AND NOT EXISTS " +
                    "(SELECT 1 FROM " + pOfficerTable + " O WHERE " +
                    "O.ACTOR_ID=R.ACTOR_ID AND O.ACTIVE=1) AND EXISTS " +
                    "(SELECT 1 FROM " + pCandidateTable + " Q JOIN " +
                    pSessionTable + " S ON S.ID=Q.SESSION_ID WHERE " +
                    "Q.ACTOR_ID=R.ACTOR_ID AND Q.KINGDOM_ID=@kingdom AND " +
                    "Q.QUALIFICATION IN ('gongshi','jinshi') " +
                    "AND S.STATUS='completed') " +
                    "GROUP BY R.ACTOR_ID ORDER BY MIN(M.START_YEAR)," +
                    "MAX(M.REPUTATION) DESC,R.ACTOR_ID LIMIT @limit";
                command.Parameters.AddWithValue("@year", pYear);
                command.Parameters.AddWithValue("@resident",
                    pResidentState ?? "");
                command.Parameters.AddWithValue("@kingdom", pHostKingdomId);
                command.Parameters.AddWithValue("@limit", pLimit);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read() && result.Count < pLimit)
                    result.Add(Convert.ToInt64(reader.GetValue(0)));
            }
            catch
            {
                result.Clear();
            }
            return result;
        }
    }
}
