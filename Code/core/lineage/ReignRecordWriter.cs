using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     君主世系写入。OnKingChanged→OpenReign(新)+CloseOpenReign(旧)；
    ///     OnKingDied/OnAbdicate→CloseOpenReign(已在各 On 方法调过)。
    ///     亡国→CloseOpenReign("kingdom_fell")。谥号→SetPosthumous。
    /// </summary>
    internal static class ReignRecordWriter
    {
        public struct ReignInfo
        {
            public long ReignId;
            public long KingdomId;
            public long KingActorId;
            public int StartPopulation;
            public int StartCityCount;
            public double StartTime;

            public bool IsValid => ReignId >= 0;

            public static ReignInfo Empty => new ReignInfo
            {
                ReignId = -1,
                KingdomId = -1,
                KingActorId = -1
            };
        }

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;
        private static string TABLE => KingdomReignTableItem.GetTableName();

        // ── 外部接口 ──

        public static void OpenReign(Kingdom pKingdom, Actor pNewKing)
        {
            if (!Ready || pKingdom?.data == null || pNewKing?.data == null) return;
            long reignId = TableIdAllocator.Next(DB, TABLE, "REIGN_ID");
            int idx = CountReigns(pKingdom.id) + 1;
            double now = World.world.getCurWorldTime();
            pKingdom.data.get(LineageKeys.KINGDOM_YEAR_NAME, out string stem, "");
            int pop = SafePopulation(pKingdom);
            int cities = SafeCityCount(pKingdom);
            string kingdomColor = HistoryColors.FromKingdom(pKingdom);
            string kingColor = HistoryColors.FromActor(pNewKing);
            try
            {
                DB.Insert(TABLE,
                    ColumnVal.Create("REIGN_ID",           reignId),
                    ColumnVal.Create("KINGDOM_ID",         pKingdom.id),
                    ColumnVal.Create("KINGDOM_COLOR",      kingdomColor),
                    ColumnVal.Create("KING_ACTOR_ID",      pNewKing.data.id),
                    ColumnVal.Create("KING_NAME",          pNewKing.getName()),
                    ColumnVal.Create("KING_COLOR",         string.IsNullOrEmpty(kingColor) ? kingdomColor : kingColor),
                    ColumnVal.Create("REIGN_INDEX",        idx),
                    ColumnVal.Create("START_TIME",         now),
                    ColumnVal.Create("END_TIME",           -1.0),
                    ColumnVal.Create("YEAR_NAME_STEM",     stem ?? ""),
                    ColumnVal.Create("YEAR_NAME_COLOR",    kingdomColor),
                    ColumnVal.Create("POSTHUMOUS_TITLE",   ""),
                    ColumnVal.Create("POSTHUMOUS_COLOR",   ""),
                    ColumnVal.Create("END_REASON",         ""),
                    ColumnVal.Create("START_POPULATION",   pop),
                    ColumnVal.Create("START_CITY_COUNT",   cities));
            }
            catch (Exception e) { ModClass.LogWarning("ReignRecordWriter.OpenReign: " + e.Message); }
        }

        /// <summary>关闭该国当前 end_time=-1 的 reign。</summary>
        public static ReignInfo CloseOpenReign(long pKingdomId, string pReason)
        {
            if (!Ready) return ReignInfo.Empty;
            ReignInfo open = ReadOpenReignInfo(pKingdomId);
            if (!open.IsValid) return ReignInfo.Empty;
            try
            {
                DB.UpdateValue(TABLE,
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("REIGN_ID", open.ReignId) },
                    ColumnVal.Create("END_TIME",   World.world.getCurWorldTime()),
                    ColumnVal.Create("END_REASON", pReason ?? ""));
                return open;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("ReignRecordWriter.CloseOpenReign: " + e.Message);
                return ReignInfo.Empty;
            }
        }

        /// <summary>回填谥号到 KingdomReign 行。</summary>
        public static void SetPosthumous(long pReignId, string pFullTitle)
        {
            SetPosthumous(pReignId, pFullTitle, "");
        }

        public static void SetPosthumous(long pReignId, string pFullTitle, string pColor)
        {
            if (!Ready || pReignId < 0) return;
            try
            {
                DB.UpdateValue(TABLE,
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("REIGN_ID", pReignId) },
                    ColumnVal.Create("POSTHUMOUS_TITLE", pFullTitle ?? ""),
                    ColumnVal.Create("POSTHUMOUS_COLOR", HistoryColors.Normalize(pColor)));
            }
            catch { }
        }

        public static void SetPosthumous(long pKingdomId, long pActorId, string pFullTitle)
        {
            if (!Ready) return;
            long id = FindReignByActor(pKingdomId, pActorId);
            if (id < 0) return;
            SetPosthumous(id, pFullTitle);
        }

        /// <summary>读当前开着的 reign 行（end=-1），供谥号评定读取起始国力。</summary>
        public static (long reignId, int startPop, int startCities, double startTime)
            ReadOpenReign(long pKingdomId)
        {
            ReignInfo info = ReadOpenReignInfo(pKingdomId);
            if (!info.IsValid) return (-1, 0, 0, 0);
            return (info.ReignId, info.StartPopulation, info.StartCityCount, info.StartTime);
        }

        public static ReignInfo ReadOpenReignInfo(long pKingdomId)
        {
            if (!Ready) return ReignInfo.Empty;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    $"SELECT REIGN_ID, KINGDOM_ID, KING_ACTOR_ID, START_POPULATION, START_CITY_COUNT, START_TIME " +
                    $"FROM {TABLE} WHERE KINGDOM_ID=@kid AND END_TIME=-1 ORDER BY START_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                if (!r.Read()) return ReignInfo.Empty;
                return new ReignInfo
                {
                    ReignId = r.GetInt64(0),
                    KingdomId = r.GetInt64(1),
                    KingActorId = r.GetInt64(2),
                    StartPopulation = (int)r.GetInt64(3),
                    StartCityCount = (int)r.GetInt64(4),
                    StartTime = r.GetDouble(5)
                };
            }
            catch { return ReignInfo.Empty; }
        }

        // ── 内部辅助 ──

        public static long FindOpenReignId(long pKingdomId)
        {
            if (!Ready) return -1;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT REIGN_ID FROM {TABLE} " +
                                  $"WHERE KINGDOM_ID=@kid AND END_TIME=-1 ORDER BY START_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                object v = cmd.ExecuteScalar();
                return (v == null || v == DBNull.Value) ? -1L : Convert.ToInt64(v);
            }
            catch { return -1; }
        }

        private static long FindReignByActor(long pKingdomId, long pActorId)
        {
            if (!Ready) return -1;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT REIGN_ID FROM {TABLE} " +
                                  $"WHERE KINGDOM_ID=@kid AND KING_ACTOR_ID=@aid ORDER BY START_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                cmd.Parameters.AddWithValue("@aid", pActorId);
                object v = cmd.ExecuteScalar();
                return (v == null || v == DBNull.Value) ? -1L : Convert.ToInt64(v);
            }
            catch { return -1; }
        }

        private static int CountReigns(long pKingdomId)
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

        private static int SafePopulation(Kingdom k)
        {
            try { return k.getPopulationTotal(); } catch { return 0; }
        }

        private static int SafeCityCount(Kingdom k)
        {
            try { return k.cities?.Count ?? 0; } catch { return 0; }
        }
    }
}
