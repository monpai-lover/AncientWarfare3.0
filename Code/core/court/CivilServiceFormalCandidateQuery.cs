using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CivilServiceFormalCandidateQuery
    {
        public static List<long> Load(SQLiteConnection pDb,
            string pCandidateTable, string pSessionTable,
            string pArchiveTable, string pOfficerTable, long pKingdomId,
            int pLimit)
        {
            var result = new List<long>();
            if (pDb == null || pKingdomId < 0L || pLimit <= 0 ||
                string.IsNullOrWhiteSpace(pCandidateTable) ||
                string.IsNullOrWhiteSpace(pSessionTable) ||
                string.IsNullOrWhiteSpace(pArchiveTable) ||
                string.IsNullOrWhiteSpace(pOfficerTable)) return result;

            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText =
                    "SELECT C.ACTOR_ID FROM " + pCandidateTable + " C JOIN " +
                    pSessionTable + " S ON S.ID=C.SESSION_ID JOIN " +
                    pArchiveTable + " A ON A.ID=C.ACTOR_ID " +
                    "WHERE S.KINGDOM_ID=@kingdom AND C.KINGDOM_ID=@kingdom " +
                    "AND C.QUALIFICATION IN ('gongshi','jinshi') " +
                    "AND S.STATUS='completed' " +
                    "AND A.IS_ALIVE=1 AND A.SEX=0 " +
                    "AND IFNULL(A.STATUS,'')<>@slave " +
                    "AND A.KINGDOM_ID=@kingdom AND NOT EXISTS (SELECT 1 FROM " +
                    pOfficerTable + " O WHERE O.ACTOR_ID=C.ACTOR_ID " +
                    "AND O.ACTIVE=1 AND O.LAYER='central') " +
                    "AND NOT EXISTS (SELECT 1 FROM " + pCandidateTable +
                    " C2 JOIN " + pSessionTable +
                    " S2 ON S2.ID=C2.SESSION_ID " +
                    "WHERE C2.ACTOR_ID=C.ACTOR_ID " +
                    "AND C2.KINGDOM_ID=C.KINGDOM_ID " +
                    "AND C2.QUALIFICATION IN ('gongshi','jinshi') " +
                    "AND S2.STATUS='completed' " +
                    "AND (S2.CYCLE_YEAR>S.CYCLE_YEAR OR " +
                    "(S2.CYCLE_YEAR=S.CYCLE_YEAR AND C2.ID>C.ID))) " +
                    "ORDER BY S.CYCLE_YEAR DESC,C.ENTRY_BONUS DESC,C.ID DESC " +
                    "LIMIT @limit";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@slave",
                    LineageStatus.SLAVE);
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
