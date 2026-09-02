using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace AncientWarfare3.core.performance;

internal static class AWEnemyPresenceCache
{
    private static readonly Dictionary<Kingdom, bool> _cache =
        new();
    private static readonly Dictionary<
        Kingdom,
        HashSet<int>> _negativeKeys = new();
    private static readonly EnemyFinderData _emptyResult =
        new();

    [ThreadStatic]
    private static bool _preparationActive;

    private static long _queries;
    private static long _cacheHits;
    private static long _populatedEnemyKingdoms;
    private static long _emptyEnemyKingdoms;
    private static long _skippedChunkBuilds;
    private static long _negativeKeyReuses;

    internal static bool IsPreparationActive =>
        _preparationActive;

    internal static EnemyFinderData SharedEmptyResult =>
        _emptyResult;

    internal static void BeginPreparation()
    {
        _cache.Clear();
        _preparationActive = true;
    }

    internal static void EndPreparation()
    {
        _preparationActive = false;
        _cache.Clear();
    }

    internal static bool TryGetNegativeResult(
        Kingdom pKingdom,
        int pKey)
    {
        if (pKingdom == null)
        {
            return false;
        }

        if (!_negativeKeys.TryGetValue(
                pKingdom,
                out HashSet<int> keys) ||
            !keys.Contains(pKey))
        {
            return false;
        }

        if (Bench.bench_enabled)
        {
            Interlocked.Increment(
                ref _negativeKeyReuses);
        }

        return true;
    }

    internal static bool TryGetPreparationEmptyResult(
        WorldTile pTile,
        Kingdom pKingdom,
        int pRange,
        out EnemyFinderData result)
    {
        if (!_preparationActive ||
            pTile == null ||
            pKingdom == null ||
            pKingdom.asset == null ||
            pTile.chunk == null ||
            HasPopulatedEnemy(pKingdom))
        {
            result = null;
            return false;
        }

        int pKey =
            pTile.chunk.id * 10000 +
            pRange;
        if (TryGetNegativeResult(
                pKingdom,
                pKey))
        {
            EnemiesFinder.counter_reused++;
            result = _emptyResult;
            return true;
        }

        AddNegativeResult(
            pKingdom,
            pKey,
            pRange);
        result = _emptyResult;
        return true;
    }

    internal static void AddNegativeResult(
        Kingdom pKingdom,
        int pKey,
        int pRange)
    {
        if (!_negativeKeys.TryGetValue(
                pKingdom,
                out HashSet<int> keys))
        {
            keys = new HashSet<int>();
            _negativeKeys.Add(
                pKingdom,
                keys);
        }

        keys.Add(pKey);
        // Kingdom.Dispose 会把 asset 置 null,而王国对象仍可被 chunk 的
        // 王国 id 反查到 —— 这里不能假定它还在。
        if (pKingdom.asset != null &&
            !pKingdom.asset.force_look_all_chunks &&
            pRange != 0)
        {
            Randy.randomChance(0.8f);
        }

        RecordSkippedChunkBuild();
    }

    internal static void ClearNegativeKeys(
        Kingdom pKingdom)
    {
        if (pKingdom != null)
        {
            _negativeKeys.Remove(pKingdom);
        }
    }

    internal static bool HasPopulatedEnemy(
        Kingdom pMainKingdom)
    {
        bool collectDiagnostics =
            Bench.bench_enabled;
        if (collectDiagnostics)
        {
            Interlocked.Increment(ref _queries);
        }

        if (_cache.TryGetValue(
                pMainKingdom,
                out bool result))
        {
            if (collectDiagnostics)
            {
                Interlocked.Increment(ref _cacheHits);
            }

            return result;
        }

        result = FindPopulatedEnemy(pMainKingdom);
        _cache.Add(pMainKingdom, result);
        if (collectDiagnostics)
        {
            if (result)
            {
                Interlocked.Increment(
                    ref _populatedEnemyKingdoms);
            }
            else
            {
                Interlocked.Increment(
                    ref _emptyEnemyKingdoms);
            }
        }

        return result;
    }

    internal static void RecordSkippedChunkBuild()
    {
        if (Bench.bench_enabled)
        {
            Interlocked.Increment(
                ref _skippedChunkBuilds);
        }
    }

    internal static void Clear()
    {
        _preparationActive = false;
        _cache.Clear();
        _negativeKeys.Clear();
    }

    internal static string GetDiagnostics()
    {
        long queryCount =
            Interlocked.Read(ref _queries);
        long hitCount =
            Interlocked.Read(ref _cacheHits);
        return string.Format(
            CultureInfo.InvariantCulture,
            "_queries={0} cache_hits={1} ({2:0.0}%) kingdoms={3}/{4}" +
            "(enemy/empty) chunk_builds_skipped={5} negative_reuses={6}",
            queryCount,
            hitCount,
            queryCount == 0L
                ? 0.0
                : hitCount * 100.0 / queryCount,
            Interlocked.Read(
                ref _populatedEnemyKingdoms),
            Interlocked.Read(
                ref _emptyEnemyKingdoms),
            Interlocked.Read(
                ref _skippedChunkBuilds),
            Interlocked.Read(
                ref _negativeKeyReuses));
    }

    private static bool FindPopulatedEnemy(
        Kingdom pMainKingdom)
    {
        if (pMainKingdom?.asset == null)
        {
            return false;
        }

        bool peacefulMonsters =
            WorldLawLibrary
                .world_law_peaceful_monsters
                .isEnabled();
        if (pMainKingdom.asset.mobs &&
            peacefulMonsters)
        {
            return false;
        }

        if (HasPopulatedEnemyIn(
                pMainKingdom,
                World.world.kingdoms,
                peacefulMonsters))
        {
            return true;
        }

        return HasPopulatedEnemyIn(
            pMainKingdom,
            World.world.kingdoms_wild,
            peacefulMonsters);
    }

    private static bool HasPopulatedEnemyIn(
        Kingdom pMainKingdom,
        IEnumerable<Kingdom> candidates,
        bool peacefulMonsters)
    {
        foreach (Kingdom candidate in candidates)
        {
            // candidate.asset 为 null 的王国(已 Dispose 或读档时资产没解析上)
            // 直接跳过:vanilla 的 Kingdom.isCiv() 是裸的 return asset.civ,
            // 交给 isEnemy 就是一个必现 NRE。
            if (candidate?.asset == null ||
                ReferenceEquals(
                    candidate,
                    pMainKingdom) ||
                candidate.units.Count == 0 &&
                candidate.buildings.Count == 0 ||
                peacefulMonsters &&
                candidate.asset.mobs)
            {
                continue;
            }

            if (pMainKingdom.isEnemy(candidate))
            {
                return true;
            }
        }

        return false;
    }
}
