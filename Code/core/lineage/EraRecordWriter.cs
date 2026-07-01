using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     纪元/年号写入。YearNameService.OnNewKing 末尾调此，关旧开新。
    /// </summary>
    internal static class EraRecordWriter
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;
        private static string TABLE => EraPeriodTableItem.GetTableName();

        public static void OnEraChanged(Kingdom pKingdom, string pNewStem)
        {
            if (!Ready || pKingdom?.data == null) return;
            CloseOpenEra(pKingdom.id);
            if (string.IsNullOrEmpty(pNewStem)) return;

            long eraId = TableIdAllocator.Next(DB, TABLE, "ERA_ID");
            double now = World.world.getCurWorldTime();
            int year = Date.getYear(now);
            string color = HistoryColors.FromKingdom(pKingdom);
            try
            {
                DB.Insert(TABLE,
                    ColumnVal.Create("ERA_ID",     eraId),
                    ColumnVal.Create("KINGDOM_ID", pKingdom.id),
                    ColumnVal.Create("KINGDOM_COLOR", color),
                    ColumnVal.Create("ERA_STEM",   pNewStem),
                    ColumnVal.Create("ERA_COLOR",  color),
                    ColumnVal.Create("START_TIME", now),
                    ColumnVal.Create("END_TIME",   -1.0),
                    ColumnVal.Create("START_YEAR", year));
                HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.ERA_CHANGE,
                    HistoryText.PlainText("改元 ") + HistoryText.Colored(pNewStem, color));
            }
            catch (Exception e) { ModClass.LogWarning("EraRecordWriter.OnEraChanged: " + e.Message); }
        }

        public static void CloseOpenEra(long pKingdomId)
        {
            if (!Ready) return;
            try
            {
                using var findCmd = new SQLiteCommand(DB);
                findCmd.CommandText = $"SELECT ERA_ID FROM {TABLE} " +
                                      $"WHERE KINGDOM_ID=@kid AND END_TIME=-1 ORDER BY START_TIME DESC LIMIT 1";
                findCmd.Parameters.AddWithValue("@kid", pKingdomId);
                object v = findCmd.ExecuteScalar();
                if (v == null || v == DBNull.Value) return;
                long openId = Convert.ToInt64(v);
                DB.UpdateValue(TABLE,
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("ERA_ID", openId) },
                    ColumnVal.Create("END_TIME", World.world.getCurWorldTime()));
            }
            catch (Exception e) { ModClass.LogWarning("EraRecordWriter.CloseOpenEra: " + e.Message); }
        }
    }
}
