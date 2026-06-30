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
                $"SELECT SHI_ID, LINEAGE_ID, CLAN_NAME, SOURCE_TYPE, CREATED_TIME, FOUNDER_ACTOR_ID " +
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
                        founder_actor_id = reader.GetInt64(5)
                    });
                }
            }

            foreach (var s in result) FillShiCounts(s);
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

        /// <summary>取单个氏支信息(含统计)。无则 null。</summary>
        public static ShiBranchInfo GetShiBranchInfo(long pShiId)
        {
            var db = DB;
            if (db == null) return null;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT SHI_ID, LINEAGE_ID, CLAN_NAME, SOURCE_TYPE, CREATED_TIME, FOUNDER_ACTOR_ID " +
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
                        founder_actor_id = reader.GetInt64(5)
                    };
                }
            }
            if (info != null) FillShiCounts(info);
            return info;
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
                    live.data.get(LineageKeys.IS_HEIR, out bool isHeir, false);
                    if (live.isKing() || live.isCityLeader() || isHeir)
                    {
                        probe.any_important = true;
                        return probe; // 已确认重要,提前返回
                    }
                }
            }
            return probe;
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
                return new FamilyTreeNode
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
                    city_name = live.city?.data?.name ?? "",
                    head = live.data.head,
                    phenotype_index = live.data.phenotype_index,
                    phenotype_shade = live.data.phenotype_shade,
                    founded_branch_shi_id = ReadLiveFoundedBranch(live)
                };
            }

            var row = LineageArchiveReader.ReadRow(pId);
            if (row == null) return null;
            return new FamilyTreeNode
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
                city_name = row.city_name ?? "",
                head = row.head,
                skin = row.skin,
                skin_set = row.skin_set,
                phenotype_index = row.phenotype_index,
                phenotype_shade = row.phenotype_shade,
                founded_branch_shi_id = row.founded_branch_shi_id
            };
        }

        /// <summary>父母占位节点:BuildNode 失败(非 Xia / 无档案)时,用 live actor 最小信息建节点,保证上溯链不断。
        /// 无 live actor(纯陌生 id)则返 null。</summary>
        private static FamilyTreeNode BuildPlaceholderNode(long pId)
        {
            var live = World.world?.units?.get(pId);
            if (live == null) return null;
            return new FamilyTreeNode
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
                city_name = live.city?.data?.name ?? "",
                head = live.data.head,
                phenotype_index = live.data.phenotype_index,
                phenotype_shade = live.data.phenotype_shade
            };
        }

        /// <summary>读活人 actor.data 上的"称王分封新支 id"(无则 -1)。</summary>
        private static long ReadLiveFoundedBranch(Actor pLive)
        {
            pLive.data.get(LineageKeys.FOUNDED_BRANCH_SHI_ID, out long shi, -1L);
            return shi;
        }

        // ─────────────────────── helpers ───────────────────────

        private static int ToInt(SQLiteDataReader pReader, int pOrdinal)
        {
            if (pReader.IsDBNull(pOrdinal)) return 0;
            return (int)pReader.GetInt64(pOrdinal);
        }

        private static string SafeStr(SQLiteDataReader pReader, int pOrdinal)
        {
            return pReader.IsDBNull(pOrdinal) ? "" : pReader.GetString(pOrdinal);
        }
    }
}
