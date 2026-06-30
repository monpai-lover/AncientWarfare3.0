using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    /// <summary>编年史一条事件(人物传记 / 国家历史 / 城市易主 通用展示结构)。</summary>
    internal class HistoryEntry
    {
        public long   event_id;
        public double world_time;
        public string year_prefix;   // 已是写入当时拼好的快照(通用年 + 国家年号纪年),展示在前
        public string subject_name;  // 事件发生时人/国/城名快照
        public string content;       // 事件内容,展示在后
        public string event_type;
    }

    /// <summary>
    ///     编年史只读查询(对应三张事件表 PersonBiography / KingdomHistory / CityHistory)。
    ///     HistoryWriter 只写;本类只读。统一按 WORLD_TIME 升序(时间线)。DB null 返空表,不抛。
    /// </summary>
    internal static class HistoryQuery
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance.OperatingDB;

        public static List<HistoryEntry> ReadPerson(long pActorId)
        {
            return Read(PersonBiographyTableItem.GetTableName(), "ACTOR_ID", pActorId);
        }

        public static List<HistoryEntry> ReadKingdom(long pKingdomId)
        {
            return Read(KingdomHistoryTableItem.GetTableName(), "KINGDOM_ID", pKingdomId);
        }

        public static List<HistoryEntry> ReadCity(long pCityId)
        {
            return Read(CityHistoryTableItem.GetTableName(), "CITY_ID", pCityId);
        }

        // ─────────────────────── 全王国名册 ───────────────────────

        /// <summary>读 KingdomArchive 全表(全王国,含亡国),按建国时间升序。</summary>
        public static List<KingdomArchiveInfo> GetAllKingdoms()
        {
            var result = new List<KingdomArchiveInfo>();
            var db = DB;
            if (db == null) return result;

            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                "SELECT KINGDOM_ID, KINGDOM_NAME, COLOR_TEXT, COLOR_ID, BANNER_ICON_ID, BANNER_BACKGROUND_ID, " +
                "BANNER_ID, IS_ALIVE, FOUNDED_TIME, DESTROYED_TIME " +
                $"FROM {KingdomArchiveTableItem.GetTableName()} ORDER BY FOUNDED_TIME ASC, KINGDOM_ID ASC";

            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new KingdomArchiveInfo
                {
                    kingdom_id = reader.GetInt64(0),
                    kingdom_name = SafeStr(reader, 1),
                    color_text = SafeStr(reader, 2),
                    color_id = ToInt(reader, 3),
                    banner_icon_id = ToInt(reader, 4),
                    banner_background_id = ToInt(reader, 5),
                    banner_id = SafeStr(reader, 6),
                    is_alive = ToInt(reader, 7) != 0,
                    founded_time = reader.GetDouble(8),
                    destroyed_time = reader.GetDouble(9)
                });
            }
            return result;
        }

        // ─────────────────────── 朝代分段(从事件推导) ───────────────────────

        /// <summary>
        ///     把一个国家的历史事件按"朝代时期"分段:
        ///     - 每个 rule_change 事件开启一个新王统治期(king_name 取该事件 SUBJECT_NAME);
        ///     - found 之后到首个 rule_change 之前 = 无王时期(按时间区间);
        ///     - 段的 end = 下一个 rule_change 的 world_time(最后一段 end=-1 至今 / 亡国时间)。
        ///     段内 events = 落在 [start, end) 的全部该国事件。
        /// </summary>
        public static List<ReignPeriod> GetKingdomReigns(long pKingdomId)
        {
            var events = ReadKingdom(pKingdomId); // 已按 world_time 升序
            var periods = new List<ReignPeriod>();
            if (events.Count == 0) return periods;

            // 1) 用 rule_change 切分点建段骨架。首段从最早事件时间起(可能是 found / 无王)。
            ReignPeriod current = new ReignPeriod
            {
                has_king = false,
                king_name = "",
                start_time = events[0].world_time,
                year_prefix_snapshot = events[0].year_prefix
            };
            periods.Add(current);

            foreach (var e in events)
            {
                if (e.event_type == "rule_change")
                {
                    // 关闭上一段、开新王段。
                    current.end_time = e.world_time;
                    current = new ReignPeriod
                    {
                        has_king = true,
                        king_name = e.subject_name,
                        start_time = e.world_time,
                        year_prefix_snapshot = e.year_prefix
                    };
                    periods.Add(current);
                }
            }

            // 2) 把每个事件归入其所属段([start, end))。
            foreach (var e in events)
            {
                ReignPeriod target = null;
                foreach (var p in periods)
                {
                    bool afterStart = e.world_time >= p.start_time;
                    bool beforeEnd = p.end_time < 0 || e.world_time < p.end_time;
                    if (afterStart && beforeEnd) target = p; // 取最后一个匹配段(切分点归新段)
                }
                if (target != null) target.events.Add(e);
            }

            // 3) 丢弃既无王又无事件的空首段(found 紧接 rule_change 时首段可能为空无意义)。
            periods.RemoveAll(p => !p.has_king && p.events.Count == 0);
            return periods;
        }

        /// <summary>通用读取:某表里某 *_ID 的全部事件,按 WORLD_TIME 升序(再按 EVENT_ID 稳定排序)。</summary>
        private static List<HistoryEntry> Read(string pTable, string pIdColumn, long pId)
        {
            var result = new List<HistoryEntry>();
            var db = DB;
            if (db == null) return result;

            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT EVENT_ID, WORLD_TIME, YEAR_PREFIX, SUBJECT_NAME, CONTENT, EVENT_TYPE " +
                $"FROM {pTable} WHERE {pIdColumn}=@id ORDER BY WORLD_TIME ASC, EVENT_ID ASC";
            cmd.Parameters.AddWithValue("@id", pId);

            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new HistoryEntry
                {
                    event_id = reader.GetInt64(0),
                    world_time = reader.GetDouble(1),
                    year_prefix = SafeStr(reader, 2),
                    subject_name = SafeStr(reader, 3),
                    content = SafeStr(reader, 4),
                    event_type = SafeStr(reader, 5)
                });
            }
            return result;
        }

        private static string SafeStr(SQLiteDataReader pReader, int pOrdinal)
        {
            return pReader.IsDBNull(pOrdinal) ? "" : pReader.GetString(pOrdinal);
        }

        private static int ToInt(SQLiteDataReader pReader, int pOrdinal)
        {
            return pReader.IsDBNull(pOrdinal) ? 0 : (int)pReader.GetInt64(pOrdinal);
        }
    }
}
