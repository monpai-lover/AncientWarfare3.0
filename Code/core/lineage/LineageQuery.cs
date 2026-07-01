using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     姓族 / 氏支 / 成员 / 家族树 的只读查询接口(对应 docs 任务书 §4 查询部分)。
    ///     供阶段5 UI 调用。
    ///
    ///     单一数据源:所有 Xia 出生即写 ActorArchive(is_alive=1),晋升/合流/死亡都 upsert,
    ///     故活人与死人都在表里,查询统一走 SQLite,不需要"活人遍历+死人查库"两路合并。
    /// </summary>
    internal static class LineageQuery
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance.OperatingDB;

        // ─────────────────────── 姓族总览(所有姓) ───────────────────────

        /// <summary>所有出现过的姓 + 统计(总/存活/贵族/氏支数/最早时间)。</summary>
        public static List<SurnameOverview> GetSurnameOverview()
        {
            var result = new List<SurnameOverview>();
            var db = DB;
            if (db == null) return result;

            string actorTable = ActorArchiveTableItem.GetTableName();
            string shiTable = ShiBranchTableItem.GetTableName();
            string lineageTable = LineageGroupTableItem.GetTableName();

            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT FAMILY_NAME, " +
                $"COUNT(*) AS total, " +
                $"SUM(IS_ALIVE) AS alive, " +
                $"SUM(CASE WHEN STATUS='{LineageStatus.NOBLE}' AND IS_ALIVE=1 THEN 1 ELSE 0 END) AS noble " +
                $"FROM {actorTable} WHERE FAMILY_NAME IS NOT NULL AND FAMILY_NAME<>'' " +
                $"GROUP BY FAMILY_NAME ORDER BY total DESC";

            using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    result.Add(new SurnameOverview
                    {
                        family_name = reader.GetString(0),
                        total = ToInt(reader, 1),
                        alive = ToInt(reader, 2),
                        noble = ToInt(reader, 3)
                    });
                }
            }

            // 补氏支数量 + 最早成立时间(分别查 ShiBranch / LineageGroup)
            foreach (var o in result)
            {
                o.shi_count = CountShiOfSurname(o.family_name, lineageTable, shiTable);
                o.earliest_time = EarliestLineageTime(o.family_name, lineageTable);
                FillLineageOrigin(o, lineageTable);
            }

            return result;
        }

        private static int CountShiOfSurname(string pFamilyName, string pLineageTable, string pShiTable)
        {
            var db = DB;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT COUNT(*) FROM {pShiTable} WHERE LINEAGE_ID IN " +
                $"(SELECT LINEAGE_ID FROM {pLineageTable} WHERE FAMILY_NAME=@f)";
            cmd.Parameters.AddWithValue("@f", pFamilyName);
            return (int)(long)cmd.ExecuteScalar();
        }

        private static double EarliestLineageTime(string pFamilyName, string pLineageTable)
        {
            var db = DB;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText = $"SELECT IFNULL(MIN(CREATED_TIME), 0) FROM {pLineageTable} WHERE FAMILY_NAME=@f";
            cmd.Parameters.AddWithValue("@f", pFamilyName);
            return (double)cmd.ExecuteScalar();
        }

        private static void FillLineageOrigin(SurnameOverview pOverview, string pLineageTable)
        {
            var db = DB;
            if (db == null || pOverview == null) return;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT FOUNDER_ACTOR_ID, IFNULL(FOUNDER_NAME, ''), CREATED_TIME, " +
                    $"IFNULL(ORIGIN_KINGDOM_ID, -1), IFNULL(ORIGIN_CITY_ID, -1) " +
                    $"FROM {pLineageTable} WHERE FAMILY_NAME=@f ORDER BY CREATED_TIME ASC, LINEAGE_ID ASC LIMIT 1";
                cmd.Parameters.AddWithValue("@f", pOverview.family_name);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                if (!r.Read()) return;

                pOverview.founder_actor_id = ToLong(r, 0, -1);
                pOverview.founder_name = SafeStr(r, 1);
                pOverview.created_time = ToDouble(r, 2, 0);
                pOverview.earliest_time = pOverview.created_time;
                pOverview.origin_kingdom_id = ToLong(r, 3, -1);
                pOverview.origin_city_id = ToLong(r, 4, -1);

                FillOriginFromFounderArchive(pOverview);
                FillOriginFromLiveFounder(pOverview);

                ResolveKingdomArchive(pOverview.origin_kingdom_id, out string kingdomName, out string kingdomColor);
                if (!string.IsNullOrEmpty(kingdomName)) pOverview.origin_kingdom_name = kingdomName;
                if (!string.IsNullOrEmpty(kingdomColor)) pOverview.origin_kingdom_color = kingdomColor;

                string cityName = ResolveCityName(pOverview.origin_city_id);
                if (!string.IsNullOrEmpty(cityName)) pOverview.origin_city_name = cityName;
            }
            catch { }
        }

        private static void FillOriginFromFounderArchive(SurnameOverview pOverview)
        {
            if (pOverview == null || pOverview.founder_actor_id < 0) return;
            var db = DB;
            if (db == null) return;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT IFNULL(KINGDOM_ID, -1), IFNULL(KINGDOM_NAME, ''), IFNULL(KINGDOM_COLOR, ''), " +
                    $"IFNULL(CITY_ID, -1), IFNULL(CITY_NAME, '') " +
                    $"FROM {ActorArchiveTableItem.GetTableName()} WHERE ID=@id LIMIT 1";
                cmd.Parameters.AddWithValue("@id", pOverview.founder_actor_id);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                if (!r.Read()) return;

                long kingdomId = ToLong(r, 0, -1);
                string kingdomName = SafeStr(r, 1);
                string kingdomColor = SafeStr(r, 2);
                long cityId = ToLong(r, 3, -1);
                string cityName = SafeStr(r, 4);

                if (pOverview.origin_kingdom_id < 0 && kingdomId >= 0) pOverview.origin_kingdom_id = kingdomId;
                if (string.IsNullOrEmpty(pOverview.origin_kingdom_name)) pOverview.origin_kingdom_name = kingdomName;
                if (string.IsNullOrEmpty(pOverview.origin_kingdom_color)) pOverview.origin_kingdom_color = kingdomColor;
                if (pOverview.origin_city_id < 0 && cityId >= 0) pOverview.origin_city_id = cityId;
                if (string.IsNullOrEmpty(pOverview.origin_city_name)) pOverview.origin_city_name = cityName;
            }
            catch { }
        }

        private static void FillOriginFromLiveFounder(SurnameOverview pOverview)
        {
            if (pOverview == null || pOverview.founder_actor_id < 0) return;
            Actor founder = World.world?.units?.get(pOverview.founder_actor_id);
            if (founder?.data == null) return;

            City city = founder.city;
            Kingdom kingdom = founder.kingdom ?? city?.kingdom;
            if (city == null && kingdom?.capital?.data != null) city = kingdom.capital;

            if (pOverview.origin_kingdom_id < 0 && kingdom?.data != null) pOverview.origin_kingdom_id = kingdom.id;
            if (string.IsNullOrEmpty(pOverview.origin_kingdom_name) && kingdom?.data != null)
                pOverview.origin_kingdom_name = kingdom.name ?? "";
            if (string.IsNullOrEmpty(pOverview.origin_kingdom_color) && kingdom?.data != null)
                pOverview.origin_kingdom_color = HistoryColors.FromKingdom(kingdom);

            if (pOverview.origin_city_id < 0 && city?.data != null) pOverview.origin_city_id = city.data.id;
            if (string.IsNullOrEmpty(pOverview.origin_city_name) && city?.data != null)
                pOverview.origin_city_name = city.data.name ?? "";
        }

        private static void ResolveKingdomArchive(long pKingdomId, out string pName, out string pColor)
        {
            pName = "";
            pColor = "";
            if (pKingdomId < 0) return;

            var live = World.world?.kingdoms?.get(pKingdomId);
            if (live != null && !live.isRekt())
            {
                pName = live.name ?? "";
                pColor = HistoryColors.FromKingdom(live);
                return;
            }

            var db = DB;
            if (db == null) return;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT IFNULL(KINGDOM_NAME, ''), IFNULL(COLOR_TEXT, '') " +
                    $"FROM {KingdomArchiveTableItem.GetTableName()} WHERE KINGDOM_ID=@kid LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                if (!r.Read()) return;
                pName = SafeStr(r, 0);
                pColor = HistoryColors.Normalize(SafeStr(r, 1));
            }
            catch { }
        }

        private static string ResolveCityName(long pCityId)
        {
            if (pCityId < 0) return "";
            var live = World.world?.cities?.get(pCityId);
            if (live?.data != null && live.isAlive()) return live.data.name ?? "";

            var db = DB;
            if (db == null) return "";
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT IFNULL(SUBJECT_NAME, '') FROM {CityHistoryTableItem.GetTableName()} " +
                    $"WHERE CITY_ID=@cid ORDER BY WORLD_TIME ASC, EVENT_ID ASC LIMIT 1";
                cmd.Parameters.AddWithValue("@cid", pCityId);
                object o = cmd.ExecuteScalar();
                return o == null || o == System.DBNull.Value ? "" : o.ToString();
            }
            catch { return ""; }
        }

        // ─────────────────────── 某姓下的氏支列表 ───────────────────────

        public static List<ShiBranchInfo> GetShiBranches(string pFamilyName)
        {
            var result = new List<ShiBranchInfo>();
            var db = DB;
            if (db == null) return result;

            string shiTable = ShiBranchTableItem.GetTableName();
            string lineageTable = LineageGroupTableItem.GetTableName();

            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT SHI_ID, LINEAGE_ID, CLAN_NAME, SOURCE_TYPE, CREATED_TIME, FOUNDER_ACTOR_ID, " +
                $"IFNULL(ORIGIN_KINGDOM_ID, -1), IFNULL(ORIGIN_CITY_ID, -1) " +
                $"FROM {shiTable} WHERE LINEAGE_ID IN " +
                $"(SELECT LINEAGE_ID FROM {lineageTable} WHERE FAMILY_NAME=@f) " +
                $"ORDER BY CREATED_TIME ASC";
            cmd.Parameters.AddWithValue("@f", pFamilyName);

            using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    result.Add(new ShiBranchInfo
                    {
                        shi_id = reader.GetInt64(0),
                        lineage_id = reader.GetInt64(1),
                        clan_name = SafeStr(reader, 2),
                        source_type = SafeStr(reader, 3),
                        created_time = reader.GetDouble(4),
                        founder_actor_id = reader.GetInt64(5),
                        origin_kingdom_id = ToLong(reader, 6, -1),
                        origin_city_id = ToLong(reader, 7, -1)
                    });
                }
            }

            foreach (var s in result)
            {
                FillShiCounts(s);
                FillShiOrigin(s);
            }
            return result;
        }

        /// <summary>取某氏支的始祖 actor id(ShiBranch.FOUNDER_ACTOR_ID)。无则 -1。</summary>
        public static long GetShiBranchFounderId(long pShiId)
        {
            var db = DB;
            if (db == null) return -1;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT IFNULL(FOUNDER_ACTOR_ID, -1) FROM {ShiBranchTableItem.GetTableName()} WHERE SHI_ID=@s LIMIT 1";
            cmd.Parameters.AddWithValue("@s", pShiId);
            var o = cmd.ExecuteScalar();
            return o == null ? -1 : (long)o;
        }

        /// <summary>取某姓族总始祖 actor id(LineageGroup.FOUNDER_ACTOR_ID)。无则 -1。</summary>
        public static long GetLineageFounderId(long pLineageId)
        {
            var db = DB;
            if (db == null) return -1;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT IFNULL(FOUNDER_ACTOR_ID, -1) FROM {LineageGroupTableItem.GetTableName()} WHERE LINEAGE_ID=@l LIMIT 1";
            cmd.Parameters.AddWithValue("@l", pLineageId);
            var o = cmd.ExecuteScalar();
            return o == null ? -1 : (long)o;
        }

        /// <summary>取某氏支的 origin_kingdom_id(始祖建支时的国)。无则 -1。称王分封触发判定用。</summary>
        public static long GetShiOriginKingdom(long pShiId)
        {
            var db = DB;
            if (db == null) return -1;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT IFNULL(ORIGIN_KINGDOM_ID, -1) FROM {ShiBranchTableItem.GetTableName()} WHERE SHI_ID=@s LIMIT 1";
            cmd.Parameters.AddWithValue("@s", pShiId);
            var o = cmd.ExecuteScalar();
            return o == null ? -1 : (long)o;
        }

        public static int CountAliveInShi(long pShiId)
        {
            var db = DB;
            if (db == null || pShiId < 0) return 0;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT COUNT(*) FROM {ActorArchiveTableItem.GetTableName()} WHERE SHI_ID=@s AND IS_ALIVE=1";
            cmd.Parameters.AddWithValue("@s", pShiId);
            object o = cmd.ExecuteScalar();
            return o == null || o == System.DBNull.Value ? 0 : System.Convert.ToInt32(o);
        }

        /// <summary>取单个氏支信息(含统计)。无则 null。</summary>
        public static ShiBranchInfo GetShiBranchInfo(long pShiId)
        {
            var db = DB;
            if (db == null) return null;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT SHI_ID, LINEAGE_ID, CLAN_NAME, SOURCE_TYPE, CREATED_TIME, FOUNDER_ACTOR_ID, " +
                $"IFNULL(ORIGIN_KINGDOM_ID, -1), IFNULL(ORIGIN_CITY_ID, -1) " +
                $"FROM {ShiBranchTableItem.GetTableName()} WHERE SHI_ID=@s LIMIT 1";
            cmd.Parameters.AddWithValue("@s", pShiId);
            ShiBranchInfo info = null;
            using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    info = new ShiBranchInfo
                    {
                        shi_id = reader.GetInt64(0),
                        lineage_id = reader.GetInt64(1),
                        clan_name = SafeStr(reader, 2),
                        source_type = SafeStr(reader, 3),
                        created_time = reader.GetDouble(4),
                        founder_actor_id = reader.GetInt64(5),
                        origin_kingdom_id = ToLong(reader, 6, -1),
                        origin_city_id = ToLong(reader, 7, -1)
                    };
                }
            }
            if (info != null)
            {
                FillShiCounts(info);
                FillShiOrigin(info);
            }
            return info;
        }

        /// <summary>
        ///     兜底读取某 actor 作为始祖开创的称王分支。
        ///     有些旧档/时序里 ActorArchive.founded_branch_shi_id 没写上,但 ShiBranch 已经有
        ///     FOUNDER_ACTOR_ID + KING_FOUNDED 行;族谱显示"建支:X氏"必须以 ShiBranch 为最终事实。
        /// </summary>
        public static long GetKingFoundedBranchByFounder(long pActorId)
        {
            var db = DB;
            if (db == null || pActorId < 0) return -1;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT SHI_ID FROM {ShiBranchTableItem.GetTableName()} " +
                $"WHERE FOUNDER_ACTOR_ID=@actor AND SOURCE_TYPE=@source " +
                $"ORDER BY CREATED_TIME DESC, SHI_ID DESC LIMIT 1";
            cmd.Parameters.AddWithValue("@actor", pActorId);
            cmd.Parameters.AddWithValue("@source", ShiSourceType.KING_FOUNDED);
            object o = cmd.ExecuteScalar();
            return o == null || o == System.DBNull.Value ? -1 : System.Convert.ToInt64(o);
        }

        private static void FillShiCounts(ShiBranchInfo pShi)
        {
            var db = DB;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT COUNT(*), SUM(IS_ALIVE), " +
                $"SUM(CASE WHEN STATUS='{LineageStatus.NOBLE}' AND IS_ALIVE=1 THEN 1 ELSE 0 END) " +
                $"FROM {ActorArchiveTableItem.GetTableName()} WHERE SHI_ID=@s";
            cmd.Parameters.AddWithValue("@s", pShi.shi_id);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            if (reader.Read())
            {
                pShi.total = ToInt(reader, 0);
                pShi.alive = ToInt(reader, 1);
                pShi.noble = ToInt(reader, 2);
            }
        }

        private static void FillShiOrigin(ShiBranchInfo pShi)
        {
            if (pShi == null) return;
            FillShiOriginFromFounderArchive(pShi);
            FillShiOriginFromLiveFounder(pShi);

            ResolveKingdomArchive(pShi.origin_kingdom_id, out string kingdomName, out string kingdomColor);
            if (!string.IsNullOrEmpty(kingdomName)) pShi.origin_kingdom_name = kingdomName;
            if (!string.IsNullOrEmpty(kingdomColor)) pShi.origin_kingdom_color = kingdomColor;

            string cityName = ResolveCityName(pShi.origin_city_id);
            if (!string.IsNullOrEmpty(cityName)) pShi.origin_city_name = cityName;
        }

        private static void FillShiOriginFromFounderArchive(ShiBranchInfo pShi)
        {
            if (pShi == null || pShi.founder_actor_id < 0) return;
            var db = DB;
            if (db == null) return;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT IFNULL(DISPLAY_NAME, ''), IFNULL(KINGDOM_ID, -1), IFNULL(KINGDOM_NAME, ''), " +
                    $"IFNULL(KINGDOM_COLOR, ''), IFNULL(CITY_ID, -1), IFNULL(CITY_NAME, '') " +
                    $"FROM {ActorArchiveTableItem.GetTableName()} WHERE ID=@id LIMIT 1";
                cmd.Parameters.AddWithValue("@id", pShi.founder_actor_id);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                if (!r.Read()) return;

                string founderName = SafeStr(r, 0);
                long kingdomId = ToLong(r, 1, -1);
                string kingdomName = SafeStr(r, 2);
                string kingdomColor = SafeStr(r, 3);
                long cityId = ToLong(r, 4, -1);
                string cityName = SafeStr(r, 5);

                if (string.IsNullOrEmpty(pShi.founder_name)) pShi.founder_name = founderName;
                if (pShi.origin_kingdom_id < 0 && kingdomId >= 0) pShi.origin_kingdom_id = kingdomId;
                if (string.IsNullOrEmpty(pShi.origin_kingdom_name)) pShi.origin_kingdom_name = kingdomName;
                if (string.IsNullOrEmpty(pShi.origin_kingdom_color)) pShi.origin_kingdom_color = kingdomColor;
                if (pShi.origin_city_id < 0 && cityId >= 0) pShi.origin_city_id = cityId;
                if (string.IsNullOrEmpty(pShi.origin_city_name)) pShi.origin_city_name = cityName;
            }
            catch { }
        }

        private static void FillShiOriginFromLiveFounder(ShiBranchInfo pShi)
        {
            if (pShi == null || pShi.founder_actor_id < 0) return;
            Actor founder = World.world?.units?.get(pShi.founder_actor_id);
            if (founder?.data == null) return;

            City city = founder.city;
            Kingdom kingdom = founder.kingdom ?? city?.kingdom;
            if (city == null && kingdom?.capital?.data != null) city = kingdom.capital;

            if (string.IsNullOrEmpty(pShi.founder_name)) pShi.founder_name = founder.getName();
            if (pShi.origin_kingdom_id < 0 && kingdom?.data != null) pShi.origin_kingdom_id = kingdom.id;
            if (string.IsNullOrEmpty(pShi.origin_kingdom_name) && kingdom?.data != null)
                pShi.origin_kingdom_name = kingdom.name ?? "";
            if (string.IsNullOrEmpty(pShi.origin_kingdom_color) && kingdom?.data != null)
                pShi.origin_kingdom_color = HistoryColors.FromKingdom(kingdom);
            if (pShi.origin_city_id < 0 && city?.data != null) pShi.origin_city_id = city.data.id;
            if (string.IsNullOrEmpty(pShi.origin_city_name) && city?.data != null)
                pShi.origin_city_name = city.data.name ?? "";
        }

        // ─────────────────────── 成员列表(某姓 / 某氏支) ───────────────────────

        public static List<MemberInfo> GetSurnameMembers(string pFamilyName)
        {
            return ReadMembers("FAMILY_NAME=@k", "@k", pFamilyName);
        }

        public static List<MemberInfo> GetShiMembers(long pShiId)
        {
            return ReadMembers("SHI_ID=@k", "@k", pShiId);
        }

        private static List<MemberInfo> ReadMembers(string pWhere, string pParam, object pValue)
        {
            var result = new List<MemberInfo>();
            var db = DB;
            if (db == null) return result;

            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT ID, DISPLAY_NAME, FAMILY_NAME, CLAN_NAME, STATUS, SEX, IS_ALIVE, " +
                $"BIRTH_TIME, DEATH_TIME, KINGDOM_NAME, CITY_NAME, SHI_ID " +
                $"FROM {ActorArchiveTableItem.GetTableName()} WHERE {pWhere} " +
                $"ORDER BY BIRTH_TIME ASC";
            cmd.Parameters.AddWithValue(pParam, pValue);

            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new MemberInfo
                {
                    id = reader.GetInt64(0),
                    display_name = SafeStr(reader, 1),
                    family_name = SafeStr(reader, 2),
                    clan_name = SafeStr(reader, 3),
                    status = SafeStr(reader, 4),
                    sex = ToInt(reader, 5),
                    is_alive = ToInt(reader, 6) != 0,
                    birth_time = reader.GetDouble(7),
                    death_time = reader.GetDouble(8),
                    kingdom_name = SafeStr(reader, 9),
                    city_name = SafeStr(reader, 10),
                    shi_id = reader.GetInt64(11)
                });
            }

            return result;
        }

        // ─────────────────────── 家族树(三层) ───────────────────────

        /// <summary>以 centerActorId 为中心,返回三层节点(父母 / 本人 / 子女)。死者用 SQL 档案。</summary>
        public static FamilyTreeNode GetFamilyTree(long pCenterActorId)
        {
            var center = BuildNode(pCenterActorId);
            if (center == null) return null;

            // 父母:用 FamilyEdge 反查(child=center 的 parent),活人优先 actor,死人查档案。
            //   BuildNode 失败(父母非 Xia / 无档案)时补**占位节点**,保证上溯链不断(用户报"往上查不到父母")。
            foreach (var pid in GetParentIds(pCenterActorId))
            {
                var pn = BuildNode(pid) ?? BuildPlaceholderNode(pid);
                if (pn != null) center.parents.Add(pn);
            }

            // 子女:FamilyEdge 正查(parent=center 的 child)
            foreach (var cid in GetChildIds(pCenterActorId))
            {
                var cn = BuildNode(cid);
                if (cn != null) center.children.Add(cn);
            }

            return center;
        }

        public static List<long> GetParentIds(long pChildId)
        {
            var ids = new List<long>();
            var db = DB;
            if (db == null) return ids;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT PARENT_ID FROM {FamilyEdgeTableItem.GetTableName()} WHERE CHILD_ID=@c AND PARENT_ID>=0";
            cmd.Parameters.AddWithValue("@c", pChildId);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            while (reader.Read()) ids.Add(reader.GetInt64(0));
            return ids;
        }

        public static List<long> GetChildIds(long pParentId)
        {
            var ids = new List<long>();
            var db = DB;
            if (db == null) return ids;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT CHILD_ID FROM {FamilyEdgeTableItem.GetTableName()} WHERE PARENT_ID=@p";
            cmd.Parameters.AddWithValue("@p", pParentId);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            while (reader.Read()) ids.Add(reader.GetInt64(0));
            return ids;
        }

        public static int CountKnownChildren(Actor pParent)
        {
            if (pParent?.data == null) return 0;
            var ids = new HashSet<long>(GetChildIds(pParent.data.id));
            try
            {
                foreach (var child in pParent.getChildren(pOnlyCurrentFamily: false))
                {
                    if (child?.data != null) ids.Add(child.data.id);
                }
            }
            catch { }
            return ids.Count;
        }

        public static string GetActorDisplayName(long pActorId)
        {
            var live = World.world?.units?.get(pActorId);
            if (live?.data != null)
            {
                live.data.get("display_name", out string display, "");
                return string.IsNullOrEmpty(display) ? live.getName() : display;
            }

            var row = LineageArchiveReader.ReadRow(pActorId);
            if (row == null) return "";
            return string.IsNullOrEmpty(row.display_name) ? (row.given_name ?? "") : row.display_name;
        }

        /// <summary>
        ///     氏族大树折叠探测:只看 pNodeId 的**直接子代一层**(不递归全树),返回是否有子代/有活人/有重要人物。
        ///     用于决定该节点默认是否折叠:全死 OR 无 king/leader/heir → 自动折叠(用户定调:省性能,折叠的不展开)。
        ///     轻量:只取直接子代 id + 对活人查运行时职业标记;死人只算 archive 行(不算重要,只算"存在")。
        /// </summary>
        public static BranchProbe ProbeBranch(long pNodeId)
        {
            var probe = new BranchProbe();
            var childIds = GetChildIds(pNodeId);
            if (childIds.Count == 0) return probe;

            var units = World.world?.units;
            foreach (long cid in childIds)
            {
                var live = units?.get(cid);
                bool liveValid = live != null && !live.isRekt() && live.isAlive();

                // 平民/奴隶不进氏族大树 → 探测里也跳过(否则会显示展开+号却展开为空)。
                string status = liveValid ? GetLiveStatus(live) : GetArchivedStatus(cid);
                if (status == LineageStatus.COMMON || status == LineageStatus.SLAVE) continue;

                probe.has_children = true; // 至少有一个大树可见(非平民)子代

                if (liveValid)
                {
                    probe.any_alive = true;
                    probe.any_descendant_alive = true;
                    live.data.get(LineageKeys.IS_HEIR, out bool isHeir, false);
                    if (live.isKing() || live.isCityLeader() || isHeir)
                    {
                        probe.any_important = true;
                        return probe; // 已确认重要,提前返回
                    }
                }
            }
            if (!probe.any_descendant_alive)
                probe.any_descendant_alive = HasAliveDescendant(pNodeId);
            return probe;
        }

        public static bool HasAliveDescendant(long pNodeId)
        {
            var db = DB;
            if (db == null || pNodeId < 0) return false;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"WITH RECURSIVE descendants(ID) AS (" +
                    $"SELECT CHILD_ID FROM {FamilyEdgeTableItem.GetTableName()} WHERE PARENT_ID=@id " +
                    $"UNION SELECT e.CHILD_ID FROM {FamilyEdgeTableItem.GetTableName()} e " +
                    $"JOIN descendants d ON e.PARENT_ID=d.ID) " +
                    $"SELECT EXISTS(SELECT 1 FROM descendants d " +
                    $"JOIN {ActorArchiveTableItem.GetTableName()} a ON a.ID=d.ID " +
                    $"WHERE a.IS_ALIVE=1 AND IFNULL(a.STATUS,'')<>@common AND IFNULL(a.STATUS,'')<>@slave LIMIT 1)";
                cmd.Parameters.AddWithValue("@id", pNodeId);
                cmd.Parameters.AddWithValue("@common", LineageStatus.COMMON);
                cmd.Parameters.AddWithValue("@slave", LineageStatus.SLAVE);
                object o = cmd.ExecuteScalar();
                return o != null && o != System.DBNull.Value && System.Convert.ToInt64(o) != 0;
            }
            catch { return false; }
        }

        private static string GetLiveStatus(Actor pLive)
        {
            pLive.data.get(LineageKeys.LINEAGE_STATUS, out string st, LineageStatus.NONE);
            return st;
        }

        /// <summary>轻量取档案 status(死者/不在场用,只读一列)。无则 none。</summary>
        private static string GetArchivedStatus(long pId)
        {
            var db = DB;
            if (db == null) return LineageStatus.NONE;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText = $"SELECT IFNULL(STATUS,'{LineageStatus.NONE}') FROM {ActorArchiveTableItem.GetTableName()} WHERE ID=@id LIMIT 1";
            cmd.Parameters.AddWithValue("@id", pId);
            var o = cmd.ExecuteScalar();
            return o == null ? LineageStatus.NONE : o.ToString();
        }

        /// <summary>构造单个节点。活人优先用 actor 当前态,否则查档案。两路都填齐 UI 字段。</summary>
        private static FamilyTreeNode BuildNode(long pId)
        {
            var live = World.world?.units?.get(pId);
            if (live != null && LineageService.IsXia(live))
            {
                live.data.get("display_name", out string disp, "");
                live.data.get(LineageKeys.LINEAGE_STATUS, out string st, LineageStatus.NONE);
                live.data.get(LineageKeys.CLAN_NAME, out string clan, "");
                live.data.get(LineageKeys.SHI_ID, out long shi, -1L);
                // ⚠ NOBLE_DISTANCE 是用 set(key,int) 写入的(LineageService:111),必须 get<int> 读,
                //   用 get<long> 会类型失配返默认 99 → 活人 tooltip 永远不显示"距贵族N代"(用户报"只有死人有")。
                live.data.get(LineageKeys.NOBLE_DISTANCE, out int nd, 99);
                var node = new FamilyTreeNode
                {
                    id = pId,
                    display_name = string.IsNullOrEmpty(disp) ? live.getName() : disp,
                    sex = live.isSexMale() ? 0 : 1,
                    is_alive = true,
                    status = st,
                    clan_name = clan,
                    shi_id = shi,
                    noble_distance = nd,
                    birth_time = live.data.created_time,
                    death_time = -1,
                    kingdom_id = live.kingdom?.id ?? -1,
                    kingdom_name = live.kingdom?.name ?? "",
                    kingdom_color = live.kingdom?.getColor()?.color_text ?? "",
                    original_clan_id = live.clan?.data?.id ?? -1,
                    city_name = live.city?.data?.name ?? "",
                    head = live.data.head,
                    phenotype_index = live.data.phenotype_index,
                    phenotype_shade = live.data.phenotype_shade,
                    death_cause = ReadLiveDeathCause(live),
                    founded_branch_shi_id = ResolveFoundedBranch(live, pId, ReadLiveFoundedBranch(live))
                };
                ApplyFoundedBranchDisplay(node, live);
                FillKingdomFlagSnapshot(node);
                FillLiveClanFlagSnapshot(node, live.clan);
                return node;
            }

            var row = LineageArchiveReader.ReadRow(pId);
            if (row == null) return null;
            var archived = new FamilyTreeNode
            {
                id = pId,
                display_name = string.IsNullOrEmpty(row.display_name) ? row.given_name : row.display_name,
                sex = row.sex,
                is_alive = row.is_alive != 0,
                status = row.status,
                clan_name = row.clan_name ?? "",
                shi_id = row.shi_id,
                noble_distance = row.noble_distance,
                birth_time = row.birth_time,
                death_time = row.death_time,
                kingdom_id = row.kingdom_id,
                kingdom_name = row.kingdom_name ?? "",
                kingdom_color = row.kingdom_color ?? "",
                original_clan_id = row.original_clan_id,
                city_name = row.city_name ?? "",
                head = row.head,
                skin = row.skin,
                skin_set = row.skin_set,
                phenotype_index = row.phenotype_index,
                phenotype_shade = row.phenotype_shade,
                clan_color_text = row.clan_color_text ?? "",
                clan_color_id = row.clan_color_id,
                clan_banner_icon_id = row.clan_banner_icon_id,
                clan_banner_background_id = row.clan_banner_background_id,
                death_cause = row.death_cause ?? "",
                founded_branch_shi_id = ResolveFoundedBranch(null, pId, row.founded_branch_shi_id)
            };
            ApplyFoundedBranchDisplay(archived, null);
            FillKingdomFlagSnapshot(archived);
            return archived;
        }

        /// <summary>父母占位节点:BuildNode 失败(非 Xia / 无档案)时,用 live actor 最小信息建节点,保证上溯链不断。
        /// 无 live actor(纯陌生 id)则返 null。</summary>
        private static FamilyTreeNode BuildPlaceholderNode(long pId)
        {
            var live = World.world?.units?.get(pId);
            if (live == null) return null;
            var node = new FamilyTreeNode
            {
                id = pId,
                display_name = live.getName(),
                sex = live.isSexMale() ? 0 : 1,
                is_alive = !live.isRekt(),
                status = LineageStatus.NONE,
                clan_name = "",
                shi_id = -1,
                noble_distance = 99,
                birth_time = live.data.created_time,
                death_time = -1,
                kingdom_id = live.kingdom?.id ?? -1,
                kingdom_name = live.kingdom?.name ?? "",
                kingdom_color = live.kingdom?.getColor()?.color_text ?? "",
                original_clan_id = live.clan?.data?.id ?? -1,
                city_name = live.city?.data?.name ?? "",
                head = live.data.head,
                phenotype_index = live.data.phenotype_index,
                phenotype_shade = live.data.phenotype_shade
            };
            FillKingdomFlagSnapshot(node);
            FillLiveClanFlagSnapshot(node, live.clan);
            return node;
        }

        /// <summary>读活人 actor.data 上的"称王分封新支 id"(无则 -1)。</summary>
        private static long ReadLiveFoundedBranch(Actor pLive)
        {
            pLive.data.get(LineageKeys.FOUNDED_BRANCH_SHI_ID, out long shi, -1L);
            return shi;
        }

        private static string ReadLiveDeathCause(Actor pLive)
        {
            if (pLive?.data == null) return "";
            pLive.data.get(LineageKeys.DEATH_CAUSE, out string cause, "");
            return cause ?? "";
        }

        private static long ResolveFoundedBranch(Actor pLive, long pActorId, long pStoredShi)
        {
            if (pStoredShi >= 0) return pStoredShi;

            long fallback = GetKingFoundedBranchByFounder(pActorId);
            if (fallback >= 0 && pLive?.data != null)
                pLive.data.set(LineageKeys.FOUNDED_BRANCH_SHI_ID, fallback);
            return fallback;
        }

        private static void ApplyFoundedBranchDisplay(FamilyTreeNode pNode, Actor pLive)
        {
            if (pNode == null || pNode.founded_branch_shi_id < 0) return;
            ShiBranchInfo info = GetShiBranchInfo(pNode.founded_branch_shi_id);
            if (info == null || info.founder_actor_id != pNode.id) return;

            pNode.shi_id = info.shi_id;
            if (!string.IsNullOrEmpty(info.clan_name))
                pNode.clan_name = info.clan_name;

            if (pLive?.data == null) return;
            pLive.data.set(LineageKeys.SHI_ID, info.shi_id);
            if (!string.IsNullOrEmpty(info.clan_name))
                pLive.data.set(LineageKeys.CLAN_NAME, info.clan_name);
            pLive.data.set(LineageKeys.FOUNDED_BRANCH_SHI_ID, info.shi_id);
            LineageService.ApplyDisplayName(pLive);
            LineageService.ArchiveActor(pLive, pAlive: true);
            pLive.clearGraphicsFully();
        }

        private static void FillLiveClanFlagSnapshot(FamilyTreeNode pNode, Clan pClan)
        {
            if (pNode == null || pClan?.data == null) return;
            pNode.clan_color_text = pClan.getColor()?.color_text ?? "";
            pNode.clan_color_id = pClan.data.color_id;
            pNode.clan_banner_icon_id = pClan.data.banner_icon_id;
            pNode.clan_banner_background_id = pClan.data.banner_background_id;
        }

        private static void FillKingdomFlagSnapshot(FamilyTreeNode pNode)
        {
            if (pNode == null || pNode.kingdom_id < 0) return;
            var db = DB;
            if (db == null) return;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    "SELECT COLOR_TEXT, COLOR_ID, BANNER_ICON_ID, BANNER_BACKGROUND_ID, BANNER_ID " +
                    $"FROM {KingdomArchiveTableItem.GetTableName()} WHERE KINGDOM_ID=@kid LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pNode.kingdom_id);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                if (!r.Read()) return;
                string color = SafeStr(r, 0);
                if (!string.IsNullOrEmpty(color)) pNode.kingdom_color = color;
                pNode.kingdom_color_id = ToInt(r, 1);
                pNode.kingdom_banner_icon_id = ToInt(r, 2);
                pNode.kingdom_banner_background_id = ToInt(r, 3);
                pNode.kingdom_banner_id = SafeStr(r, 4);
            }
            catch { }
        }

        // ─────────────────────── helpers ───────────────────────

        private static int ToInt(SQLiteDataReader pReader, int pOrdinal)
        {
            if (pReader.IsDBNull(pOrdinal)) return 0;
            return (int)pReader.GetInt64(pOrdinal);
        }

        private static long ToLong(SQLiteDataReader pReader, int pOrdinal, long pDefault = 0)
        {
            return pReader.IsDBNull(pOrdinal) ? pDefault : pReader.GetInt64(pOrdinal);
        }

        private static double ToDouble(SQLiteDataReader pReader, int pOrdinal, double pDefault = 0)
        {
            return pReader.IsDBNull(pOrdinal) ? pDefault : pReader.GetDouble(pOrdinal);
        }

        private static string SafeStr(SQLiteDataReader pReader, int pOrdinal)
        {
            return pReader.IsDBNull(pOrdinal) ? "" : pReader.GetString(pOrdinal);
        }
    }
}
