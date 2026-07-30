using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolEducationCandidateQuery
    {
        public static List<long> LoadDeclinedNobles(SQLiteConnection pDb,
            string pArchiveTable, long pKingdomId, long pAfterActorId,
            int pLimit)
        {
            var result = new List<long>();
            if (pDb == null || string.IsNullOrWhiteSpace(pArchiveTable) ||
                pKingdomId < 0L || pLimit <= 0) return result;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = "SELECT ID FROM " + pArchiveTable +
                    " WHERE KINGDOM_ID=@kingdom AND IS_ALIVE=1 " +
                    "AND IFNULL(STATUS,'')<>'noble' AND " +
                    "(EVER_NOBLE_BLOOD=1 OR LINEAGE_ID>=0) " +
                    "AND ID>@cursor ORDER BY ID LIMIT @limit";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@cursor", pAfterActorId);
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
