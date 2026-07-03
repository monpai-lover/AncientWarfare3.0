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
        public string year_prefix_rich = "";
        public string subject_name;  // 事件发生时人/国/城名快照
        public string subject_color = "";
        public string content;       // 事件内容,展示在后
        public string content_rich = "";
        public string event_type;
        public string kingdom_name = ""; // 城市史专用:所属国名快照(归属期分段用)
        public string kingdom_color = "";
        public string category = "";     // 人物史专用:life/honor/clan/war/bond(UI 筛选用)
        public int    age_at_event = -1;
        public int    is_king_at_event = 0;
        public string role_snapshot = "";
        public string role_label = "";
        public long   context_kingdom_id = -1;
        public string context_kingdom_name = "";
        public string context_kingdom_color = "";
        public string target_type = "";
        public long   target_id = -1;
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
            // 人物史多读 CATEGORY(UI 分类筛选用)。老档若无此列,自动迁移已补默认空串。
            var result = new List<HistoryEntry>();
            var db = DB;
            if (db == null) return result;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                "SELECT EVENT_ID, WORLD_TIME, YEAR_PREFIX, YEAR_PREFIX_RICH, SUBJECT_NAME, SUBJECT_COLOR, " +
                "CONTENT, CONTENT_RICH, EVENT_TYPE, CATEGORY, AGE_AT_EVENT, IS_KING_AT_EVENT, ROLE_SNAPSHOT, ROLE_LABEL, " +
                "CONTEXT_KINGDOM_ID, CONTEXT_KINGDOM_NAME, CONTEXT_KINGDOM_COLOR, " +
                "TARGET_TYPE, TARGET_ID " +
                $"FROM {PersonBiographyTableItem.GetTableName()} WHERE ACTOR_ID=@id ORDER BY WORLD_TIME ASC, EVENT_ID ASC";
            cmd.Parameters.AddWithValue("@id", pActorId);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            var seenDeathEvents = new HashSet<string>();
            while (reader.Read())
            {
                var entry = new HistoryEntry
                {
                    event_id     = reader.GetInt64(0),
                    world_time   = reader.GetDouble(1),
                    year_prefix  = SafeStr(reader, 2),
                    year_prefix_rich = SafeStr(reader, 3),
                    subject_name = SafeStr(reader, 4),
                    subject_color = SafeStr(reader, 5),
                    content      = SafeStr(reader, 6),
                    content_rich = SafeStr(reader, 7),
                    event_type   = SafeStr(reader, 8),
                    category     = SafeStr(reader, 9),
                    age_at_event = ToInt(reader, 10, -1),
                    is_king_at_event = ToInt(reader, 11),
                    role_snapshot = SafeStr(reader, 12),
                    role_label = SafeStr(reader, 13),
                    context_kingdom_id = reader.IsDBNull(14) ? -1 : reader.GetInt64(14),
                    context_kingdom_name = SafeStr(reader, 15),
                    context_kingdom_color = SafeStr(reader, 16),
                    target_type = SafeStr(reader, 17),
                    target_id = reader.IsDBNull(18) ? -1 : reader.GetInt64(18)
                };
                if (IsDuplicateDeathEntry(entry, seenDeathEvents)) continue;
                result.Add(entry);
            }
            return result;
        }

        private static bool IsDuplicateDeathEntry(HistoryEntry pEntry, HashSet<string> pSeen)
        {
            if (pEntry == null || pSeen == null) return false;
            if (pEntry.event_type != PersonEvent.DEATH && pEntry.event_type != PersonEvent.BOND_DEATH) return false;
            string content = !string.IsNullOrEmpty(pEntry.content_rich) ? pEntry.content_rich : pEntry.content;
            string key = pEntry.event_type + "|" + content;
            return !pSeen.Add(key);
        }

        public static List<HistoryEntry> ReadKingdom(long pKingdomId)
        {
            var result = Read(KingdomHistoryTableItem.GetTableName(), "KINGDOM_ID", pKingdomId);
            NormalizeFigureFoundEvent(pKingdomId, result);
            RemoveSimpleKingDeathCoveredByPosthumous(result);
            FilterNonXiaKingdomEvents(pKingdomId, result);
            return result;
        }

        private static void NormalizeFigureFoundEvent(long pKingdomId, List<HistoryEntry> pEntries)
        {
            if (pEntries == null || pEntries.Count == 0) return;
            if (!FigureStateStore.TryGetAppliedKingdomName(pKingdomId, out string appliedName)) return;

            Kingdom live = World.world?.kingdoms?.get(pKingdomId);
            string liveColor = HistoryColors.FromKingdom(live);

            foreach (var entry in pEntries)
            {
                if (entry == null || entry.event_type != KingdomEvent.FOUND) continue;

                string color = liveColor;
                if (string.IsNullOrEmpty(color)) color = entry.context_kingdom_color;
                if (string.IsNullOrEmpty(color)) color = entry.subject_color;

                HistoryText text = HistoryText.Colored(appliedName, color) + " 建立";
                entry.subject_name = appliedName;
                entry.context_kingdom_name = appliedName;
                entry.content = text.Plain;
                entry.content_rich = text.Rich;
                if (!string.IsNullOrEmpty(color))
                {
                    entry.subject_color = color;
                    entry.context_kingdom_color = color;
                }
            }
        }

        private static void RemoveSimpleKingDeathCoveredByPosthumous(List<HistoryEntry> pEntries)
        {
            if (pEntries == null || pEntries.Count == 0) return;

            var posthumousTimes = new HashSet<string>();
            foreach (var entry in pEntries)
            {
                if (entry?.event_type != KingdomEvent.POSTHUMOUS) continue;
                posthumousTimes.Add(TimeKey(entry.world_time));
            }
            if (posthumousTimes.Count == 0) return;

            pEntries.RemoveAll(entry =>
                entry != null &&
                entry.event_type == KingdomEvent.KING_DIED &&
                posthumousTimes.Contains(TimeKey(entry.world_time)));
        }

        private static string TimeKey(double pTime)
        {
            return pTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void FilterNonXiaKingdomEvents(long pKingdomId, List<HistoryEntry> pEntries)
        {
            if (IsXiaKingdomHistory(pKingdomId)) return;
            if (pEntries == null) return;
            pEntries.RemoveAll(entry =>
                entry != null &&
                (entry.event_type == KingdomEvent.DYNASTY_CHANGE ||
                 entry.event_type == KingdomEvent.ERA_CHANGE ||
                 entry.event_type == KingdomEvent.POSTHUMOUS));
            foreach (var entry in pEntries)
            {
                if (entry == null) continue;
                entry.year_prefix = "";
                entry.year_prefix_rich = "";
            }
        }

        public static List<HistoryEntry> ReadCity(long pCityId)
        {
            var result = new List<HistoryEntry>();
            var db = DB;
            if (db == null) return result;

            using var cmd = new SQLiteCommand(db);
            // 城市史多读 KINGDOM_NAME(归属期分段用)。老档若无此列,自动迁移已补默认空串。
            cmd.CommandText =
                "SELECT EVENT_ID, WORLD_TIME, YEAR_PREFIX, YEAR_PREFIX_RICH, SUBJECT_NAME, SUBJECT_COLOR, " +
                "CONTENT, CONTENT_RICH, EVENT_TYPE, KINGDOM_NAME, KINGDOM_COLOR, " +
                "CONTEXT_KINGDOM_ID, CONTEXT_KINGDOM_NAME, CONTEXT_KINGDOM_COLOR, TARGET_TYPE, TARGET_ID " +
                $"FROM {CityHistoryTableItem.GetTableName()} WHERE CITY_ID=@id ORDER BY WORLD_TIME ASC, EVENT_ID ASC";
            cmd.Parameters.AddWithValue("@id", pCityId);

            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new HistoryEntry
                {
                    event_id = reader.GetInt64(0),
                    world_time = reader.GetDouble(1),
                    year_prefix = SafeStr(reader, 2),
                    year_prefix_rich = SafeStr(reader, 3),
                    subject_name = SafeStr(reader, 4),
                    subject_color = SafeStr(reader, 5),
                    content = SafeStr(reader, 6),
                    content_rich = SafeStr(reader, 7),
                    event_type = SafeStr(reader, 8),
                    kingdom_name = SafeStr(reader, 9),
                    kingdom_color = SafeStr(reader, 10),
                    context_kingdom_id = reader.IsDBNull(11) ? -1 : reader.GetInt64(11),
                    context_kingdom_name = SafeStr(reader, 12),
                    context_kingdom_color = SafeStr(reader, 13),
                    target_type = SafeStr(reader, 14),
                    target_id = reader.IsDBNull(15) ? -1 : reader.GetInt64(15)
                });
            }
            ClearNonXiaEventYearPrefixes(result);
            return result;
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
                "BANNER_ID, FOUNDER_ACTOR_ID, FOUNDER_NAME, CAPITAL_CITY_ID, CAPITAL_CITY_NAME, " +
                "IS_ALIVE, FOUNDED_TIME, DESTROYED_TIME " +
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
                    founder_actor_id = ToLong(reader, 7, -1),
                    founder_name = SafeStr(reader, 8),
                    capital_city_id = ToLong(reader, 9, -1),
                    capital_city_name = SafeStr(reader, 10),
                    is_alive = ToInt(reader, 11) != 0,
                    founded_time = ToDouble(reader, 12, 0),
                    destroyed_time = ToDouble(reader, 13, -1)
                });
            }
            foreach (var item in result) FillCapitalFallback(item);
            return result;
        }

        private static void FillCapitalFallback(KingdomArchiveInfo pInfo)
        {
            if (pInfo == null || pInfo.kingdom_id < 0) return;
            if (pInfo.capital_city_id >= 0 && !string.IsNullOrEmpty(pInfo.capital_city_name)) return;
            var db = DB;
            if (db == null) return;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT CITY_ID, IFNULL(SUBJECT_NAME, '') FROM {CityHistoryTableItem.GetTableName()} " +
                    $"WHERE CONTEXT_KINGDOM_ID=@kid ORDER BY WORLD_TIME ASC, EVENT_ID ASC LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pInfo.kingdom_id);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                if (!r.Read()) return;
                if (pInfo.capital_city_id < 0) pInfo.capital_city_id = ToLong(r, 0, -1);
                if (string.IsNullOrEmpty(pInfo.capital_city_name)) pInfo.capital_city_name = SafeStr(r, 1);
            }
            catch { }
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
            double destroyedTime = GetKingdomDestroyedTime(pKingdomId);

            // 1) 用 rule_change 切分点建段骨架。首段从最早事件时间起(可能是 found / 无王)。
            ReignPeriod current = new ReignPeriod
            {
                has_king = false,
                king_name = "",
                start_time = events[0].world_time,
                year_prefix_snapshot = events[0].year_prefix,
                period_color = events[0].context_kingdom_color
            };
            periods.Add(current);

            foreach (var e in events)
            {
                if (e.event_type == KingdomEvent.DESTROYED) destroyedTime = e.world_time;
                if (e.event_type == "rule_change")
                {
                    // 关闭上一段、开新王段。
                    current.end_time = e.world_time;
                    current = new ReignPeriod
                    {
                        has_king = true,
                        king_name = e.subject_name,
                        king_color = e.subject_color,
                        start_time = e.world_time,
                        year_prefix_snapshot = e.year_prefix,
                        period_color = e.context_kingdom_color
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
            if (destroyedTime >= 0)
            {
                foreach (var p in periods)
                    if (p.end_time < 0 && p.start_time <= destroyedTime)
                        p.end_time = destroyedTime;
            }
            ClearNonXiaReignDecoration(pKingdomId, periods);
            return periods;
        }

        /// <summary>
        ///     城市历史按"所属国 + 当时君主在位期"分段。城市换所属国或所属国换君主都会开启新段。
        /// </summary>
        public static List<ReignPeriod> GetCityPeriods(long pCityId)
        {
            var events = ReadCity(pCityId); // 已按 world_time 升序
            var periods = new List<ReignPeriod>();
            if (events.Count == 0) return periods;

            var reignCache = new Dictionary<long, List<ReignPeriod>>();
            ReignPeriod current = null;
            string currentKey = "";
            long ownerId = -1;
            double ownerStart = events[0].world_time;

            foreach (var e in events)
            {
                long eventOwnerId = e.context_kingdom_id;
                string eventOwnerName = !string.IsNullOrEmpty(e.context_kingdom_name) ? e.context_kingdom_name : (e.kingdom_name ?? "");
                string eventOwnerColor = !string.IsNullOrEmpty(e.context_kingdom_color) ? e.context_kingdom_color : e.kingdom_color;
                string ownerToken = eventOwnerId >= 0 ? eventOwnerId.ToString() : eventOwnerName;
                string currentOwnerToken = ownerId >= 0 ? ownerId.ToString() : (current?.owner_name ?? "");
                if (current == null || ownerToken != currentOwnerToken)
                {
                    ownerId = eventOwnerId;
                    ownerStart = e.world_time;
                }

                ReignPeriod reign = FindReignAt(eventOwnerId, e.world_time, reignCache);
                string key = ownerToken + "|" + (reign == null ? "owner" : (reign.king_actor_id + "@" + reign.start_time.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                if (current == null || key != currentKey)
                {
                    double start = ownerStart;
                    if (reign != null && reign.start_time > start) start = reign.start_time;
                    if (current != null) current.end_time = start;
                    string periodPrefix = (reign != null && System.Math.Abs(start - reign.start_time) < 0.001)
                        ? reign.year_prefix_snapshot
                        : e.year_prefix;

                    current = new ReignPeriod
                    {
                        is_city_period = true,
                        has_king = reign != null,
                        king_name = reign?.king_name ?? "",
                        king_color = reign?.king_color ?? eventOwnerColor,
                        king_actor_id = reign?.king_actor_id ?? -1,
                        posthumous_title = reign?.posthumous_title ?? "",
                        posthumous_color = reign?.posthumous_color ?? "",
                        start_time = start,
                        end_time = reign?.end_time ?? -1,
                        year_prefix_snapshot = periodPrefix,
                        owner_name = eventOwnerName,
                        owner_color = eventOwnerColor,
                        period_color = eventOwnerColor
                    };
                    periods.Add(current);
                    currentKey = key;
                }
                current.events.Add(e);
            }
            return periods;
        }

        private static ReignPeriod FindReignAt(long pKingdomId, double pTime,
            Dictionary<long, List<ReignPeriod>> pCache)
        {
            if (pKingdomId < 0) return null;
            if (!pCache.TryGetValue(pKingdomId, out var reigns))
            {
                reigns = ReadReignRows(pKingdomId);
                pCache[pKingdomId] = reigns;
            }
            ReignPeriod best = null;
            foreach (var r in reigns)
            {
                bool afterStart = pTime >= r.start_time;
                bool beforeEnd = r.end_time < 0 || pTime < r.end_time;
                if (afterStart && beforeEnd) best = r;
            }
            return best;
        }

        private static List<ReignPeriod> ReadReignRows(long pKingdomId)
        {
            var result = new List<ReignPeriod>();
            var db = DB;
            if (db == null || pKingdomId < 0) return result;
            if (!IsXiaKingdomHistory(pKingdomId)) return result;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT KING_ACTOR_ID, KING_NAME, KING_COLOR, START_TIME, END_TIME, " +
                    $"YEAR_NAME_STEM, YEAR_NAME_COLOR, POSTHUMOUS_TITLE, POSTHUMOUS_COLOR " +
                    $"FROM {KingdomReignTableItem.GetTableName()} WHERE KINGDOM_ID=@kid ORDER BY START_TIME ASC";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                while (r.Read())
                {
                    string stem = SafeStr(r, 5);
                    result.Add(new ReignPeriod
                    {
                        has_king = true,
                        king_actor_id = ToLong(r, 0, -1),
                        king_name = SafeStr(r, 1),
                        king_color = SafeStr(r, 2),
                        start_time = ToDouble(r, 3, 0),
                        end_time = ToDouble(r, 4, -1),
                        year_prefix_snapshot = string.IsNullOrEmpty(stem) ? "" : stem + "\u5143\u5E74",
                        period_color = SafeStr(r, 6),
                        posthumous_title = SafeStr(r, 7),
                        posthumous_color = SafeStr(r, 8)
                    });
                }
            }
            catch { }
            return result;
        }

        /// <summary>
        ///     国家史两层折叠:朝代 → 王段。
        ///     从 DynastyPeriod 表读朝代列表;将 GetKingdomReigns 的王段按时间归入各朝代。
        ///     王段的谥号从 KingdomReign 表补齐。DynastyPeriod 表为空时,
        ///     兜底为一个包含全部王段的"未知朝代"。
        /// </summary>
        public static List<DynastyView> GetKingdomDynasties(long pKingdomId)
        {
            var reigns  = GetKingdomReigns(pKingdomId);
            EnrichPosthumous(pKingdomId, reigns); // 补谥号

            var dynasties = ReadDynasties(pKingdomId);
            if (dynasties.Count == 0)
            {
                // 兜底：一个匿名朝代包含所有王段
                var fallback = new DynastyView
                {
                    dynasty_name = "",
                    dynasty_index = 0,
                    start_time = 0,
                    end_time = GetKingdomDestroyedTime(pKingdomId)
                };
                fallback.reigns.AddRange(reigns);
                return new List<DynastyView> { fallback };
            }

            // 将每个 reign 分配到时间最接近的朝代
            foreach (var r in reigns)
            {
                DynastyView best = null;
                foreach (var d in dynasties)
                {
                    bool afterStart = r.start_time >= d.start_time;
                    bool beforeEnd  = d.end_time < 0 || r.start_time < d.end_time;
                    if (afterStart && beforeEnd) best = d;
                }
                (best ?? dynasties[dynasties.Count - 1]).reigns.Add(r);
            }
            return dynasties;
        }

        private static List<DynastyView> ReadDynasties(long pKingdomId)
        {
            var result = new List<DynastyView>();
            var db = DB;
            if (db == null) return result;
            if (!IsXiaKingdomHistory(pKingdomId)) return result;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT DYNASTY_ID, DYNASTY_NAME, DYNASTY_COLOR, KINGDOM_COLOR, START_TIME, END_TIME, " +
                    $"IFNULL(SHI_ID, -1), IFNULL(CLAN_NAME, ''), IFNULL(ORIGINAL_KINGDOM_NAME, ''), IFNULL(END_REASON, '') " +
                    $"FROM {DynastyPeriodTableItem.GetTableName()} " +
                    $"WHERE KINGDOM_ID=@kid ORDER BY START_TIME ASC";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                int idx = 0;
                while (reader.Read())
                    result.Add(new DynastyView
                    {
                        dynasty_index = idx++,
                        dynasty_name  = SafeStr(reader, 1),
                        dynasty_color = SafeStr(reader, 2),
                        kingdom_color = SafeStr(reader, 3),
                        start_time    = reader.GetDouble(4),
                        end_time      = reader.GetDouble(5),
                        shi_id        = ToLong(reader, 6, -1),
                        clan_name     = SafeStr(reader, 7),
                        original_kingdom_name = SafeStr(reader, 8),
                        end_reason    = SafeStr(reader, 9)
                    });
            }
            catch { }
            FillDynastyOrigins(result);
            double destroyedTime = GetKingdomDestroyedTime(pKingdomId);
            if (destroyedTime >= 0)
            {
                foreach (var d in result)
                    if (d.end_time < 0 && d.start_time <= destroyedTime)
                    {
                        d.end_time = destroyedTime;
                        if (string.IsNullOrEmpty(d.end_reason))
                            d.end_reason = DynastyRecordWriter.END_REASON_KINGDOM_FELL;
                    }
            }
            return result;
        }

        private static void FillDynastyOrigins(List<DynastyView> pDynasties)
        {
            if (pDynasties == null) return;
            foreach (var dyn in pDynasties)
            {
                if (dyn == null || dyn.shi_id < 0) continue;
                var shi = LineageQuery.GetShiBranchInfo(dyn.shi_id);
                if (shi == null) continue;
                if (string.IsNullOrEmpty(dyn.clan_name)) dyn.clan_name = shi.clan_name ?? "";
                if (string.IsNullOrEmpty(dyn.origin_city_name)) dyn.origin_city_name = shi.origin_city_name ?? "";
            }
        }

        private static double GetKingdomDestroyedTime(long pKingdomId)
        {
            var db = DB;
            if (db == null) return -1;
            double archived = -1;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT IFNULL(DESTROYED_TIME, -1) FROM {KingdomArchiveTableItem.GetTableName()} " +
                    $"WHERE KINGDOM_ID=@kid LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                object v = cmd.ExecuteScalar();
                archived = v == null || v == System.DBNull.Value ? -1 : System.Convert.ToDouble(v);
            }
            catch { archived = -1; }
            if (archived >= 0) return archived;

            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT IFNULL(MAX(WORLD_TIME), -1) FROM {KingdomHistoryTableItem.GetTableName()} " +
                    $"WHERE KINGDOM_ID=@kid AND EVENT_TYPE=@type";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                cmd.Parameters.AddWithValue("@type", KingdomEvent.DESTROYED);
                object v = cmd.ExecuteScalar();
                return v == null || v == System.DBNull.Value ? -1 : System.Convert.ToDouble(v);
            }
            catch { return -1; }
        }

        // 查 KingdomReign 表给各 ReignPeriod 补 king_actor_id / posthumous_title
        private static void EnrichPosthumous(long pKingdomId, List<ReignPeriod> pReigns)
        {
            var db = DB;
            if (db == null) return;
            if (!IsXiaKingdomHistory(pKingdomId)) return;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT KING_ACTOR_ID, KING_NAME, KING_COLOR, START_TIME, POSTHUMOUS_TITLE, POSTHUMOUS_COLOR " +
                    $"FROM {KingdomReignTableItem.GetTableName()} " +
                    $"WHERE KINGDOM_ID=@kid ORDER BY START_TIME ASC";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                var rows = new System.Collections.Generic.List<(long actorId, string kingName, string kingColor, double startTime, string title, string titleColor)>();
                while (r.Read())
                {
                    rows.Add((r.GetInt64(0), SafeStr(r, 1), SafeStr(r, 2), r.GetDouble(3), SafeStr(r, 4), SafeStr(r, 5)));
                }
                foreach (var reign in pReigns)
                {
                    if (string.IsNullOrEmpty(reign.king_name)) continue;
                    (long actorId, string kingName, string kingColor, double startTime, string title, string titleColor) best = (-1, "", "", 0, "", "");
                    double bestDelta = double.MaxValue;
                    foreach (var row in rows)
                    {
                        double delta = System.Math.Abs(row.startTime - reign.start_time);
                        if (delta >= bestDelta) continue;
                        best = row;
                        bestDelta = delta;
                    }
                    if (best.actorId < 0) continue;
                    reign.king_actor_id = best.actorId;
                    if (!string.IsNullOrEmpty(best.kingName)) reign.king_name = best.kingName;
                    if (!string.IsNullOrEmpty(best.kingColor)) reign.king_color = best.kingColor;
                    if (!string.IsNullOrEmpty(best.title)) reign.posthumous_title = best.title;
                    if (!string.IsNullOrEmpty(best.titleColor)) reign.posthumous_color = best.titleColor;
                }
            }
            catch { }
        }
        private static List<HistoryEntry> Read(string pTable, string pIdColumn, long pId)
        {
            var result = new List<HistoryEntry>();
            var db = DB;
            if (db == null) return result;

            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT EVENT_ID, WORLD_TIME, YEAR_PREFIX, YEAR_PREFIX_RICH, SUBJECT_NAME, SUBJECT_COLOR, " +
                $"CONTENT, CONTENT_RICH, EVENT_TYPE, CONTEXT_KINGDOM_ID, CONTEXT_KINGDOM_NAME, CONTEXT_KINGDOM_COLOR, " +
                $"TARGET_TYPE, TARGET_ID " +
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
                    year_prefix_rich = SafeStr(reader, 3),
                    subject_name = SafeStr(reader, 4),
                    subject_color = SafeStr(reader, 5),
                    content = SafeStr(reader, 6),
                    content_rich = SafeStr(reader, 7),
                    event_type = SafeStr(reader, 8),
                    context_kingdom_id = reader.IsDBNull(9) ? -1 : reader.GetInt64(9),
                    context_kingdom_name = SafeStr(reader, 10),
                    context_kingdom_color = SafeStr(reader, 11),
                    target_type = SafeStr(reader, 12),
                    target_id = reader.IsDBNull(13) ? -1 : reader.GetInt64(13)
                });
            }
            SortHistoryEntries(result);
            return result;
        }

        private static void SortHistoryEntries(List<HistoryEntry> pEntries)
        {
            if (pEntries == null || pEntries.Count <= 1) return;
            pEntries.Sort(CompareHistoryEntries);
        }

        private static int CompareHistoryEntries(HistoryEntry pLeft, HistoryEntry pRight)
        {
            if (ReferenceEquals(pLeft, pRight)) return 0;
            if (pLeft == null) return 1;
            if (pRight == null) return -1;

            int time = pLeft.world_time.CompareTo(pRight.world_time);
            if (time != 0) return time;

            int priority = EventSortPriority(pLeft.event_type).CompareTo(EventSortPriority(pRight.event_type));
            if (priority != 0) return priority;

            return pLeft.event_id.CompareTo(pRight.event_id);
        }

        private static int EventSortPriority(string pEventType)
        {
            switch (pEventType)
            {
                case KingdomEvent.FOUND: return 0;
                case KingdomEvent.ERA_CHANGE: return 10;
                case KingdomEvent.RULE_CHANGE: return 20;
                case KingdomEvent.DYNASTY_CHANGE: return 30;
                default: return 100;
            }
        }

        private static bool IsXiaKingdomHistory(long pKingdomId)
        {
            if (pKingdomId < 0) return false;

            Kingdom live = World.world?.kingdoms?.get(pKingdomId);
            if (live?.data != null) return LineageService.IsXiaKingdom(live);

            var db = DB;
            if (db == null) return false;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT IFNULL(BANNER_ID, '') FROM {KingdomArchiveTableItem.GetTableName()} " +
                    $"WHERE KINGDOM_ID=@kid LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                object value = cmd.ExecuteScalar();
                string bannerId = value == null || value == System.DBNull.Value ? "" : value.ToString();
                return bannerId == LineageService.XIA_ASSET_ID;
            }
            catch { return false; }
        }

        private static void ClearNonXiaReignDecoration(long pKingdomId, List<ReignPeriod> pPeriods)
        {
            if (IsXiaKingdomHistory(pKingdomId)) return;
            if (pPeriods == null) return;
            foreach (var period in pPeriods)
            {
                if (period == null) continue;
                period.year_prefix_snapshot = "";
                period.king_actor_id = -1;
                period.posthumous_title = "";
                period.posthumous_color = "";
            }
        }

        private static void ClearNonXiaEventYearPrefixes(List<HistoryEntry> pEntries)
        {
            if (pEntries == null) return;
            foreach (var entry in pEntries)
            {
                if (entry == null || entry.context_kingdom_id < 0) continue;
                if (IsXiaKingdomHistory(entry.context_kingdom_id)) continue;
                entry.year_prefix = "";
                entry.year_prefix_rich = "";
            }
        }

        private static string SafeStr(SQLiteDataReader pReader, int pOrdinal)
        {
            return pReader.IsDBNull(pOrdinal) ? "" : pReader.GetString(pOrdinal);
        }

        private static int ToInt(SQLiteDataReader pReader, int pOrdinal)
        {
            return ToInt(pReader, pOrdinal, 0);
        }

        private static int ToInt(SQLiteDataReader pReader, int pOrdinal, int pDefault)
        {
            return pReader.IsDBNull(pOrdinal) ? pDefault : (int)pReader.GetInt64(pOrdinal);
        }

        private static long ToLong(SQLiteDataReader pReader, int pOrdinal, long pDefault = 0)
        {
            return pReader.IsDBNull(pOrdinal) ? pDefault : pReader.GetInt64(pOrdinal);
        }

        private static double ToDouble(SQLiteDataReader pReader, int pOrdinal, double pDefault = 0)
        {
            return pReader.IsDBNull(pOrdinal) ? pDefault : pReader.GetDouble(pOrdinal);
        }
    }
}
