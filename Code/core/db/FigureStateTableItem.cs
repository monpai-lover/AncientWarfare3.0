using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.attributes;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     历史人物生成状态表(随存档持久化)——根治 AW2 用内存 Dictionary 不存档导致重进档重复生成的 bug。
    ///
    ///     每个历史人物(按 Order 0..4)一行:是否已生成、对应 actor、是否已死、套用国名的国、生成时间。
    ///     [TableDef] → LineageArchiveManager 反射自动建表 + 随存档复制(无需手写 SQL/迁移)。
    ///
    ///     注:SQLiteHelper 只有 Insert/CheckKeyExist/UpdateValue 三个扩展,无多列 select。
    ///     故读取走 FigureStateStore 的原生 SQLiteCommand 一次性载入内存缓存,运行时读内存、改时同步落盘。
    /// </summary>
    [TableDef("FigureState")]
    public class FigureStateTableItem : AbstractTableItem<FigureStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long figure_index; // 0..4(=HistoricalFigureDef.Order)

        public string figure_key;            // 全名(姬发),便于人读
        public int    spawned;               // 0/1 是否已生成过
        public long   actor_id = -1;         // 生成的 actor id
        public int    dead;                  // 0/1 该人是否已死
        public long   kingdom_id = -1;       // 成为 king 时套用国名的那个国
        public string kingdom_name_applied;  // 实际套用的国名(周/秦/…)
        public double spawn_time;
    }

    /// <summary>
    ///     FigureState 表的内存缓存 + 落盘读写。启动/读档后 Load 一次进内存,变更同步 UPDATE/INSERT。
    ///     index 越界一律返回安全默认。所有写操作幂等(先 CheckKeyExist 决定 Insert/Update)。
    /// </summary>
    public static class FigureStateStore
    {
        // 内存缓存:5 行(对应 HistoricalFigureDef.All)。
        private static readonly bool[] _spawned = new bool[content.figures.HistoricalFigureDef.Count];
        private static readonly bool[] _dead    = new bool[content.figures.HistoricalFigureDef.Count];
        private static readonly long[] _actorId = new long[content.figures.HistoricalFigureDef.Count];
        private static bool _loaded;

        private static string Table => FigureStateTableItem.GetTableName();

        /// <summary>从当前 DB 载入 5 行状态进内存(读档/新世界后调用,幂等)。DB 无行视为未生成。</summary>
        public static void Load()
        {
            for (int i = 0; i < _spawned.Length; i++)
            {
                _spawned[i] = false;
                _dead[i] = false;
                _actorId[i] = -1;
            }
            _loaded = true;

            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return;

            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText = "SELECT FIGURE_INDEX, SPAWNED, DEAD, ACTOR_ID FROM " + Table;
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    int idx = (int)reader.GetInt64(0);
                    if (idx < 0 || idx >= _spawned.Length) continue;
                    _spawned[idx] = reader.GetInt64(1) != 0;
                    _dead[idx]    = reader.GetInt64(2) != 0;
                    _actorId[idx] = reader.GetInt64(3);
                }
            }
            catch
            {
                // 表可能尚未建立(极早期调用)——视为全未生成,不抛。
            }
        }

        private static void EnsureLoaded()
        {
            if (!_loaded) Load();
        }

        public static bool IsSpawned(int pIndex)
        {
            EnsureLoaded();
            return pIndex >= 0 && pIndex < _spawned.Length && _spawned[pIndex];
        }

        public static bool IsDead(int pIndex)
        {
            EnsureLoaded();
            return pIndex >= 0 && pIndex < _dead.Length && _dead[pIndex];
        }

        public static long GetActorId(int pIndex)
        {
            EnsureLoaded();
            return pIndex >= 0 && pIndex < _actorId.Length ? _actorId[pIndex] : -1;
        }

        /// <summary>
        ///     下一个可生成的 index:最小的"未生成"且(前一个不存在或已死)的 index。
        ///     严格顺序:idx0 未生成 → 返回 0;idx0 已生成但未死 → 返回 -1(等它死);
        ///     idx0 已生成且已死、idx1 未生成 → 返回 1;全部生成完 → -1。
        /// </summary>
        public static int NextSpawnableIndex()
        {
            EnsureLoaded();
            ReconcileAliveState();                    // 先校正:已生成但单位实际已死/消失 → 补 dead
            for (int i = 0; i < _spawned.Length; i++)
            {
                if (_spawned[i]) continue;            // 已生成,跳过看下一个
                if (i == 0) return 0;                 // 第一个未生成 → 可生成
                if (_dead[i - 1]) return i;           // 前一个已死 → 轮到这个
                return -1;                            // 前一个还活着 → 暂不可生成
            }
            return -1;                                // 全生成完
        }

        /// <summary>
        ///     校正:DB 标"已生成未死"但单位实际已不存在/已死(被非 die 路径移除,如编辑器删/被抹除)→ 补 dead,
        ///     防止严格顺序因死亡钩漏触发而永久卡死。
        /// </summary>
        private static void ReconcileAliveState()
        {
            var units = World.world?.units;
            if (units == null) return;
            for (int i = 0; i < _spawned.Length; i++)
            {
                if (!_spawned[i] || _dead[i]) continue;
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
            EnsureLoaded();
            ReconcileAliveState();   // 校正已死但漏标的,避免误判"还有人活着"卡住后续生成
            for (int i = 0; i < _spawned.Length; i++)
                if (_spawned[i] && !_dead[i]) return true;
            return false;
        }

        public static void MarkSpawned(int pIndex, string pKey, long pActorId, double pTime)
        {
            EnsureLoaded();
            if (pIndex < 0 || pIndex >= _spawned.Length) return;

            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null)
            {
                // DB 失效:**不改内存**,避免"内存以为已生成、DB 无记录"重启后状态错乱。
                ModClass.LogWarning("FigureState.MarkSpawned: DB 不可用,历史人物状态未持久化(" + pKey + ")");
                return;
            }

            // DB 可用 → 先改内存再落盘(两者一致)。
            _spawned[pIndex] = true;
            _dead[pIndex] = false;
            _actorId[pIndex] = pActorId;

            if (db.CheckKeyExist(Table, SimpleColumnConstraint.CreateEq("FIGURE_INDEX", (long)pIndex)))
            {
                db.UpdateValue(Table,
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("FIGURE_INDEX", (long)pIndex) },
                    ColumnVal.Create("FIGURE_KEY", pKey),
                    ColumnVal.Create("SPAWNED", 1),
                    ColumnVal.Create("ACTOR_ID", pActorId),
                    ColumnVal.Create("DEAD", 0),
                    ColumnVal.Create("SPAWN_TIME", pTime));
                return;
            }
            db.Insert(Table,
                ColumnVal.Create("FIGURE_INDEX", (long)pIndex),
                ColumnVal.Create("FIGURE_KEY", pKey),
                ColumnVal.Create("SPAWNED", 1),
                ColumnVal.Create("ACTOR_ID", pActorId),
                ColumnVal.Create("DEAD", 0),
                ColumnVal.Create("KINGDOM_ID", -1L),
                ColumnVal.Create("KINGDOM_NAME_APPLIED", ""),
                ColumnVal.Create("SPAWN_TIME", pTime));
        }

        public static void MarkDead(int pIndex)
        {
            EnsureLoaded();
            if (pIndex < 0 || pIndex >= _dead.Length) return;
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
            EnsureLoaded();
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return;
            db.UpdateValue(Table,
                new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("FIGURE_INDEX", (long)pIndex) },
                ColumnVal.Create("KINGDOM_ID", pKingdomId),
                ColumnVal.Create("KINGDOM_NAME_APPLIED", pKingdomName ?? ""));
        }

        /// <summary>找某 actor 对应的历史人物 index(成为 king/死亡时反查)。无则 -1。</summary>
        public static int IndexOfActor(long pActorId)
        {
            EnsureLoaded();
            if (pActorId < 0) return -1;
            for (int i = 0; i < _actorId.Length; i++)
                if (_spawned[i] && _actorId[i] == pActorId) return i;
            return -1;
        }
    }
}
