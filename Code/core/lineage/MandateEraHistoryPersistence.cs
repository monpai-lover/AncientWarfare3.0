using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    public sealed class MandateEraHistoryRecord
    {
        public long EraId = -1;
        public long ReignId = -1;
        public string EraName = "";
        public string EraColor = "";
        public double StartTime = -1d;
        public double EndTime = -1d;
    }

    public static class MandateEraHistoryPersistence
    {
        private const int MaximumReadLimit = 512;

        public static IReadOnlyList<MandateEraHistoryRecord> Read(
            SQLiteConnection pDb, long pKingdomId, double pPeriodStart,
            double pPeriodEnd, int pLimit = MaximumReadLimit)
        {
            if (pDb == null ||
                pDb.State != System.Data.ConnectionState.Open ||
                pKingdomId < 0)
                return Array.Empty<MandateEraHistoryRecord>();

            int limit = Math.Max(1, Math.Min(MaximumReadLimit, pLimit));
            var result = new List<MandateEraHistoryRecord>(
                Math.Min(limit, 32));
            using var command = new SQLiteCommand(pDb);
            command.CommandText =
                "SELECT ERA_ID,REIGN_ID,IFNULL(ERA_STEM,'')," +
                "IFNULL(ERA_COLOR,''),START_TIME,END_TIME FROM EraPeriod " +
                "WHERE KINGDOM_ID=@kingdom AND " +
                "(@periodEnd<0 OR START_TIME<@periodEnd) AND " +
                "(@periodStart<0 OR END_TIME<0 OR END_TIME>@periodStart) " +
                "ORDER BY START_TIME ASC,ERA_ID ASC LIMIT @limit";
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            command.Parameters.AddWithValue("@periodStart", pPeriodStart);
            command.Parameters.AddWithValue("@periodEnd", pPeriodEnd);
            command.Parameters.AddWithValue("@limit", limit);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new MandateEraHistoryRecord
                {
                    EraId = reader.IsDBNull(0) ? -1L : reader.GetInt64(0),
                    ReignId = reader.IsDBNull(1) ? -1L : reader.GetInt64(1),
                    EraName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    EraColor = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    StartTime = reader.IsDBNull(4) ? -1d :
                        Convert.ToDouble(reader.GetValue(4)),
                    EndTime = reader.IsDBNull(5) ? -1d :
                        Convert.ToDouble(reader.GetValue(5))
                });
            }
            return result;
        }
    }
}
