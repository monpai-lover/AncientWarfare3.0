using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.naming;
using AncientWarfare3.ui;

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
        [System.ThreadStatic]
        private static SQLiteConnection _backgroundConnection;

        [System.ThreadStatic]
        private static bool _backgroundRead;

        private const string ShiBranchIdentitySelectColumns =
            "IFNULL(NAMING_PROFILE, 'xia'), " +
            "IFNULL(WESTERN_NAMING_TRADITION, ''), " +
            "IFNULL(ORIGIN_CITY_CHINESE_NAME, ''), " +
            "IFNULL(DISPLAY_STEM, '')";

        private static SQLiteConnection DB => _backgroundConnection ??
            (_backgroundRead ? null : LineageArchiveManager.Instance.OperatingDB);

        public static System.IDisposable EnterBackgroundRead(
            SQLiteConnection pConnection)
        {
            if (pConnection == null)
                throw new System.ArgumentNullException(nameof(pConnection));
            return new BackgroundReadScope(pConnection);
        }

        private sealed class BackgroundReadScope : System.IDisposable
        {
            private readonly SQLiteConnection _previousConnection;
            private readonly bool _previousBackgroundRead;

            public BackgroundReadScope(SQLiteConnection pConnection)
            {
                _previousConnection = _backgroundConnection;
                _previousBackgroundRead = _backgroundRead;
                _backgroundConnection = pConnection;
                _backgroundRead = true;
            }

            public void Dispose()
            {
                _backgroundConnection = _previousConnection;
                _backgroundRead = _previousBackgroundRead;
            }
        }

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

        /// <summary>按城市聚合姓族概览。默认总览用这个视角,姓氏合流后也能按聚落查看氏支分布。</summary>
        public static List<SurnameOverview> GetCityLineageOverview()
        {
            var result = new List<SurnameOverview>();
            var db = DB;
            if (db == null) return result;

            var byCity = new Dictionary<long, SurnameOverview>();
            var familiesByCity = new Dictionary<long, HashSet<long>>();
            foreach (var branch in ReadShiBranches(""))
            {
                FillShiCounts(branch);
                FillShiOrigin(branch);
                BackfillShiOrigin(branch);
                if (branch.origin_city_id < 0) continue;

                if (!byCity.TryGetValue(branch.origin_city_id, out var overview))
                {
                    overview = new SurnameOverview
                    {
                        is_city_overview = true,
                        city_id = branch.origin_city_id,
                        city_name = branch.origin_city_name,
                        city_kingdom_id = branch.origin_kingdom_id,
                        city_kingdom_name = branch.origin_kingdom_name,
                        city_kingdom_color = branch.origin_kingdom_color,
                        earliest_time = branch.created_time,
                        created_time = branch.created_time
                    };
                    byCity.Add(branch.origin_city_id, overview);
                    familiesByCity.Add(branch.origin_city_id, new HashSet<long>());
                }

                overview.total += branch.total;
                overview.alive += branch.alive;
                overview.noble += branch.noble;
                overview.shi_count++;
                if (overview.earliest_time <= 0 || branch.created_time < overview.earliest_time)
                {
                    overview.earliest_time = branch.created_time;
                    overview.created_time = branch.created_time;
                }
                if (string.IsNullOrEmpty(overview.city_name)) overview.city_name = branch.origin_city_name;
                if (overview.city_kingdom_id < 0) overview.city_kingdom_id = branch.origin_kingdom_id;
                if (string.IsNullOrEmpty(overview.city_kingdom_name)) overview.city_kingdom_name = branch.origin_kingdom_name;
                if (string.IsNullOrEmpty(overview.city_kingdom_color)) overview.city_kingdom_color = branch.origin_kingdom_color;
                familiesByCity[branch.origin_city_id].Add(branch.lineage_id);
            }

            foreach (var pair in byCity)
            {
                pair.Value.family_count = familiesByCity.TryGetValue(pair.Key, out var families) ? families.Count : 0;
                FillCityOverviewMeta(pair.Value);
                result.Add(pair.Value);
            }
            result.Sort((a, b) =>
            {
                int alive = b.alive.CompareTo(a.alive);
                if (alive != 0) return alive;
                int total = b.total.CompareTo(a.total);
                if (total != 0) return total;
                return string.Compare(a.city_name ?? "", b.city_name ?? "", System.StringComparison.Ordinal);
            });
            return result;
        }

        private static void FillCityOverviewMeta(SurnameOverview pOverview)
        {
            if (pOverview == null || pOverview.city_id < 0) return;

            City liveCity = World.world?.cities?.get(pOverview.city_id);
            if (liveCity?.data != null && liveCity.isAlive())
            {
                pOverview.city_name = liveCity.data.name ?? "";
                Kingdom kingdom = liveCity.kingdom;
                if (kingdom?.data != null && !kingdom.isRekt())
                {
                    pOverview.city_kingdom_id = kingdom.id;
                    pOverview.city_kingdom_name = kingdom.name ?? "";
                    pOverview.city_kingdom_color = HistoryColors.FromKingdom(kingdom);
                    return;
                }
            }

            string resolvedCity = ResolveCityName(pOverview.city_id);
            if (!string.IsNullOrEmpty(resolvedCity)) pOverview.city_name = resolvedCity;

            FillCityKingdomFromArchive(pOverview);
            ResolveKingdomArchive(pOverview.city_kingdom_id, out string kingdomName, out string kingdomColor);
            if (!string.IsNullOrEmpty(kingdomName)) pOverview.city_kingdom_name = kingdomName;
            if (!string.IsNullOrEmpty(kingdomColor)) pOverview.city_kingdom_color = kingdomColor;
        }

        private static void FillCityKingdomFromArchive(SurnameOverview pOverview)
        {
            var db = DB;
            if (db == null || pOverview == null || pOverview.city_id < 0) return;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT IFNULL(CITY_NAME, ''), IFNULL(KINGDOM_ID, -1), IFNULL(KINGDOM_NAME, ''), IFNULL(KINGDOM_COLOR, '') " +
                    $"FROM {ActorArchiveTableItem.GetTableName()} WHERE CITY_ID=@cid " +
                    $"ORDER BY IS_ALIVE DESC, BIRTH_TIME DESC, ID DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@cid", pOverview.city_id);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                if (!r.Read()) return;

                string cityName = SafeStr(r, 0);
                long kingdomId = ToLong(r, 1, -1);
                string kingdomName = SafeStr(r, 2);
                string kingdomColor = SafeStr(r, 3);

                if (string.IsNullOrEmpty(pOverview.city_name)) pOverview.city_name = cityName;
                if (pOverview.city_kingdom_id < 0 && kingdomId >= 0) pOverview.city_kingdom_id = kingdomId;
                if (string.IsNullOrEmpty(pOverview.city_kingdom_name)) pOverview.city_kingdom_name = kingdomName;
                if (string.IsNullOrEmpty(pOverview.city_kingdom_color)) pOverview.city_kingdom_color = kingdomColor;
            }
            catch { }
        }

        private static int CountShiOfSurname(string pFamilyName, string pLineageTable, string pShiTable)
        {
            var db = DB;
            using var cmd = new SQLiteCommand(db);
            string actorTable = ActorArchiveTableItem.GetTableName();
            cmd.CommandText =
                $"SELECT COUNT(*) FROM {pShiTable} sb WHERE sb.LINEAGE_ID IN " +
                $"(SELECT LINEAGE_ID FROM {pLineageTable} WHERE FAMILY_NAME=@f) " +
                $"OR EXISTS (SELECT 1 FROM {actorTable} aa " +
                $"WHERE aa.SHI_ID=sb.SHI_ID AND aa.FAMILY_NAME=@f)";
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
                pOverview.origin_city_name = city.name ?? "";
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
                string name = o == null || o == System.DBNull.Value ? "" : o.ToString();
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { }

            return ResolveCityNameFromActorArchive(pCityId);
        }

        private static string ResolveCityNameFromActorArchive(long pCityId)
        {
            if (pCityId < 0) return "";
            var db = DB;
            if (db == null) return "";
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT IFNULL(CITY_NAME, '') FROM {ActorArchiveTableItem.GetTableName()} " +
                    $"WHERE CITY_ID=@cid AND IFNULL(CITY_NAME, '')<>'' " +
                    $"ORDER BY IS_ALIVE DESC, BIRTH_TIME ASC, ID ASC LIMIT 1";
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
            string actorTable = ActorArchiveTableItem.GetTableName();

            using var cmd = new SQLiteCommand(db);
            // A patrilineal surname edit changes only the affected actor
            // records; the shared LineageGroup keeps its original family.
            // Recover branches through renamed archive members as well.
            cmd.CommandText =
                $"SELECT SHI_ID, LINEAGE_ID, CLAN_NAME, SOURCE_TYPE, CREATED_TIME, FOUNDER_ACTOR_ID, " +
                $"IFNULL(ORIGIN_KINGDOM_ID, -1), IFNULL(ORIGIN_CITY_ID, -1), " +
                $"IFNULL(PARENT_SHI_ID, -1), IFNULL(STATE_NAME, ''), " +
                $"IFNULL(STATE_NAME_SOURCE, ''), IFNULL(STATE_NAME_DECIDED_TIME, -1), " +
                $"{ShiBranchIdentitySelectColumns} " +
                $"FROM {shiTable} sb WHERE sb.LINEAGE_ID IN " +
                $"(SELECT LINEAGE_ID FROM {lineageTable} WHERE FAMILY_NAME=@f) " +
                $"OR EXISTS (SELECT 1 FROM {actorTable} aa " +
                $"WHERE aa.SHI_ID=sb.SHI_ID AND aa.FAMILY_NAME=@f) " +
                $"ORDER BY CREATED_TIME ASC";
            cmd.Parameters.AddWithValue("@f", pFamilyName);

            using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    result.Add(ReadShiBranchInfo(reader));
                }
            }

            foreach (var s in result)
            {
                FillShiCounts(s);
                FillShiOrigin(s);
            }
            return result;
        }

        public static List<ShiBranchInfo> GetShiBranchesByCity(long pCityId)
        {
            var result = new List<ShiBranchInfo>();
            var db = DB;
            if (db == null || pCityId < 0) return result;

            foreach (var s in ReadShiBranches(""))
            {
                FillShiCounts(s);
                FillShiOrigin(s);
                BackfillShiOrigin(s);
                if (s.origin_city_id == pCityId) result.Add(s);
            }
            result.Sort((a, b) =>
            {
                int clan = string.Compare(a.clan_name ?? "", b.clan_name ?? "", System.StringComparison.Ordinal);
                if (clan != 0) return clan;
                return a.created_time.CompareTo(b.created_time);
            });
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
        public static long GetActorShiId(long pActorId)
        {
            if (pActorId < 0) return -1;
            var live = World.world?.units?.get(pActorId);
            if (live?.data != null)
            {
                live.data.get(LineageKeys.SHI_ID, out long liveShiId, -1L);
                if (liveShiId >= 0) return liveShiId;
            }

            var row = LineageArchiveReader.ReadRow(pActorId);
            return row != null ? row.shi_id : -1;
        }

        public static List<long> GetAncestorPathToFounder(long pActorId, long pFounderId)
        {
            var path = new List<long>();
            if (pActorId < 0 || pFounderId < 0) return path;

            var visited = new HashSet<long>();
            if (!FindAncestorPathToFounder(pActorId, pFounderId, visited, path, 0, 96))
            {
                path.Clear();
                return path;
            }

            path.Reverse();
            return path;
        }

        public static List<long> GetAgnaticPathToAncestor(long pActorId, long pAncestorId)
        {
            return FamilyTreeRelationRules.BuildAgnaticPath(pActorId, pAncestorId, GetFatherId);
        }

        public static long GetEarliestReachableAgnaticAncestor(long pActorId)
        {
            if (pActorId < 0) return -1L;
            var visited = new HashSet<long>();
            long current = pActorId;
            for (int depth = 0; depth <= 96; depth++)
            {
                if (!visited.Add(current)) return current;
                long father = GetFatherId(current);
                if (father < 0 || father == current) return current;
                current = father;
            }
            return current;
        }

        public static int GetTreeGenerationInShi(long pActorId, long pShiId)
        {
            if (pActorId < 0) return 0;
            if (pShiId < 0) pShiId = GetActorShiId(pActorId);
            long founder = GetShiBranchFounderId(pShiId);
            if (founder < 0) return 0;
            var path = GetAncestorPathToFounder(pActorId, founder);
            return path.Count;
        }

        private static bool FindAncestorPathToFounder(long pCurrentId, long pFounderId,
            HashSet<long> pVisited, List<long> pPath, int pDepth, int pMaxDepth)
        {
            if (pDepth > pMaxDepth || !pVisited.Add(pCurrentId)) return false;
            pPath.Add(pCurrentId);
            if (pCurrentId == pFounderId) return true;

            foreach (long parentId in GetParentIds(pCurrentId))
            {
                if (FindAncestorPathToFounder(parentId, pFounderId, pVisited, pPath, pDepth + 1, pMaxDepth))
                    return true;
            }

            pPath.RemoveAt(pPath.Count - 1);
            return false;
        }

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

        /// <summary>取角色性别(0=男/1=女);先看在场 actor,再回退档案,查不到返回 -1。</summary>
        public static int GetActorSex(long pActorId)
        {
            if (pActorId < 0) return -1;
            var live = World.world?.units?.get(pActorId);
            if (live?.data != null) return live.isSexMale() ? 0 : 1;
            return LineageArchiveReader.GetSex(pActorId);
        }

        /// <summary>取角色所属姓(LINEAGE_ID);先看在场 actor,再回退档案。</summary>
        public static long GetActorLineageId(long pActorId)
        {
            if (pActorId < 0) return -1L;
            var live = World.world?.units?.get(pActorId);
            if (live?.data != null)
            {
                live.data.get(LineageKeys.LINEAGE_ID, out long liveLineage, -1L);
                if (liveLineage >= 0) return liveLineage;
            }
            var row = LineageArchiveReader.ReadRow(pActorId);
            return row?.lineage_id ?? -1L;
        }

        /// <summary>取父亲 id(父母中的男性),没有则 -1。</summary>
        public static long GetFatherId(long pActorId)
        {
            if (pActorId < 0) return -1L;
            foreach (long parentId in GetParentIds(pActorId))
            {
                if (parentId < 0) continue;
                if (GetActorSex(parentId) == 0) return parentId;
            }
            return -1L;
        }

        public static long GetMotherId(long pActorId)
        {
            if (pActorId < 0) return -1L;
            foreach (long parentId in GetParentIds(pActorId))
            {
                if (parentId >= 0 && GetActorSex(parentId) != 0)
                    return parentId;
            }
            return -1L;
        }

        public static bool HasHeldTitle(long pActorId)
        {
            if (pActorId < 0L) return false;
            LineageBulkSnapshot bulk = LineageBulkSnapshotContext.Current;
            if (bulk != null && bulk.ContainsNode(pActorId))
                return bulk.HasHeldTitle(pActorId);

            Actor live = World.world?.units?.get(pActorId);
            if (live?.data != null && !live.isRekt())
            {
                if (live.isKing()) return true;
                if (NobleRankService.ReadHot(live).Rank >
                    NobleRankRules.RankNone) return true;
            }

            var db = DB;
            if (db == null) return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText =
                    "SELECT CASE WHEN EXISTS (SELECT 1 FROM " +
                    KingdomReignTableItem.GetTableName() +
                    " WHERE KING_ACTOR_ID=@actor) OR EXISTS (SELECT 1 FROM " +
                    EnfeoffmentTableItem.GetTableName() +
                    " WHERE ACTOR_ID=@actor AND NOBLE_RANK>0) " +
                    "THEN 1 ELSE 0 END";
                command.Parameters.AddWithValue("@actor", pActorId);
                return System.Convert.ToInt32(command.ExecuteScalar()) != 0;
            }
            catch (SQLiteException)
            {
                return false;
            }
        }

        private static NamingProfileId GetBigTreeProfile(long pActorId)
        {
            LineageBulkSnapshot bulk = LineageBulkSnapshotContext.Current;
            if (bulk != null) return bulk.BigTreeProfile;
            long shiId = GetActorShiId(pActorId);
            ShiBranchInfo branch = shiId >= 0L
                ? GetShiBranchInfo(shiId)
                : null;
            return AWCultureNamingTraditionRules.ParseProfile(
                branch?.naming_profile);
        }

        /// <summary>
        ///     是否为该姓(LINEAGE_ID)的男系(父→父→…同姓)后裔。氏(SHI/分支)可不同,不受限。
        ///     沿途任一父亲出现异姓即判否;男系链一路同姓到始祖或链根则判是。
        /// </summary>
        public static bool IsAgnaticDescendant(long pActorId, long pLegitimateLineage)
        {
            if (pActorId < 0 || pLegitimateLineage < 0) return false;
            if (GetActorLineageId(pActorId) != pLegitimateLineage) return false;

            long founderId = GetLineageFounderId(pLegitimateLineage);
            var visited = new HashSet<long>();
            long current = pActorId;
            for (int depth = 0; depth <= 96; depth++)
            {
                if (founderId >= 0 && current == founderId) return true;
                if (!visited.Add(current)) return false;

                long fatherId = GetFatherId(current);
                if (fatherId < 0) return true; // 男系链到头,沿途皆本姓
                if (GetActorLineageId(fatherId) != pLegitimateLineage) return false; // 异姓父亲
                current = fatherId;
            }
            return false;
        }

        /// <summary>是否为 pAncestorId 的男系直系后裔(沿父亲链能走到该祖先)。本人不算自己的后裔。</summary>
        public static bool IsAgnaticDescendantOf(long pDescendantId, long pAncestorId)
        {
            if (pDescendantId < 0 || pAncestorId < 0 || pDescendantId == pAncestorId) return false;
            var visited = new HashSet<long>();
            long current = pDescendantId;
            for (int depth = 0; depth <= 96; depth++)
            {
                long fatherId = GetFatherId(current);
                if (fatherId < 0) return false;
                if (fatherId == pAncestorId) return true;
                if (!visited.Add(fatherId)) return false;
                current = fatherId;
            }
            return false;
        }

        /// <summary>
        ///     沿父系从 actor 往上走到本姓(LINEAGE_ID)男系链顶端,返回最顶端那个同姓祖先的 id。
        ///     用作 GetLineageFounderId 查不到始祖档案时的兜底锚点,使辈分深度计算不依赖 DB 始祖记录。
        ///     actor 自身非本姓或无效则返回 -1;链顶(父亲缺失或异姓)即为根。
        /// </summary>
        public static long GetAgnaticRootId(long pActorId, long pLegitimateLineage)
        {
            if (pActorId < 0 || pLegitimateLineage < 0) return -1L;
            if (GetActorLineageId(pActorId) != pLegitimateLineage) return -1L;
            var visited = new HashSet<long>();
            long current = pActorId;
            for (int depth = 0; depth <= 96; depth++)
            {
                if (!visited.Add(current)) return current;
                long fatherId = GetFatherId(current);
                if (fatherId < 0) return current;                              // 父系链到头 → 当前即根
                if (GetActorLineageId(fatherId) != pLegitimateLineage) return current; // 父异姓 → 当前即根
                current = fatherId;
            }
            return current;
        }

        /// <summary>
        ///     两人最近的共同父系祖先(沿父亲链,纯 parent 记录,不看 LINEAGE_ID)——"同源"判定的核心。
        ///     不受姓氏合流/开创分支改氏/始祖档案缺失影响。返回祖先 id 与各自到该祖先的代数;
        ///     无共同父系祖先返回 -1。其中一方是另一方的父系祖先时,该祖先即较年长的一方(对应代数为 0)。
        /// </summary>
        public static long NearestCommonAgnaticAncestor(long pIdA, long pIdB, out int pDepthA, out int pDepthB)
        {
            pDepthA = -1;
            pDepthB = -1;
            if (pIdA < 0 || pIdB < 0) return -1L;

            // A 的父系链:id → 到 A 的代数(含 A 自身 = 0)。
            var depthOfA = new Dictionary<long, int>();
            long cur = pIdA;
            for (int d = 0; d <= 96; d++)
            {
                if (!depthOfA.ContainsKey(cur)) depthOfA[cur] = d;
                long father = GetFatherId(cur);
                if (father < 0 || father == cur || depthOfA.ContainsKey(father)) break;
                cur = father;
            }

            // 沿 B 的父系链找首个落在 A 链上的节点 = 最近共同祖先。
            var visited = new HashSet<long>();
            cur = pIdB;
            for (int d = 0; d <= 96; d++)
            {
                if (depthOfA.TryGetValue(cur, out int da))
                {
                    pDepthA = da;
                    pDepthB = d;
                    return cur;
                }
                if (!visited.Add(cur)) break;
                long father = GetFatherId(cur);
                if (father < 0 || father == cur) break;
                cur = father;
            }
            return -1L;
        }

        /// <summary>
        ///     某个基准 actor 的父系祖先深度表(本人 = 0,父亲 = 1,……上限 96 代)。
        ///
        ///     上面那三个函数(NearestCommonAgnaticAncestor / IsAgnaticDescendantOf /
        ///     GetAgnaticDepth)每次调用都要重走一整条父系链并新建集合,而头衔继承的
        ///     候选人循环里基准是固定的、或者同一条链要被问两遍。走一次建表,之后
        ///     全是字典查询。
        ///
        ///     这里的 GetFatherId 每步还要 GetParentIds + 逐个 GetActorSex,所以省掉
        ///     的不只是分配。
        ///
        ///     由调用方持有、串行使用(Reset 后复用),不是共享全局状态。深度上限取
        ///     96,与 NearestCommonAgnaticAncestor / GetAgnaticDepth 一致;
        ///     IsAgnaticDescendantOf 原本能多走一步(97),实际父系链远不及此。
        /// </summary>
        public sealed class AgnaticAncestorDepths
        {
            private readonly Dictionary<long, int> _depths =
                new Dictionary<long, int>();
            private readonly HashSet<long> _scratch = new HashSet<long>();

            /// <summary>
            ///     父亲 id 的记忆表。候选人的父系链高度重叠(都收敛到王室那条线上),
            ///     而每一步 GetFatherId 都是 GetParentIds(两条 SQL)+ 逐个
            ///     GetActorSex。这个对象现在按「王国 + 参照君主」长期复用
            ///     (HeirService.GetKingAncestry),所以记忆表跨候选人、跨单位有效。
            ///
            ///     **只记正结果**:查不到父亲往往是暂时的(出生事务还没落库、
            ///     档案还没写),把 -1 记下来会让这一朝再也纠正不过来 —— 之前
            ///     "有胞弟却选不出继承人"就是父系链断的那一类故障。
            /// </summary>
            private readonly Dictionary<long, long> _fathers =
                new Dictionary<long, long>();

            private long CachedFather(long pActorId)
            {
                if (pActorId < 0L) return -1L;
                if (_fathers.TryGetValue(pActorId, out long cached))
                    return cached;
                long father = GetFatherId(pActorId);
                if (father >= 0L) _fathers[pActorId] = father;
                return father;
            }

            public long RootId { get; private set; } = -1L;
            public bool IsUsable => RootId >= 0L;

            public void Reset(long pRootId)
            {
                _depths.Clear();
                _fathers.Clear();
                RootId = -1L;
                if (pRootId < 0L) return;
                RootId = pRootId;
                long current = pRootId;
                for (int depth = 0; depth <= 96; depth++)
                {
                    if (!_depths.ContainsKey(current)) _depths[current] = depth;
                    long father = CachedFather(current);
                    if (father < 0L || father == current ||
                        _depths.ContainsKey(father)) break;
                    current = father;
                }
            }

            /// <summary>基准到 pAncestorId 的父系步数;不在链上返回 -1,基准本人为 0。</summary>
            public int DepthOf(long pAncestorId)
            {
                if (!IsUsable || pAncestorId < 0L) return -1;
                return _depths.TryGetValue(pAncestorId, out int depth)
                    ? depth
                    : -1;
            }

            /// <summary>基准是否 pAncestorId 的男系直系后裔(本人不算)。</summary>
            public bool IsStrictDescendantOf(long pAncestorId)
            {
                return DepthOf(pAncestorId) > 0;
            }

            /// <summary>基准与 pOtherId 的最近共同父系祖先。语义同静态版。</summary>
            public long NearestCommon(long pOtherId, out int pRootDepth,
                out int pOtherDepth)
            {
                pRootDepth = -1;
                pOtherDepth = -1;
                if (!IsUsable || pOtherId < 0L) return -1L;
                _scratch.Clear();
                long current = pOtherId;
                for (int depth = 0; depth <= 96; depth++)
                {
                    if (_depths.TryGetValue(current, out int rootDepth))
                    {
                        pRootDepth = rootDepth;
                        pOtherDepth = depth;
                        return current;
                    }
                    if (!_scratch.Add(current)) break;
                    long father = CachedFather(current);
                    if (father < 0L || father == current) break;
                    current = father;
                }
                return -1L;
            }
        }

        /// <summary>沿父系从 actor 到某祖先/始祖的步数(辈分深度);走不到返回 -1,祖先本人为 0。</summary>
        public static int GetAgnaticDepth(long pActorId, long pFounderId)
        {
            if (pActorId < 0 || pFounderId < 0) return -1;
            if (pActorId == pFounderId) return 0;
            var visited = new HashSet<long>();
            long current = pActorId;
            for (int steps = 1; steps <= 96; steps++)
            {
                long fatherId = GetFatherId(current);
                if (fatherId < 0) return -1;
                if (fatherId == pFounderId) return steps;
                if (!visited.Add(fatherId)) return -1;
                current = fatherId;
            }
            return -1;
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
                $"IFNULL(ORIGIN_KINGDOM_ID, -1), IFNULL(ORIGIN_CITY_ID, -1), " +
                $"IFNULL(PARENT_SHI_ID, -1), IFNULL(STATE_NAME, ''), " +
                $"IFNULL(STATE_NAME_SOURCE, ''), IFNULL(STATE_NAME_DECIDED_TIME, -1), " +
                $"{ShiBranchIdentitySelectColumns} " +
                $"FROM {ShiBranchTableItem.GetTableName()} WHERE SHI_ID=@s LIMIT 1";
            cmd.Parameters.AddWithValue("@s", pShiId);
            ShiBranchInfo info = null;
            using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    info = ReadShiBranchInfo(reader);
                }
            }
            if (info != null)
            {
                FillShiCounts(info);
                FillShiOrigin(info);
            }
            return info;
        }

        public static long GetParentShiId(long pShiId)
        {
            var db = DB;
            if (db == null || pShiId < 0) return -1;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT IFNULL(PARENT_SHI_ID, -1) FROM {ShiBranchTableItem.GetTableName()} " +
                "WHERE SHI_ID=@s LIMIT 1";
            cmd.Parameters.AddWithValue("@s", pShiId);
            object value = cmd.ExecuteScalar();
            return value == null || value == System.DBNull.Value ? -1 : System.Convert.ToInt64(value);
        }

        public static List<ShiBranchInfo> GetShiParentChain(long pShiId, int pMaxDepth = 64)
        {
            long[] parentIds = ShiBranchRules.TraceParents(pShiId, GetParentShiId, pMaxDepth);
            var result = new List<ShiBranchInfo>(parentIds.Length);
            foreach (long parentId in parentIds)
            {
                ShiBranchInfo parent = GetShiBranchInfo(parentId);
                if (parent != null) result.Add(parent);
            }
            return result;
        }

        public static ShiBranchInfo GetRootShiBranchInfo(long pLineageId)
        {
            var db = DB;
            if (db == null || pLineageId < 0) return null;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT SHI_ID, LINEAGE_ID, CLAN_NAME, SOURCE_TYPE, CREATED_TIME, FOUNDER_ACTOR_ID, " +
                $"IFNULL(ORIGIN_KINGDOM_ID, -1), IFNULL(ORIGIN_CITY_ID, -1), " +
                $"IFNULL(PARENT_SHI_ID, -1), IFNULL(STATE_NAME, ''), " +
                $"IFNULL(STATE_NAME_SOURCE, ''), IFNULL(STATE_NAME_DECIDED_TIME, -1), " +
                $"{ShiBranchIdentitySelectColumns} " +
                $"FROM {ShiBranchTableItem.GetTableName()} WHERE LINEAGE_ID=@l " +
                $"ORDER BY CREATED_TIME ASC, SHI_ID ASC LIMIT 1";
            cmd.Parameters.AddWithValue("@l", pLineageId);
            ShiBranchInfo info = null;
            using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    info = ReadShiBranchInfo(reader);
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
        ///     兜底读取某 actor 作为始祖开创的称王或藩王分支。
        ///     旧档可能未写 ActorArchive.founded_branch_shi_id，但 ShiBranch
        ///     已保留来源与创建者；族谱显示必须以该事实为准。
        /// </summary>
        public static long GetFoundedBranchByFounder(long pActorId)
        {
            return TryResolveOwnedFoundedBranch(pActorId, -1L,
                out long resolved) ? resolved : -1L;
        }

        internal static bool TryResolveOwnedFoundedBranch(long pActorId,
            long pStoredShiId, out long pResolvedShiId)
        {
            pResolvedShiId = pStoredShiId;
            var db = DB;
            return db != null && pActorId >= 0L &&
                   FoundedBranchRecoveryQuery.TryResolve(db, null, pActorId,
                       pStoredShiId, out pResolvedShiId);
        }

        private static void FillShiCounts(ShiBranchInfo pShi, long pCityId = -1)
        {
            var db = DB;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT COUNT(*), SUM(IS_ALIVE), " +
                $"SUM(CASE WHEN STATUS='{LineageStatus.NOBLE}' AND IS_ALIVE=1 THEN 1 ELSE 0 END) " +
                $"FROM {ActorArchiveTableItem.GetTableName()} WHERE SHI_ID=@s" +
                (pCityId >= 0 ? " AND CITY_ID=@cid" : "");
            cmd.Parameters.AddWithValue("@s", pShi.shi_id);
            if (pCityId >= 0) cmd.Parameters.AddWithValue("@cid", pCityId);
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
            FillShiOriginFromMemberArchive(pShi);

            ResolveKingdomArchive(pShi.origin_kingdom_id, out string kingdomName, out string kingdomColor);
            if (!string.IsNullOrEmpty(kingdomName)) pShi.origin_kingdom_name = kingdomName;
            if (!string.IsNullOrEmpty(kingdomColor)) pShi.origin_kingdom_color = kingdomColor;

            string cityName = ResolveCityName(pShi.origin_city_id);
            if (!string.IsNullOrEmpty(cityName)) pShi.origin_city_name = cityName;
        }

        private static List<ShiBranchInfo> ReadShiBranches(string pWhereSql)
        {
            var result = new List<ShiBranchInfo>();
            var db = DB;
            if (db == null) return result;

            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT SHI_ID, LINEAGE_ID, CLAN_NAME, SOURCE_TYPE, CREATED_TIME, FOUNDER_ACTOR_ID, " +
                $"IFNULL(ORIGIN_KINGDOM_ID, -1), IFNULL(ORIGIN_CITY_ID, -1), " +
                $"IFNULL(PARENT_SHI_ID, -1), IFNULL(STATE_NAME, ''), " +
                $"IFNULL(STATE_NAME_SOURCE, ''), IFNULL(STATE_NAME_DECIDED_TIME, -1), " +
                $"{ShiBranchIdentitySelectColumns} " +
                $"FROM {ShiBranchTableItem.GetTableName()} " +
                pWhereSql +
                $" ORDER BY CREATED_TIME ASC, SHI_ID ASC";

            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(ReadShiBranchInfo(reader));
            }
            return result;
        }

        private static ShiBranchInfo ReadShiBranchInfo(SQLiteDataReader pReader)
        {
            return new ShiBranchInfo
            {
                shi_id = pReader.GetInt64(0),
                lineage_id = pReader.GetInt64(1),
                clan_name = SafeStr(pReader, 2),
                source_type = SafeStr(pReader, 3),
                created_time = ToDouble(pReader, 4),
                founder_actor_id = ToLong(pReader, 5, -1),
                origin_kingdom_id = ToLong(pReader, 6, -1),
                origin_city_id = ToLong(pReader, 7, -1),
                parent_shi_id = ToLong(pReader, 8, -1),
                state_name = SafeStr(pReader, 9),
                state_name_source = SafeStr(pReader, 10),
                state_name_decided_time = ToDouble(pReader, 11, -1),
                naming_profile = SafeStr(pReader, 12),
                western_naming_tradition = SafeStr(pReader, 13),
                origin_city_chinese_name = SafeStr(pReader, 14),
                display_stem = SafeStr(pReader, 15)
            };
        }

        private static void BackfillShiOrigin(ShiBranchInfo pShi)
        {
            var db = DB;
            if (db == null || pShi == null || pShi.shi_id < 0) return;
            if (pShi.origin_city_id < 0 && pShi.origin_kingdom_id < 0) return;

            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"UPDATE {ShiBranchTableItem.GetTableName()} SET " +
                    $"ORIGIN_CITY_ID=CASE WHEN IFNULL(ORIGIN_CITY_ID, -1)<0 AND @cid>=0 THEN @cid ELSE ORIGIN_CITY_ID END, " +
                    $"ORIGIN_KINGDOM_ID=CASE WHEN IFNULL(ORIGIN_KINGDOM_ID, -1)<0 AND @kid>=0 THEN @kid ELSE ORIGIN_KINGDOM_ID END " +
                    $"WHERE SHI_ID=@sid";
                cmd.Parameters.AddWithValue("@cid", pShi.origin_city_id);
                cmd.Parameters.AddWithValue("@kid", pShi.origin_kingdom_id);
                cmd.Parameters.AddWithValue("@sid", pShi.shi_id);
                HistoricalContentRevision
                    .AdvanceAfterSuccessfulSynchronousWrite(
                        () => cmd.ExecuteNonQuery());
            }
            catch { }
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
                pShi.origin_city_name = city.name ?? "";
        }

        private static void FillShiOriginFromMemberArchive(ShiBranchInfo pShi)
        {
            if (pShi == null || pShi.shi_id < 0) return;
            if (pShi.origin_city_id >= 0 && !string.IsNullOrEmpty(pShi.origin_city_name)) return;
            var db = DB;
            if (db == null) return;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT IFNULL(KINGDOM_ID, -1), IFNULL(KINGDOM_NAME, ''), IFNULL(KINGDOM_COLOR, ''), " +
                    $"IFNULL(CITY_ID, -1), IFNULL(CITY_NAME, '') " +
                    $"FROM {ActorArchiveTableItem.GetTableName()} " +
                    $"WHERE SHI_ID=@sid AND (IFNULL(CITY_ID, -1)>=0 OR IFNULL(CITY_NAME, '')<>'') " +
                    $"ORDER BY IS_ALIVE DESC, BIRTH_TIME ASC, ID ASC LIMIT 1";
                cmd.Parameters.AddWithValue("@sid", pShi.shi_id);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                if (!r.Read()) return;

                long kingdomId = ToLong(r, 0, -1);
                string kingdomName = SafeStr(r, 1);
                string kingdomColor = SafeStr(r, 2);
                long cityId = ToLong(r, 3, -1);
                string cityName = SafeStr(r, 4);

                if (pShi.origin_kingdom_id < 0 && kingdomId >= 0) pShi.origin_kingdom_id = kingdomId;
                if (string.IsNullOrEmpty(pShi.origin_kingdom_name)) pShi.origin_kingdom_name = kingdomName;
                if (string.IsNullOrEmpty(pShi.origin_kingdom_color)) pShi.origin_kingdom_color = kingdomColor;
                if (pShi.origin_city_id < 0 && cityId >= 0) pShi.origin_city_id = cityId;
                if (string.IsNullOrEmpty(pShi.origin_city_name)) pShi.origin_city_name = cityName;
            }
            catch { }
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

        public static List<long> GetLivingShiMemberIds(long pShiId,
            int pLimit)
        {
            var result = new List<long>();
            var db = DB;
            if (db == null || pShiId < 0 || pLimit <= 0) return result;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText = "SELECT ID FROM " +
                ActorArchiveTableItem.GetTableName() +
                " WHERE SHI_ID=@shi AND IS_ALIVE=1 " +
                "ORDER BY BIRTH_TIME ASC,ID ASC LIMIT @limit";
            cmd.Parameters.AddWithValue("@shi", pShiId);
            cmd.Parameters.AddWithValue("@limit", pLimit);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            while (reader.Read()) result.Add(reader.GetInt64(0));
            return result;
        }

        public static List<long> GetLivingLineageMemberIds(long pLineageId,
            int pLimit)
        {
            var result = new List<long>();
            var db = DB;
            if (db == null || pLineageId < 0 || pLimit <= 0) return result;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText = "SELECT ID FROM " +
                ActorArchiveTableItem.GetTableName() +
                " WHERE LINEAGE_ID=@lineage AND IS_ALIVE=1 " +
                "ORDER BY BIRTH_TIME ASC,ID ASC LIMIT @limit";
            cmd.Parameters.AddWithValue("@lineage", pLineageId);
            cmd.Parameters.AddWithValue("@limit", pLimit);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            while (reader.Read()) result.Add(reader.GetInt64(0));
            return result;
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
            var center = GetFamilyTreeNode(pCenterActorId);
            if (center == null) return null;

            // 父母:用 FamilyEdge 反查(child=center 的 parent),活人优先 actor,死人查档案。
            //   BuildNode 失败(父母非 Xia / 无档案)时补**占位节点**,保证上溯链不断(用户报"往上查不到父母")。
            foreach (var pid in GetParentIds(pCenterActorId, pUseReverseLiveLookup: true))
            {
                var pn = BuildNode(pid) ?? BuildPlaceholderNode(pid);
                if (pn != null) center.parents.Add(pn);
            }

            // 子女:FamilyEdge 正查(parent=center 的 child)
            foreach (var cid in GetChildIds(pCenterActorId))
            {
                var cn = BuildNode(cid) ?? BuildPlaceholderNode(cid);
                if (cn != null) center.children.Add(cn);
            }

            return center;
        }

        public static FamilyTreeNode GetFamilyTreeNode(long pActorId)
        {
            return BuildNode(pActorId) ?? BuildPlaceholderNode(pActorId);
        }

        public static List<long> GetParentIds(long pChildId, bool pUseReverseLiveLookup = false)
        {
            var edgeIds = new List<long>();
            var archiveIds = new List<long>();
            var liveIds = new List<long>();
            LineageBulkSnapshot bulk = LineageBulkSnapshotContext.Current;
            if (bulk != null && bulk.ContainsNode(pChildId))
            {
                edgeIds.AddRange(bulk.ParentIds(pChildId));
            }
            else
            {
                var db = DB;
                if (db != null)
                {
                    using (var cmd = new SQLiteCommand(db))
                    {
                        cmd.CommandText =
                            $"SELECT PARENT_ID FROM {FamilyEdgeTableItem.GetTableName()} " +
                            $"WHERE CHILD_ID=@c AND PARENT_ID>=0 ORDER BY PARENT_SLOT ASC";
                        cmd.Parameters.AddWithValue("@c", pChildId);
                        using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                        while (reader.Read()) edgeIds.Add(reader.GetInt64(0));
                    }

                    using (var cmd = new SQLiteCommand(db))
                    {
                        cmd.CommandText =
                            $"SELECT IFNULL(PARENT_ID_1, -1), IFNULL(PARENT_ID_2, -1) " +
                            $"FROM {ActorArchiveTableItem.GetTableName()} WHERE ID=@id LIMIT 1";
                        cmd.Parameters.AddWithValue("@id", pChildId);
                        using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                        if (reader.Read())
                        {
                            archiveIds.Add(ToLong(reader, 0, -1));
                            archiveIds.Add(ToLong(reader, 1, -1));
                        }
                    }
                }
            }

            Actor live = _backgroundRead ? null : World.world?.units?.get(pChildId);
            if (live?.data != null)
            {
                liveIds.Add(live.data.parent_id_1);
                liveIds.Add(live.data.parent_id_2);
            }

            var merged = FamilyTreeRelationRules.MergeRelationIds(edgeIds, archiveIds, liveIds);
            if (!FamilyTreeRelationRules.ShouldUseReverseLiveParentLookup(
                    merged.Count,
                    live?.data != null && !live.isRekt(),
                    pUseReverseLiveLookup))
                return merged;

            var reverseIds = new List<long>();
            AddLiveParentsByChildList(pChildId, reverseIds, merged);
            return FamilyTreeRelationRules.MergeRelationIds(merged, reverseIds);
        }

        public static List<long> GetChildIds(long pParentId)
        {
            var edgeIds = new List<long>();
            var archiveIds = new List<long>();
            var liveIds = new List<long>();
            LineageBulkSnapshot bulk = LineageBulkSnapshotContext.Current;
            if (bulk != null && bulk.ContainsNode(pParentId))
            {
                edgeIds.AddRange(bulk.ChildIds(pParentId));
            }
            else
            {
                var db = DB;
                if (db != null)
                {
                    using (var cmd = new SQLiteCommand(db))
                    {
                        cmd.CommandText =
                            $"SELECT CHILD_ID FROM {FamilyEdgeTableItem.GetTableName()} " +
                            $"WHERE PARENT_ID=@p ORDER BY CREATED_TIME ASC, CHILD_ID ASC";
                        cmd.Parameters.AddWithValue("@p", pParentId);
                        using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                        while (reader.Read()) edgeIds.Add(reader.GetInt64(0));
                    }

                    using (var cmd = new SQLiteCommand(db))
                    {
                        cmd.CommandText =
                            $"SELECT ID FROM {ActorArchiveTableItem.GetTableName()} " +
                            $"WHERE PARENT_ID_1=@p OR PARENT_ID_2=@p " +
                            $"ORDER BY BIRTH_TIME ASC, ID ASC";
                        cmd.Parameters.AddWithValue("@p", pParentId);
                        using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                        while (reader.Read()) archiveIds.Add(reader.GetInt64(0));
                    }
                }
            }

            Actor parent = _backgroundRead ? null : World.world?.units?.get(pParentId);
            if (parent?.data != null)
            {
                try
                {
                    foreach (Actor child in parent.getChildren(pOnlyCurrentFamily: false))
                    {
                        if (child?.data != null) liveIds.Add(child.data.id);
                    }
                }
                catch { }

                if (liveIds.Count < parent.current_children_count)
                    AddLiveChildrenByParentId(pParentId, liveIds);
            }

            return FamilyTreeRelationRules.MergeRelationIds(edgeIds, archiveIds, liveIds);
        }

        private static void AddLiveChildrenByParentId(long pParentId, List<long> pTarget)
        {
            if (pParentId < 0 || pTarget == null) return;
            var units = World.world?.units;
            if (units == null) return;

            foreach (Actor unit in units)
            {
                if (unit?.data == null || unit.isRekt()) continue;
                if (unit.data.parent_id_1 == pParentId || unit.data.parent_id_2 == pParentId)
                    pTarget.Add(unit.data.id);
            }
        }

        private static void AddLiveParentsByChildList(long pChildId, List<long> pTarget,
            IEnumerable<long> pKnownParents)
        {
            if (pChildId < 0 || pTarget == null) return;
            var units = World.world?.units;
            if (units == null) return;

            var known = new HashSet<long>();
            if (pKnownParents != null)
            {
                foreach (long id in pKnownParents)
                    if (id >= 0) known.Add(id);
            }

            foreach (Actor possibleParent in units)
            {
                if (possibleParent?.data == null || possibleParent.isRekt()) continue;
                long parentId = possibleParent.data.id;
                if (parentId < 0 || parentId == pChildId || known.Contains(parentId)) continue;

                bool matched = false;
                try
                {
                    foreach (Actor child in possibleParent.getChildren(pOnlyCurrentFamily: false))
                    {
                        if (child?.data == null || child.data.id != pChildId) continue;
                        matched = true;
                        break;
                    }
                }
                catch { }

                if (!matched || !known.Add(parentId)) continue;
                pTarget.Add(parentId);
                if (known.Count >= 2) return;
            }
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
                live.data.get(LineageKeys.GIVEN_NAME, out string given, "");
                live.data.get(LineageKeys.FAMILY_NAME, out string family, "");
                live.data.get(LineageKeys.CLAN_NAME, out string clan, "");
                live.data.get(LineageKeys.LINEAGE_STATUS,
                    out string status, LineageStatus.NONE);
                live.data.get(LineageKeys.NAME_INTEGRATED,
                    out bool integrated, false);
                live.data.get(LineageKeys.NAMING_PROFILE,
                    out string namingProfile, string.Empty);
                live.data.get(LineageKeys.WESTERN_NAMING_TRADITION,
                    out string westernTradition, string.Empty);
                live.data.get(LineageKeys.SHI_ID, out long liveShiId, -1L);
                ShiBranchInfo liveBranch = liveShiId >= 0L
                    ? GetShiBranchInfo(liveShiId)
                    : null;
                if (string.IsNullOrWhiteSpace(namingProfile) &&
                    liveBranch != null)
                    namingProfile = liveBranch.naming_profile;
                if (string.IsNullOrWhiteSpace(westernTradition) &&
                    liveBranch != null)
                    westernTradition = liveBranch.western_naming_tradition;
                return LineageDisplayNameRules.ProjectArchive(
                    string.IsNullOrEmpty(display) ? live.getName() : display,
                    given, family, clan, status, live.isSexMale(),
                    integrated ||
                    LineageService.IsKingdomIntegrated(live.kingdom),
                    namingProfile, westernTradition,
                    liveBranch?.origin_city_name ??
                    liveBranch?.origin_city_chinese_name,
                    liveBranch?.display_stem);
            }

            var row = LineageArchiveReader.ReadRow(pActorId);
            if (row == null) return "";
            ShiBranchInfo archivedBranch = row.shi_id >= 0L
                ? GetShiBranchInfo(row.shi_id)
                : null;
            return LineageDisplayNameRules.ProjectArchive(
                row.display_name, row.given_name, row.family_name,
                row.clan_name, row.status, row.sex == 0,
                row.name_integrated != 0,
                archivedBranch?.naming_profile,
                archivedBranch?.western_naming_tradition,
                archivedBranch?.origin_city_name ??
                archivedBranch?.origin_city_chinese_name,
                archivedBranch?.display_stem);
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
            NamingProfileId profile = GetBigTreeProfile(pNodeId);
            int parentSex = GetActorSex(pNodeId);
            bool parentHasHeldTitle = HasHeldTitle(pNodeId);
            foreach (long cid in childIds)
            {
                var live = units?.get(cid);
                bool liveValid = live != null && !live.isRekt() && live.isAlive();

                // 平民/奴隶不进氏族大树;女性也不进氏族大树 → 探测里同样跳过(否则会显示 + 号却展开为空)。
                string status = liveValid ? GetLiveStatus(live) : GetArchivedStatus(cid);
                int sex = liveValid ? (live.isSexMale() ? 0 : 1) : LineageArchiveReader.GetSex(cid);
                bool childHasHeldTitle = HasHeldTitle(cid);
                if (!FamilyTreeRelationRules.ShouldIncludeBigTreeEdge(
                        pNodeId, GetFatherId(cid), GetMotherId(cid),
                        parentSex, parentHasHeldTitle, sex, status,
                        childHasHeldTitle, profile)) continue;

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
            if (pNodeId < 0) return false;
            var queue = new Queue<long>();
            var visited = new HashSet<long> { pNodeId };
            queue.Enqueue(pNodeId);
            int scanned = 0;
            NamingProfileId profile = GetBigTreeProfile(pNodeId);
            while (queue.Count > 0 && scanned < 512)
            {
                long parentId = queue.Dequeue();
                int parentSex = GetActorSex(parentId);
                bool parentHasHeldTitle = HasHeldTitle(parentId);
                foreach (long childId in GetChildIds(parentId))
                {
                    Actor live = World.world?.units?.get(childId);
                    bool liveValid = live != null && !live.isRekt() && live.isAlive();
                    string status = liveValid ? GetLiveStatus(live) : GetArchivedStatus(childId);
                    int sex = liveValid ? (live.isSexMale() ? 0 : 1) : GetActorSex(childId);
                    bool childHasHeldTitle = HasHeldTitle(childId);
                    if (!FamilyTreeRelationRules.ShouldIncludeBigTreeEdge(
                            parentId, GetFatherId(childId),
                            GetMotherId(childId), parentSex,
                            parentHasHeldTitle, sex, status,
                            childHasHeldTitle, profile) ||
                        !visited.Add(childId)) continue;
                    scanned++;
                    if (liveValid || LineageArchiveReader.ReadRow(childId)?.is_alive == 1) return true;
                    queue.Enqueue(childId);
                    if (scanned >= 512) break;
                }
            }
            return false;
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
            var row = LineageArchiveReader.ReadRow(pId);
            bool archiveDead = IsArchivedDead(row);
            if (!archiveDead && FamilyTreeRelationRules.ShouldBuildLiveLineageNode(
                    IsAliveActor(live),
                    LineageService.IsXia(live),
                    LineageService.UsesAwLineageSystem(live)))
            {
                live.data.get("display_name", out string disp, "");
                live.data.get(LineageKeys.GIVEN_NAME, out string given, "");
                live.data.get(LineageKeys.FAMILY_NAME, out string family, "");
                live.data.get(LineageKeys.LINEAGE_STATUS, out string st, LineageStatus.NONE);
                live.data.get(LineageKeys.CLAN_NAME, out string clan, "");
                live.data.get(LineageKeys.NAME_INTEGRATED,
                    out bool nameIntegrated, false);
                live.data.get(LineageKeys.SHI_ID, out long shi, -1L);
                live.data.get(LineageKeys.NAMING_PROFILE,
                    out string liveNamingProfile, string.Empty);
                live.data.get(LineageKeys.WESTERN_NAMING_TRADITION,
                    out string liveWesternTradition, string.Empty);
                ShiBranchInfo liveNodeBranch = shi >= 0L
                    ? GetShiBranchInfo(shi)
                    : null;
                if (string.IsNullOrWhiteSpace(liveNamingProfile) &&
                    liveNodeBranch != null)
                    liveNamingProfile = liveNodeBranch.naming_profile;
                if (string.IsNullOrWhiteSpace(liveWesternTradition) &&
                    liveNodeBranch != null)
                    liveWesternTradition = liveNodeBranch.western_naming_tradition;
                // ⚠ NOBLE_DISTANCE 是用 set(key,int) 写入的(LineageService:111),必须 get<int> 读,
                //   用 get<long> 会类型失配返默认 99 → 活人 tooltip 永远不显示"距贵族N代"(用户报"只有死人有")。
                live.data.get(LineageKeys.NOBLE_DISTANCE, out int nd, 99);
                var kingdomSnapshot = ResolveLiveKingdomSnapshot(live, row);
                var node = new FamilyTreeNode
                {
                    id = pId,
                    asset_id = live.asset?.id ?? row?.asset_id ?? "",
                    display_name = LineageDisplayNameRules.ProjectArchive(
                        string.IsNullOrEmpty(disp) ? live.getName() : disp,
                        given, family, clan, st, live.isSexMale(),
                        nameIntegrated ||
                            LineageService.IsKingdomIntegrated(live.kingdom),
                        liveNamingProfile, liveWesternTradition,
                        liveNodeBranch?.origin_city_name ??
                        liveNodeBranch?.origin_city_chinese_name,
                        liveNodeBranch?.display_stem),
                    sex = live.isSexMale() ? 0 : 1,
                    is_alive = true,
                    status = st,
                    clan_name = clan,
                    shi_id = shi,
                    noble_distance = nd,
                    birth_time = live.data.created_time,
                    death_time = -1,
                    kingdom_id = kingdomSnapshot.kingdomId,
                    kingdom_name = kingdomSnapshot.kingdomName,
                    kingdom_color = kingdomSnapshot.kingdomColor,
                    original_clan_id = live.clan?.data?.id ?? -1,
                    city_name = ResolveLiveCityName(live, row),
                    has_held_title = HasHeldTitle(pId),
                    head = live.data.head,
                    age_overgrowth = live.data.age_overgrowth,
                    phenotype_index = live.data.phenotype_index,
                    phenotype_shade = live.data.phenotype_shade,
                    death_cause = ReadLiveDeathCause(live),
                    founded_branch_shi_id = ResolveFoundedBranch(live, pId, ReadLiveFoundedBranch(live))
                };
                ApplyFoundedBranchDisplay(node, live);
                ApplyLiveSocialTitle(node, live);
                FillKingdomFlagSnapshot(node);
                FillLiveClanFlagSnapshot(node, live.clan);
                RulerAppellationService.EnrichFamilyTreeNode(node);
                return node;
            }

            if (row == null) return null;
            bool liveKnownDead = live?.data != null && (!live.isAlive() || live.isRekt());
            bool archivedAlive = row.is_alive != 0 && row.death_time <= 0 && !liveKnownDead;
            ShiBranchInfo archivedNodeBranch = row.shi_id >= 0L
                ? GetShiBranchInfo(row.shi_id)
                : null;
            var archived = new FamilyTreeNode
            {
                id = pId,
                asset_id = row.asset_id ?? "",
                display_name = LineageDisplayNameRules.ProjectArchive(
                    row.display_name, row.given_name, row.family_name,
                    row.clan_name, row.status, row.sex == 0,
                    row.name_integrated != 0,
                    archivedNodeBranch?.naming_profile,
                    archivedNodeBranch?.western_naming_tradition,
                    archivedNodeBranch?.origin_city_name ??
                    archivedNodeBranch?.origin_city_chinese_name,
                    archivedNodeBranch?.display_stem),
                sex = row.sex,
                is_alive = archivedAlive,
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
                social_title = row.social_title ?? "",
                social_title_color = row.social_title_color ?? "",
                has_held_title = HasHeldTitle(pId),
                head = row.head,
                skin = row.skin,
                skin_set = row.skin_set,
                age_overgrowth = live?.data?.age_overgrowth ?? row.age_overgrowth,
                phenotype_index = row.phenotype_index,
                phenotype_shade = row.phenotype_shade,
                clan_color_text = row.clan_color_text ?? "",
                clan_color_id = row.clan_color_id,
                clan_banner_icon_id = row.clan_banner_icon_id,
                clan_banner_background_id = row.clan_banner_background_id,
                death_cause = NormalizeDeathCause(row.death_cause),
                founded_branch_shi_id = ResolveFoundedBranch(null, pId, row.founded_branch_shi_id)
            };
            ApplyFoundedBranchDisplay(archived, null);
            ApplyArchivedSocialTitle(archived);
            FillKingdomFlagSnapshot(archived);
            RulerAppellationService.EnrichFamilyTreeNode(archived);
            return archived;
        }

        /// <summary>父母占位节点:BuildNode 失败(非 Xia / 无档案)时,用 live actor 最小信息建节点,保证上溯链不断。
        /// 无 live actor(纯陌生 id)则返 null。</summary>
        private static FamilyTreeNode BuildPlaceholderNode(long pId)
        {
            var live = World.world?.units?.get(pId);
            if (live?.data == null) return null;
            var row = LineageArchiveReader.ReadRow(pId);
            var kingdomSnapshot = ResolveLiveKingdomSnapshot(live, row);
            var node = new FamilyTreeNode
            {
                id = pId,
                asset_id = live.asset?.id ?? row?.asset_id ?? "",
                display_name = live.getName(),
                sex = live.isSexMale() ? 0 : 1,
                is_alive = IsAliveActor(live),
                status = LineageStatus.NONE,
                clan_name = "",
                shi_id = -1,
                noble_distance = 99,
                birth_time = live.data.created_time,
                death_time = -1,
                kingdom_id = kingdomSnapshot.kingdomId,
                kingdom_name = kingdomSnapshot.kingdomName,
                kingdom_color = kingdomSnapshot.kingdomColor,
                original_clan_id = live.clan?.data?.id ?? -1,
                city_name = ResolveLiveCityName(live, row),
                head = live.data.head,
                age_overgrowth = live.data.age_overgrowth,
                phenotype_index = live.data.phenotype_index,
                phenotype_shade = live.data.phenotype_shade,
                death_cause = ReadLiveDeathCause(live)
            };
            ApplyLiveSocialTitle(node, live);
            FillKingdomFlagSnapshot(node);
            FillLiveClanFlagSnapshot(node, live.clan);
            RulerAppellationService.EnrichFamilyTreeNode(node);
            return node;
        }

        private static bool IsAliveActor(Actor pActor)
        {
            return pActor?.data != null && !pActor.isRekt() && pActor.isAlive();
        }

        private static (long kingdomId, string kingdomName, string kingdomColor) ResolveLiveKingdomSnapshot(
            Actor pLive, ActorArchiveTableItem pRow)
        {
            Kingdom kingdom = pLive?.kingdom;
            if (ShouldUseArchivedKingdomForMad(pLive, kingdom, pRow))
                return (pRow.kingdom_id, pRow.kingdom_name ?? "", pRow.kingdom_color ?? "");

            return (kingdom?.id ?? pRow?.kingdom_id ?? -1L,
                kingdom?.name ?? pRow?.kingdom_name ?? "",
                kingdom?.getColor()?.color_text ?? pRow?.kingdom_color ?? "");
        }

        private static bool ShouldUseArchivedKingdomForMad(Actor pLive, Kingdom pKingdom, ActorArchiveTableItem pRow)
        {
            if (pRow == null) return false;
            if (pRow.kingdom_id < 0 && string.IsNullOrEmpty(pRow.kingdom_name)) return false;
            return (pLive?.hasTrait("madness") ?? false) || pKingdom?.asset?.id == "mad";
        }

        private static string ResolveLiveCityName(Actor pLive, ActorArchiveTableItem pRow)
        {
            City city = pLive?.city;
            if (city == null && pLive?.data != null && pLive.data.cityID >= 0)
                city = World.world?.cities?.get(pLive.data.cityID);

            if (city?.data != null)
                return city.name ?? "";

            return pRow?.city_name ?? "";
        }

        private static bool IsArchivedDead(ActorArchiveTableItem pRow)
        {
            return pRow != null && (pRow.is_alive == 0 || pRow.death_time > 0);
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
            return NormalizeDeathCause(cause);
        }

        private static string NormalizeDeathCause(string pCause)
        {
            if (string.IsNullOrEmpty(pCause)) return "";
            return pCause == "\u6B7B\u4EA1" ? "\u672A\u77E5\u6B7B\u56E0" : pCause;
        }

        private static long ResolveFoundedBranch(Actor pLive, long pActorId, long pStoredShi)
        {
            if (!TryResolveOwnedFoundedBranch(pActorId, pStoredShi,
                    out long resolved)) return -1L;
            if (pLive?.data != null && resolved != pStoredShi)
                pLive.data.set(LineageKeys.FOUNDED_BRANCH_SHI_ID, resolved);
            return resolved;
        }

        private static void ApplyFoundedBranchDisplay(FamilyTreeNode pNode, Actor pLive)
        {
            if (pNode == null || pNode.founded_branch_shi_id < 0) return;
            ShiBranchInfo info = GetShiBranchInfo(pNode.founded_branch_shi_id);
            if (info == null ||
                !LineageBranchRules.IsFoundedBranchForActor(
                    info.source_type, info.founder_actor_id, pNode.id))
            {
                pNode.founded_branch_shi_id = -1;
                if (pLive?.data != null)
                {
                    pLive.data.set(LineageKeys.FOUNDED_BRANCH_SHI_ID, -1L);
                    LineageService.ArchiveActor(pLive, pAlive: true);
                }
                return;
            }

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

        private static void ApplyLiveSocialTitle(FamilyTreeNode pNode, Actor pLive)
        {
            if (pNode == null || pLive?.data == null) return;

            string color = pNode.kingdom_color;
            if (string.IsNullOrEmpty(color)) color = pLive.kingdom?.getColor()?.color_text ?? "";

            if (pLive.isKing())
            {
                string kingdomName = pLive.kingdom?.name ?? pNode.kingdom_name ?? "";
                if (RepublicGovernmentService.IsRepublic(pLive.kingdom))
                {
                    pNode.social_title = GovernmentTitleRules.BuildSocialTitle(
                        kingdomName, pIsHead: true, pIsElder: false);
                    pNode.social_title_color = color;
                    return;
                }
                string titleChar = KingdomTitleService.GetTitleChar(KingdomTitleService.GetTitle(pLive.kingdom));
                pNode.social_title = string.IsNullOrEmpty(kingdomName)
                    ? "\u541B\u4E3B"
                    : kingdomName + titleChar;
                pNode.social_title_color = color;
                return;
            }

            string virtualTitle = VirtualNobleTitleService.GetPrimaryTitle(pLive);
            if (!string.IsNullOrWhiteSpace(virtualTitle))
            {
                pNode.social_title = virtualTitle;
                pNode.social_title_color = color;
                return;
            }

            pLive.data.get(LineageKeys.FORMER_KING_TITLE, out string formerTitle, "");
            if (!string.IsNullOrEmpty(formerTitle))
            {
                pLive.data.get(LineageKeys.FORMER_KINGDOM_COLOR, out string formerColor, "");
                pNode.social_title = formerTitle;
                pNode.social_title_color = string.IsNullOrEmpty(formerColor) ? color : formerColor;
                return;
            }

            var roles = new List<string>();
            string rolesColor = color;
            string dynasticTitle =
                DynasticTitleService.ResolveLivingTitle(pLive);
            if (!string.IsNullOrEmpty(dynasticTitle))
                roles.Add(dynasticTitle);
            pLive.data.get(LineageKeys.FORMER_HEIR_TITLE, out string formerHeirTitle, "");
            if (!string.IsNullOrEmpty(formerHeirTitle))
            {
                roles.Add(formerHeirTitle);
                pLive.data.get(LineageKeys.FORMER_HEIR_KINGDOM_COLOR,
                    out string formerHeirColor, "");
                if (!string.IsNullOrEmpty(formerHeirColor)) rolesColor = formerHeirColor;
            }
            pLive.data.get(LineageKeys.IS_HEIR, out bool isHeir, false);
            if (isHeir || HeirService.IsCurrentHeir(pLive.kingdom, pLive))
            {
                // 名号由**他所继承的那个国**定,不是他此刻所在的国 —— 归化
                // 可能还没落定,拿错了国称谓就跟着错(见 HeirService.ResolveHeirKingdom)。
                Kingdom heirKingdom = HeirService.ResolveHeirKingdom(pLive) ??
                                      pLive.kingdom;
                string kingdomName = heirKingdom?.name ??
                                     pLive.kingdom?.name ??
                                     pNode.kingdom_name ?? "";
                roles.Add(HeirTitleRules.BuildSocialTitle(kingdomName,
                    heirKingdom));
                rolesColor = color;
            }
            if (GeneralService.IsFiefHolder(pLive))
            {
                City fief = FiefService.GetFiefCity(pLive);
                string cityName = fief?.data?.name ?? pLive.city?.data?.name ?? pNode.city_name ?? "";
                roles.Add(string.IsNullOrEmpty(cityName)
                    ? "\u5C01\u5730\u5927\u5C06"
                    : cityName + " \u5C01\u5730\u5927\u5C06");
            }
            else if (GeneralService.IsGeneral(pLive))
            {
                roles.Add("\u5927\u5C06");
            }

            if (pLive.isCityLeader())
            {
                string cityName = pLive.city?.data?.name ?? pNode.city_name ?? "";
                roles.Add(string.IsNullOrEmpty(cityName)
                    ? "\u592A\u5B88"
                    : cityName + " \u592A\u5B88");
            }

            pLive.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            if (!string.IsNullOrEmpty(office))
            {
                pLive.data.get(LineageKeys.COURT_KINGDOM_ID,
                    out long courtKingdomId, -1L);
                Kingdom courtKingdom = World.world?.kingdoms?.get(courtKingdomId) ??
                                        pLive.kingdom;
                string officeName = CourtInstitutionService.OfficeName(
                    courtKingdom, office);
                roles.Add(CourtOfficialTitleResolver.Resolve(pLive,
                    courtKingdom, office, officeName));
            }

            string combined = CourtTitleRules.Combine(roles.ToArray());
            if (!string.IsNullOrEmpty(combined))
            {
                pNode.social_title = combined;
                pNode.social_title_color = rolesColor;
                return;
            }

            pLive.data.get(LineageKeys.CAPTIVE_NOBLE_TITLE, out string captiveTitle, "");
            if (!string.IsNullOrEmpty(captiveTitle))
            {
                pLive.data.get(LineageKeys.CAPTIVE_NOBLE_COLOR, out string captiveColor, "");
                pNode.social_title = captiveTitle;
                pNode.social_title_color = string.IsNullOrEmpty(captiveColor) ? color : captiveColor;
            }
        }

        private static void ApplyArchivedSocialTitle(FamilyTreeNode pNode)
        {
            if (pNode == null) return;
            if (TryGetPosthumousTitle(pNode.id, out string title, out string titleColor))
            {
                pNode.social_title = title;
                pNode.social_title_color = string.IsNullOrEmpty(titleColor) ? pNode.kingdom_color : titleColor;
                return;
            }

            if (GovernmentTitleRules.IsRepublicSocialTitle(pNode.social_title))
            {
                if (string.IsNullOrEmpty(pNode.social_title_color))
                    pNode.social_title_color = pNode.kingdom_color;
                return;
            }

            if (WasKing(pNode.id))
            {
                pNode.social_title = string.IsNullOrEmpty(pNode.kingdom_name)
                    ? "\u541B\u4E3B"
                    : pNode.kingdom_name + " \u541B\u4E3B";
                pNode.social_title_color = pNode.kingdom_color;
                return;
            }

            string archivedPrimary = pNode.id >= 0
                ? CeremonialTitleResolver.ResolveArchived(
                    LineageArchiveReader.ReadRow(pNode.id))
                : "";
            if (!string.IsNullOrWhiteSpace(archivedPrimary))
            {
                pNode.social_title = archivedPrimary;
                pNode.social_title_color = pNode.kingdom_color;
                return;
            }

            if (!string.IsNullOrEmpty(pNode.social_title))
            {
                if (HeirTitleRules.IsGenericHeirTitle(pNode.social_title))
                    pNode.social_title = HeirTitleRules.BuildSocialTitle(pNode.kingdom_name,
                        MandateService.IsMandateKingdom(World.world?.kingdoms?.get(pNode.kingdom_id)));
                if (string.IsNullOrEmpty(pNode.social_title_color))
                    pNode.social_title_color = pNode.kingdom_color;
                return;
            }

            if (TryGetArchivedLeaderColor(pNode.id, out string leaderColor))
            {
                pNode.social_title = string.IsNullOrEmpty(pNode.city_name)
                    ? "\u592A\u5B88"
                    : pNode.city_name + " \u592A\u5B88";
                pNode.social_title_color = string.IsNullOrEmpty(leaderColor) ? pNode.kingdom_color : leaderColor;
            }
        }

        private static bool TryGetArchivedLeaderColor(long pActorId, out string pColor)
        {
            pColor = "";
            var db = DB;
            if (db == null || pActorId < 0) return false;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT IFNULL(CONTEXT_KINGDOM_COLOR, '') FROM {PersonBiographyTableItem.GetTableName()} " +
                    $"WHERE ACTOR_ID=@id AND EVENT_TYPE=@event ORDER BY WORLD_TIME DESC, EVENT_ID DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@id", pActorId);
                cmd.Parameters.AddWithValue("@event", PersonEvent.BECOME_LEADER);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                if (!r.Read()) return false;
                pColor = SafeStr(r, 0);
                return true;
            }
            catch { return false; }
        }

        private static bool TryGetPosthumousTitle(long pActorId, out string pTitle, out string pColor)
        {
            pTitle = "";
            pColor = "";
            var db = DB;
            if (db == null || pActorId < 0) return false;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT IFNULL(FULL_TITLE, ''), IFNULL(FULL_TITLE_COLOR, '') " +
                    $"FROM {PosthumousTitleTableItem.GetTableName()} " +
                    $"WHERE ACTOR_ID=@id AND IFNULL(FULL_TITLE, '')<>'' " +
                    $"ORDER BY DECIDED_TIME DESC, RECORD_ID DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@id", pActorId);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                if (r.Read())
                {
                    pTitle = SafeStr(r, 0);
                    pColor = SafeStr(r, 1);
                    return !string.IsNullOrEmpty(pTitle);
                }
            }
            catch { }
            return false;
        }

        private static bool WasKing(long pActorId)
        {
            var db = DB;
            if (db == null || pActorId < 0) return false;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT EXISTS(SELECT 1 FROM {KingdomReignTableItem.GetTableName()} " +
                    $"WHERE KING_ACTOR_ID=@id LIMIT 1)";
                cmd.Parameters.AddWithValue("@id", pActorId);
                object o = cmd.ExecuteScalar();
                return o != null && o != System.DBNull.Value && System.Convert.ToInt64(o) != 0;
            }
            catch { return false; }
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
