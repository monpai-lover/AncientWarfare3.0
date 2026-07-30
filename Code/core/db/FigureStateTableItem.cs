using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.attributes;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     历史人物生成状态表(随存档持久化)——根治 AW2 用内存 Dictionary 不存档导致重进档重复生成的 bug。
    ///
    ///     每个历史人物按稳定 RegistryIndex 一行:是否已生成、对应 actor、是否已死、
    ///     套用国名的国、生成时间。
    ///     [TableDef] → LineageArchiveManager 反射自动建表 + 随存档复制(无需手写 SQL/迁移)。
    ///
    ///     注:SQLiteHelper 只有 Insert/CheckKeyExist/UpdateValue 三个扩展,无多列 select。
    ///     故读取走 FigureStateStore 的原生 SQLiteCommand 一次性载入内存缓存,运行时读内存、改时同步落盘。
    /// </summary>
    [TableDef("FigureState")]
    public class FigureStateTableItem : AbstractTableItem<FigureStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long figure_index; // HistoricalFigureDef.RegistryIndex

        public string figure_key;            // 稳定 figure id；旧档前五项可能仍为姓名
        public int    spawned;               // 0=available, 2=pending, 1=committed
        public long   actor_id = -1;         // 生成的 actor id
        public int    dead;                  // 0/1 该人是否已死
        public long   kingdom_id = -1;       // 成为 king 时套用国名的那个国
        public string kingdom_name_applied;  // 实际套用的国名(周/秦/…)
        public double spawn_time;
        [TableItemDef(pDefaultValue: "-1")] public long pending_lineage_id = -1;
        [TableItemDef(pDefaultValue: "-1")] public long pending_shi_id = -1;
    }

    /// <summary>
    ///     FigureState 表的内存缓存 + 落盘读写。启动/读档后 Load 一次进内存,变更同步 UPDATE/INSERT。
    ///     index 越界一律返回安全默认。所有写操作幂等(先 CheckKeyExist 决定 Insert/Update)。
    /// </summary>
    public static class FigureStateStore
    {
        // 内存缓存按稳定 registry 槽位排列。
        private static readonly int[] _spawnState = new int[content.figures.HistoricalFigureDef.Count];
        private static readonly bool[] _dead    = new bool[content.figures.HistoricalFigureDef.Count];
        private static readonly long[] _actorId = new long[content.figures.HistoricalFigureDef.Count];
        private static bool _loaded;
        private static bool _reservationFailureLogged;

        private static string Table => FigureStateTableItem.GetTableName();
        public static bool IsReady
        {
            get
            {
                LineageArchiveManager manager = LineageArchiveManager.Instance;
                if (!manager.IsOperational) return false;
                EnsureLoaded();
                return _loaded;
            }
        }

        /// <summary>从当前 DB 载入生成状态(读档/新世界后调用,幂等)。DB 无行视为未生成。</summary>
        public static void Load()
        {
            for (int i = 0; i < _spawnState.Length; i++)
            {
                _spawnState[i] = content.figures.HistoricalFigureSpawnRules.Available;
                _dead[i] = false;
                _actorId[i] = -1;
            }
            _loaded = false;
            _reservationFailureLogged = false;

            LineageArchiveManager manager = LineageArchiveManager.Instance;
            if (!manager.IsOperational) return;
            var db = manager.OperatingDB;

            try
            {
                FigureStatePendingRecovery.Recover(db, Table,
                    ShiBranchTableItem.GetTableName(),
                    LineageGroupTableItem.GetTableName(),
                    lineage.ShiSourceType.SPECIAL_FIGURE);

                using var cmd = new SQLiteCommand(db);
                cmd.CommandText = "SELECT FIGURE_INDEX, SPAWNED, DEAD, ACTOR_ID FROM " + Table;
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    int idx = (int)reader.GetInt64(0);
                    if (idx < 0 || idx >= _spawnState.Length) continue;
                    int state = content.figures.HistoricalFigureSpawnRules.
                        NormalizeLoadedSpawnState((int)reader.GetInt64(1));
                    _spawnState[idx] = state;
                    if (state == content.figures.HistoricalFigureSpawnRules.Committed)
                    {
                        _dead[idx] = reader.GetInt64(2) != 0;
                        _actorId[idx] = reader.GetInt64(3);
                    }
                }
                _loaded = true;
            }
            catch
            {
                // 表可能尚未建立(极早期调用)——视为全未生成,不抛。
            }
        }

        private static bool EnsureLoaded()
        {
            if (!_loaded) Load();
            return _loaded;
        }

        public static bool IsSpawned(int pIndex)
        {
            if (!EnsureLoaded()) return false;
            return pIndex >= 0 && pIndex < _spawnState.Length &&
                   _spawnState[pIndex] ==
                   content.figures.HistoricalFigureSpawnRules.Committed;
        }

        public static bool IsDead(int pIndex)
        {
            if (!EnsureLoaded()) return false;
            return pIndex >= 0 && pIndex < _dead.Length &&
                   _spawnState[pIndex] ==
                   content.figures.HistoricalFigureSpawnRules.Committed &&
                   _dead[pIndex];
        }

        public static long GetActorId(int pIndex)
        {
            if (!EnsureLoaded()) return -1;
            return pIndex >= 0 && pIndex < _actorId.Length &&
                   _spawnState[pIndex] ==
                   content.figures.HistoricalFigureSpawnRules.Committed
                ? _actorId[pIndex]
                : -1;
        }

        /// <summary>
        ///     按 HistoricalFigureDef.SpawnOrder 找下一位，但返回稳定 registry index。
        ///     这样新增人物可插入历史顺序，而旧档曹丕=3、司马炎=4 不会被重解释。
        /// </summary>
        public static int NextSpawnableIndex()
        {
            if (!EnsureLoaded()) return -1;
            ReconcileAliveState();                    // 先校正:已生成但单位实际已死/消失 → 补 dead
            return content.figures.HistoricalFigureSpawnRules.
                NextSpawnableRegistryIndex(
                    content.figures.HistoricalFigureDef.SpawnRegistryOrder,
                    _spawnState, _dead);
        }

        /// <summary>
        ///     校正:DB 标"已生成未死"但单位实际已不存在/已死(被非 die 路径移除,如编辑器删/被抹除)→ 补 dead,
        ///     防止严格顺序因死亡钩漏触发而永久卡死。
        /// </summary>
        private static void ReconcileAliveState()
        {
            var units = World.world?.units;
            if (units == null) return;
            for (int i = 0; i < _spawnState.Length; i++)
            {
                if (!content.figures.HistoricalFigureSpawnRules.
                        IsCommittedAlive(_spawnState[i], _dead[i])) continue;
                long aid = _actorId[i];
                if (aid < 0) continue;
                var actor = units.get(aid);
                // 用 isRekt()(真销毁/移除)而非 !isAlive():新生 figure baby 可能瞬时 isAlive()==false,
                // 用 !isAlive() 会把刚降临的 baby 误判为死 → 提前解锁下一位 + 互斥失效。
                if (actor == null || actor.isRekt()) MarkDead(i);
            }
        }

        /// <summary>是否存在"已生成但未死"的历史人物(存活互斥用,避免遍历全图单位)。</summary>
        public static bool AnyAliveFigure()
        {
            if (!EnsureLoaded()) return false;
            ReconcileAliveState();   // 校正已死但漏标的,避免误判"还有人活着"卡住后续生成
            for (int i = 0; i < _spawnState.Length; i++)
                if (content.figures.HistoricalFigureSpawnRules.
                    IsCommittedAlive(_spawnState[i], _dead[i])) return true;
            return false;
        }

        public static bool TryReserveSpawn(int pIndex, string pKey,
            long pActorId, double pTime)
        {
            if (!IsReady || pIndex < 0 || pIndex >= _spawnState.Length ||
                pActorId < 0)
                return false;

            SQLiteConnection db = LineageArchiveManager.Instance.OperatingDB;
            try
            {
                using SQLiteTransaction transaction = db.BeginTransaction();
                int affected;
                using (var update = new SQLiteCommand(db)
                       { Transaction = transaction })
                {
                    update.CommandText = "UPDATE " + Table +
                        " SET FIGURE_KEY=@key,SPAWNED=2,ACTOR_ID=@actor," +
                        "DEAD=0,KINGDOM_ID=-1,KINGDOM_NAME_APPLIED=''," +
                        "SPAWN_TIME=@time,PENDING_LINEAGE_ID=-1," +
                        "PENDING_SHI_ID=-1 WHERE FIGURE_INDEX=@index " +
                        "AND SPAWNED=0";
                    update.Parameters.AddWithValue("@key", pKey ?? "");
                    update.Parameters.AddWithValue("@actor", pActorId);
                    update.Parameters.AddWithValue("@time", pTime);
                    update.Parameters.AddWithValue("@index", (long)pIndex);
                    affected = update.ExecuteNonQuery();
                }

                if (affected == 0)
                {
                    using var insert = new SQLiteCommand(db)
                        { Transaction = transaction };
                    insert.CommandText = "INSERT OR IGNORE INTO " + Table +
                        " (FIGURE_INDEX,FIGURE_KEY,SPAWNED,ACTOR_ID,DEAD," +
                        "KINGDOM_ID,KINGDOM_NAME_APPLIED,SPAWN_TIME," +
                        "PENDING_LINEAGE_ID,PENDING_SHI_ID) VALUES " +
                        "(@index,@key,2,@actor,0,-1,'',@time,-1,-1)";
                    insert.Parameters.AddWithValue("@index", (long)pIndex);
                    insert.Parameters.AddWithValue("@key", pKey ?? "");
                    insert.Parameters.AddWithValue("@actor", pActorId);
                    insert.Parameters.AddWithValue("@time", pTime);
                    affected = insert.ExecuteNonQuery();
                }

                if (affected != 1)
                {
                    transaction.Rollback();
                    return false;
                }

                transaction.Commit();
                _spawnState[pIndex] = content.figures.
                    HistoricalFigureSpawnRules.Pending;
                _dead[pIndex] = false;
                _actorId[pIndex] = pActorId;
                return true;
            }
            catch (System.Exception error)
            {
                if (!_reservationFailureLogged)
                {
                    _reservationFailureLogged = true;
                    ModClass.LogWarning(
                        "FigureState reservation failed; historical figure deferred: " +
                        error.Message);
                }
                return false;
            }
        }

        public static bool TryCommitSpawn(int pIndex, long pActorId)
        {
            return TryCommitPending(pIndex, pActorId);
        }

        public static bool TryBindPendingLineage(int pIndex, long pActorId,
            long pLineageId, long pShiId)
        {
            if (!IsReady || pIndex < 0 || pIndex >= _spawnState.Length ||
                pActorId < 0 || pLineageId < 0 || pShiId < 0)
                return false;

            SQLiteConnection db = LineageArchiveManager.Instance.OperatingDB;
            try
            {
                using SQLiteTransaction transaction = db.BeginTransaction();
                using var update = new SQLiteCommand(db)
                    { Transaction = transaction };
                update.CommandText = "UPDATE " + Table +
                    " SET PENDING_LINEAGE_ID=@lineage,PENDING_SHI_ID=@shi" +
                    " WHERE FIGURE_INDEX=@index AND SPAWNED=2" +
                    " AND ACTOR_ID=@actor AND PENDING_LINEAGE_ID=-1" +
                    " AND PENDING_SHI_ID=-1";
                update.Parameters.AddWithValue("@lineage", pLineageId);
                update.Parameters.AddWithValue("@shi", pShiId);
                update.Parameters.AddWithValue("@index", (long)pIndex);
                update.Parameters.AddWithValue("@actor", pActorId);
                if (update.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    return false;
                }
                transaction.Commit();
                return true;
            }
            catch (System.Exception error)
            {
                ModClass.LogWarning(
                    "FigureState pending lineage bind failed: " + error.Message);
                return false;
            }
        }

        public static bool TryAbortSpawn(int pIndex, long pActorId)
        {
            if (!IsReady || pIndex < 0 || pIndex >= _spawnState.Length ||
                pActorId < 0)
                return false;
            try
            {
                bool aborted = FigureStatePendingRecovery.TryAbort(
                    LineageArchiveManager.Instance.OperatingDB, Table,
                    ShiBranchTableItem.GetTableName(),
                    LineageGroupTableItem.GetTableName(),
                    lineage.ShiSourceType.SPECIAL_FIGURE, pIndex, pActorId);
                if (!aborted) return false;
                _spawnState[pIndex] = content.figures.
                    HistoricalFigureSpawnRules.Available;
                _dead[pIndex] = false;
                _actorId[pIndex] = -1;
                return true;
            }
            catch (System.Exception error)
            {
                ModClass.LogWarning(
                    "FigureState pending abort failed: " + error.Message);
                return false;
            }
        }

        private static bool TryCommitPending(int pIndex, long pActorId)
        {
            if (!IsReady || pIndex < 0 || pIndex >= _spawnState.Length ||
                pActorId < 0)
                return false;

            SQLiteConnection db = LineageArchiveManager.Instance.OperatingDB;
            try
            {
                using SQLiteTransaction transaction = db.BeginTransaction();
                using var update = new SQLiteCommand(db)
                    { Transaction = transaction };
                update.CommandText = "UPDATE " + Table +
                    " SET SPAWNED=1,PENDING_LINEAGE_ID=-1," +
                    "PENDING_SHI_ID=-1" +
                    " WHERE FIGURE_INDEX=@index AND SPAWNED=2 " +
                    "AND ACTOR_ID=@actor";
                update.Parameters.AddWithValue("@index", (long)pIndex);
                update.Parameters.AddWithValue("@actor", pActorId);
                int affected = update.ExecuteNonQuery();
                if (affected != 1)
                {
                    transaction.Rollback();
                    return false;
                }

                transaction.Commit();
                _spawnState[pIndex] = content.figures.
                    HistoricalFigureSpawnRules.Committed;
                _dead[pIndex] = false;
                _actorId[pIndex] = pActorId;
                return true;
            }
            catch (System.Exception error)
            {
                ModClass.LogWarning(
                    "FigureState pending transition failed: " + error.Message);
                return false;
            }
        }

        public static void MarkDead(int pIndex)
        {
            if (!EnsureLoaded()) return;
            if (pIndex < 0 || pIndex >= _dead.Length) return;
            if (_spawnState[pIndex] !=
                content.figures.HistoricalFigureSpawnRules.Committed) return;
            _dead[pIndex] = true;

            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return;
            db.UpdateValue(Table,
                new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("FIGURE_INDEX", (long)pIndex) },
                ColumnVal.Create("DEAD", 1));
        }

        /// <summary>记录成为 king 时套用的国名/国 id(供日后天命国系统读取)。</summary>
        public static void MarkKingdomApplied(int pIndex, long pKingdomId, string pKingdomName)
        {
            if (!EnsureLoaded()) return;
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return;
            db.UpdateValue(Table,
                new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("FIGURE_INDEX", (long)pIndex) },
                ColumnVal.Create("KINGDOM_ID", pKingdomId),
                ColumnVal.Create("KINGDOM_NAME_APPLIED", pKingdomName ?? ""));
        }

        /// <summary>找某 actor 对应的历史人物 index(成为 king/死亡时反查)。无则 -1。</summary>
        public static bool TryGetAppliedKingdomName(long pKingdomId, out string pKingdomName)
        {
            pKingdomName = "";
            if (!EnsureLoaded()) return false;
            if (pKingdomId < 0) return false;

            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return false;

            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    "SELECT IFNULL(KINGDOM_NAME_APPLIED, '') FROM " + Table +
                    " WHERE KINGDOM_ID=@kid AND IFNULL(KINGDOM_NAME_APPLIED, '')<>'' LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                object value = cmd.ExecuteScalar();
                pKingdomName = value == null || value == System.DBNull.Value ? "" : value.ToString();
                return !string.IsNullOrEmpty(pKingdomName);
            }
            catch
            {
                pKingdomName = "";
                return false;
            }
        }

        public static int IndexOfActor(long pActorId)
        {
            if (!EnsureLoaded()) return -1;
            if (pActorId < 0) return -1;
            for (int i = 0; i < _actorId.Length; i++)
                if (_spawnState[i] ==
                        content.figures.HistoricalFigureSpawnRules.Committed &&
                    _actorId[i] == pActorId) return i;
            return -1;
        }
    }
}
