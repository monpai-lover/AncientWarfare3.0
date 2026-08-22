using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class LegacyProjectedOfficeRepairService
    {
        internal static bool Repair(SQLiteConnection pDb)
        {
            if (pDb == null) return false;
            var affected = new List<Tuple<long, long, string>>();
            try
            {
                string table = CourtOfficerTableItem.GetTableName();
                using (var read = new SQLiteCommand(
                    "SELECT KINGDOM_ID,ACTOR_ID,OFFICE_ID FROM " + table +
                    " WHERE ACTIVE=1 AND (OFFICE_ID LIKE 'regional-chief:%' " +
                    "OR OFFICE_ID LIKE 'commandery-chief:%')", pDb))
                using (SQLiteDataReader rows = read.ExecuteReader())
                {
                    while (rows.Read())
                        affected.Add(Tuple.Create(Convert.ToInt64(rows[0]),
                            Convert.ToInt64(rows[1]),
                            Convert.ToString(rows[2]) ?? ""));
                }
                using (SQLiteTransaction tx = pDb.BeginTransaction())
                using (var update = new SQLiteCommand(pDb))
                {
                    update.Transaction = tx;
                    update.CommandText = "UPDATE " + table +
                        " SET ACTIVE=0,ENDED_YEAR=@year,ENDED_TIME=@time," +
                        "END_REASON='legacy_projection_identity',UPDATED_TIME=@time " +
                        "WHERE ACTIVE=1 AND (OFFICE_ID LIKE 'regional-chief:%' " +
                        "OR OFFICE_ID LIKE 'commandery-chief:%')";
                    update.Parameters.AddWithValue("@year", Date.getCurrentYear());
                    update.Parameters.AddWithValue("@time", LineageService.CurTime());
                    update.ExecuteNonQuery();
                    tx.Commit();
                }
                foreach (Tuple<long, long, string> item in affected)
                {
                    Actor actor = World.world?.units?.get(item.Item2);
                    OfficialCareerStateService.ClearCurrentOffice(actor,
                        item.Item1, item.Item3);
                }
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Legacy projected office repair failed: " +
                                    e.Message);
                return false;
            }
        }
    }
}
