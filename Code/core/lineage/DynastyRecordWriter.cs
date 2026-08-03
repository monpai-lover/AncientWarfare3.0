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
        public const string END_REASON_REPLACED = "dynasty_replaced";
        public const string END_REASON_KINGDOM_FELL = "kingdom_fell";
        public const string END_REASON_UNKNOWN_SUCCESSOR = "unknown_successor";

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;
        private static string TABLE => DynastyPeriodTableItem.GetTableName();

        // 雅字兜底池（当氏名为空时用）
        private static readonly string[] DYNASTY_CHARS =
        { "汉", "唐", "宋", "楚", "燕", "赵", "魏", "齐", "秦", "晋",
          "隋", "夏", "商", "周", "吴", "蜀", "越", "郑", "卫", "鲁" };

        private static readonly System.Random Rng = new System.Random();

        public static DynastyTransitionStatus TryOnKingChanged(
            Kingdom pKingdom, Actor pNewKing)
        {
            if (!Ready || pKingdom?.data == null || pNewKing?.data == null)
                return DynastyTransitionStatus.Failure;
            if (!LineageService.IsXiaKingdom(pKingdom))
                return DynastyTransitionStatus.NoChange;

            pNewKing.data.get(LineageKeys.SHI_ID, out long newShiId, -1L);
            string newClanName = ResolveDynastyClanName(pNewKing, newShiId);
            long curShiId = GetCurrentDynastyShiId(pKingdom.id);

            if (IsDynasticContinuity(pKingdom.id, curShiId, newShiId))
                return DynastyTransitionStatus.NoChange;

            // 关旧朝代
            string closeReason = (newShiId < 0 || !LineageService.IsXia(pNewKing))
                ? END_REASON_UNKNOWN_SUCCESSOR
                : END_REASON_REPLACED;
            if (!TryCloseOpenDynasty(pKingdom.id, closeReason))
                return DynastyTransitionStatus.Failure;
            if (newShiId < 0 || !LineageService.IsXia(pNewKing))
                return DynastyTransitionStatus.NoChange;

            // 开新朝代
            string dynastyName = BuildRulePeriodName(pNewKing, pKingdom, newShiId, newClanName);
            string stateName = pKingdom.name ?? "";
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
                    ColumnVal.Create("CLAN_NAME",               newClanName ?? ""),
                    ColumnVal.Create("FOUNDER_KING_ACTOR_ID",   pNewKing.data.id),
                    ColumnVal.Create("DYNASTY_NAME",            dynastyName),
                    ColumnVal.Create("DYNASTY_COLOR",           dynastyColor),
                    ColumnVal.Create("ORIGINAL_KINGDOM_NAME",   pKingdom.name ?? ""),
                    ColumnVal.Create("STATE_NAME",              stateName),
                    ColumnVal.Create("START_TIME",              now),
                    ColumnVal.Create("END_TIME",                -1.0),
                    ColumnVal.Create("END_REASON",              ""));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("DynastyRecordWriter.TryOnKingChanged: " +
                                    e.Message);
                return DynastyTransitionStatus.Failure;
            }

            try
            {
                HistoryWriter.RecordKingdom(pKingdom,
                    KingdomEvent.DYNASTY_CHANGE,
                    HistoryText.Colored(dynastyName, dynastyColor) +
                    " \u5F00\u59CB");
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Dynasty history write failed: " +
                                    e.Message);
            }
            return DynastyTransitionStatus.Created;
        }

        internal static bool WouldCreateNewDynasty(Kingdom pKingdom,
            Actor pNewKing, long pPreBranchShiId)
        {
            if (!Ready || pKingdom?.data == null || pNewKing?.data == null ||
                pPreBranchShiId < 0 ||
                !LineageService.IsXiaKingdom(pKingdom) ||
                !LineageService.IsXia(pNewKing)) return false;
            long currentShiId = GetCurrentDynastyShiId(pKingdom.id);
            return !IsDynasticContinuity(pKingdom.id, currentShiId,
                pPreBranchShiId);
        }

        public static bool UpdateCurrentStateName(long pKingdomId,
            string pStateName)
        {
            if (!Ready || pKingdomId < 0 ||
                !StateNameRules.IsValid(pStateName)) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " + TABLE +
                                      " SET STATE_NAME=@name " +
                                      "WHERE KINGDOM_ID=@kingdom AND END_TIME=-1";
                command.Parameters.AddWithValue("@name", pStateName);
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                return command.ExecuteNonQuery() > 0;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Dynasty state-name sync failed: " +
                                    e.Message);
                return false;
            }
        }

        private static string ResolveDynastyClanName(Actor pKing, long pShiId)
        {
            string clanName = "";
            try { pKing?.data?.get(LineageKeys.CLAN_NAME, out clanName, ""); } catch { clanName = ""; }
            if (!string.IsNullOrEmpty(clanName)) return clanName;
            if (pShiId < 0) return "";

            var shi = LineageQuery.GetShiBranchInfo(pShiId);
            return shi?.clan_name ?? "";
        }

        private static bool IsDynasticContinuity(long pKingdomId,
            long pCurrentShiId, long pNewShiId)
        {
            if (StateNameRules.IsSameShiContinuity(pCurrentShiId,
                    pNewShiId)) return true;
            ShiBranchInfo current = LineageQuery.GetShiBranchInfo(
                pCurrentShiId);
            ShiBranchInfo next = LineageQuery.GetShiBranchInfo(pNewShiId);
            if (current == null || next == null) return false;
            List<ShiBranchInfo> parents =
                LineageQuery.GetShiParentChain(pNewShiId);
            var parentIds = new List<long>(parents.Count);
            for (int i = 0; i < parents.Count; i++)
                if (parents[i] != null) parentIds.Add(parents[i].shi_id);
            return StateNameRules.IsDynasticContinuity(pCurrentShiId,
                pNewShiId, current.lineage_id, next.lineage_id,
                next.origin_kingdom_id, pKingdomId, next.source_type,
                parentIds);
        }

        private static string BuildRulePeriodName(Actor pKing, Kingdom pKingdom, long pShiId, string pClanName)
        {
            string clanName = pClanName ?? "";
            string cityName = "";

            if (pShiId >= 0)
            {
                var shi = LineageQuery.GetShiBranchInfo(pShiId);
                if (shi != null)
                {
                    if (string.IsNullOrEmpty(clanName)) clanName = shi.clan_name ?? "";
                    cityName = shi.origin_city_name ?? "";
                }
            }

            if (string.IsNullOrEmpty(cityName))
                cityName = pKing?.city?.data?.name ?? pKingdom?.capital?.data?.name ?? "";
            if (string.IsNullOrEmpty(clanName))
                clanName = DYNASTY_CHARS[Rng.Next(DYNASTY_CHARS.Length)];

            string ruleName = clanName + "\u6C0F\u7EDF\u6CBB";
            return string.IsNullOrEmpty(cityName) ? ruleName : cityName + " " + ruleName;
        }

        public static void CloseOpenDynasty(long pKingdomId)
        {
            CloseOpenDynasty(pKingdomId, "");
        }

        public static void CloseOpenDynasty(long pKingdomId, string pReason)
        {
            TryCloseOpenDynasty(pKingdomId, pReason);
        }

        private static bool TryCloseOpenDynasty(long pKingdomId,
            string pReason)
        {
            if (!Ready) return false;
            try
            {
                using var findCmd = new SQLiteCommand(DB);
                findCmd.CommandText = $"SELECT DYNASTY_ID FROM {TABLE} " +
                                      $"WHERE KINGDOM_ID=@kid AND END_TIME=-1 ORDER BY START_TIME DESC LIMIT 1";
                findCmd.Parameters.AddWithValue("@kid", pKingdomId);
                object v = findCmd.ExecuteScalar();
                if (v == null || v == DBNull.Value) return true;
                long openId = Convert.ToInt64(v);
                using var close = new SQLiteCommand(DB)
                {
                    CommandText = "UPDATE " + TABLE +
                                  " SET END_TIME=@time,END_REASON=@reason " +
                                  "WHERE DYNASTY_ID=@id AND END_TIME=-1"
                };
                close.Parameters.AddWithValue("@time",
                    World.world.getCurWorldTime());
                close.Parameters.AddWithValue("@reason", pReason ?? "");
                close.Parameters.AddWithValue("@id", openId);
                if (close.ExecuteNonQuery() != 1) return false;
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("DynastyRecordWriter.CloseOpenDynasty: " +
                                    e.Message);
                return false;
            }
        }

        public static long GetCurrentDynastyId(long pKingdomId)
        {
            if (!Ready) return -1;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT DYNASTY_ID FROM {TABLE} " +
                                  "WHERE KINGDOM_ID=@kid AND END_TIME=-1 " +
                                  "ORDER BY START_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? -1L : Convert.ToInt64(value);
            }
            catch { return -1; }
        }

        // 查当前朝代的 shi_id（end=-1 行），-1=无
        internal static long GetCurrentDynastyShiId(long pKingdomId)
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

        private static string GetCurrentDynastyClanName(long pKingdomId)
        {
            if (!Ready) return "";
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT IFNULL(CLAN_NAME, '') FROM {TABLE} " +
                                  $"WHERE KINGDOM_ID=@kid AND END_TIME=-1 ORDER BY START_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                object v = cmd.ExecuteScalar();
                return (v == null || v == DBNull.Value) ? "" : v.ToString();
            }
            catch { return ""; }
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
