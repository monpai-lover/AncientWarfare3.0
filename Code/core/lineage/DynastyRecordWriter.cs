using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     朝代写入。OnKingChanged 时:新王 shi_id != 当前朝代 shi_id → 切换朝代。
    ///     朝代名 = 氏名 + "朝"（如"幸朝"），无氏则取雅字。
    /// </summary>
    internal static class DynastyRecordWriter
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;
        private static string TABLE => DynastyPeriodTableItem.GetTableName();

        // 雅字兜底池（当氏名为空时用）
        private static readonly string[] DYNASTY_CHARS =
        { "汉", "唐", "宋", "楚", "燕", "赵", "魏", "齐", "秦", "晋",
          "隋", "夏", "商", "周", "吴", "蜀", "越", "郑", "卫", "鲁" };

        private static readonly System.Random Rng = new System.Random();

        public static void OnKingChanged(Kingdom pKingdom, Actor pNewKing)
        {
            if (!Ready || pKingdom?.data == null || pNewKing?.data == null) return;

            pNewKing.data.get(LineageKeys.SHI_ID, out long newShiId, -1L);
            long curShiId = GetCurrentDynastyShiId(pKingdom.id);

            // 同氏族继续，不切朝代
            if (curShiId >= 0 && curShiId == newShiId) return;

            // 关旧朝代
            CloseOpenDynasty(pKingdom.id);

            // 开新朝代
            pNewKing.data.get(LineageKeys.CLAN_NAME, out string clanName, "");
            string dynastyName = string.IsNullOrEmpty(clanName)
                ? DYNASTY_CHARS[Rng.Next(DYNASTY_CHARS.Length)] + "朝"
                : clanName + "朝";
            string kingdomColor = HistoryColors.FromKingdom(pKingdom);
            string dynastyColor = HistoryColors.FromClan(pNewKing.clan, pKingdom);
            if (string.IsNullOrEmpty(dynastyColor)) dynastyColor = kingdomColor;

            int idx = CountDynasties(pKingdom.id) + 1;
            long dynastyId = TableIdAllocator.Next(DB, TABLE, "DYNASTY_ID");
            double now = World.world.getCurWorldTime();

            try
            {
                DB.Insert(TABLE,
                    ColumnVal.Create("DYNASTY_ID",              dynastyId),
                    ColumnVal.Create("KINGDOM_ID",              pKingdom.id),
                    ColumnVal.Create("KINGDOM_COLOR",           kingdomColor),
                    ColumnVal.Create("DYNASTY_INDEX",           idx),
                    ColumnVal.Create("SHI_ID",                  newShiId),
                    ColumnVal.Create("CLAN_NAME",               clanName ?? ""),
                    ColumnVal.Create("FOUNDER_KING_ACTOR_ID",   pNewKing.data.id),
                    ColumnVal.Create("DYNASTY_NAME",            dynastyName),
                    ColumnVal.Create("DYNASTY_COLOR",           dynastyColor),
                    ColumnVal.Create("START_TIME",              now),
                    ColumnVal.Create("END_TIME",                -1.0));
                HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.DYNASTY_CHANGE,
                    HistoryText.Colored(dynastyName, dynastyColor) + " 建立");
            }
            catch (Exception e) { ModClass.LogWarning("DynastyRecordWriter.OnKingChanged: " + e.Message); }
        }

        public static void CloseOpenDynasty(long pKingdomId)
        {
            if (!Ready) return;
            try
            {
                using var findCmd = new SQLiteCommand(DB);
                findCmd.CommandText = $"SELECT DYNASTY_ID FROM {TABLE} " +
                                      $"WHERE KINGDOM_ID=@kid AND END_TIME=-1 ORDER BY START_TIME DESC LIMIT 1";
                findCmd.Parameters.AddWithValue("@kid", pKingdomId);
                object v = findCmd.ExecuteScalar();
                if (v == null || v == DBNull.Value) return;
                long openId = Convert.ToInt64(v);
                DB.UpdateValue(TABLE,
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("DYNASTY_ID", openId) },
                    ColumnVal.Create("END_TIME", World.world.getCurWorldTime()));
            }
            catch (Exception e) { ModClass.LogWarning("DynastyRecordWriter.CloseOpenDynasty: " + e.Message); }
        }

        // 查当前朝代的 shi_id（end=-1 行），-1=无
        private static long GetCurrentDynastyShiId(long pKingdomId)
        {
            if (!Ready) return -1;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT SHI_ID FROM {TABLE} " +
                                  $"WHERE KINGDOM_ID=@kid AND END_TIME=-1 ORDER BY START_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                object v = cmd.ExecuteScalar();
                return (v == null || v == DBNull.Value) ? -1L : Convert.ToInt64(v);
            }
            catch { return -1; }
        }

        private static int CountDynasties(long pKingdomId)
        {
            if (!Ready) return 0;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT COUNT(*) FROM {TABLE} WHERE KINGDOM_ID=@kid";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                object v = cmd.ExecuteScalar();
                return (v == null || v == DBNull.Value) ? 0 : Convert.ToInt32(v);
            }
            catch { return 0; }
        }
    }
}
