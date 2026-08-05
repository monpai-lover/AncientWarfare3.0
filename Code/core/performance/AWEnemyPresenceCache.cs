using System.Collections.Generic;

namespace AncientWarfare3.core.performance
{
    /// <summary>
    /// Caches the negative result of the vanilla global enemy scan for the
    /// current EnemiesFinder lifetime. A kingdom with no hostile units or
    /// buildings cannot produce a chunk-local enemy result, so rebuilding the
    /// same empty chunk data is redundant until the finder or its container
    /// is cleared.
    /// </summary>
    internal static class AWEnemyPresenceCache
    {
        private static readonly Dictionary<Kingdom, bool> Cache =
            new Dictionary<Kingdom, bool>();
        private static readonly Dictionary<Kingdom, HashSet<int>>
            NegativeKeys =
                new Dictionary<Kingdom, HashSet<int>>();
        private static readonly object Sync = new object();
        private static readonly EnemyFinderData EmptyResult =
            new EnemyFinderData();

        internal static bool TryGetEmptyResult(
            Kingdom pKingdom,
            out EnemyFinderData pResult)
        {
            pResult = null;
            if (pKingdom == null || World.world == null)
                return false;

            lock (Sync)
            {
                if (Cache.TryGetValue(pKingdom, out bool hasEnemy))
                {
                    if (hasEnemy) return false;
                    pResult = EmptyResult;
                    return true;
                }

                hasEnemy = FindPopulatedEnemy(pKingdom);
                Cache[pKingdom] = hasEnemy;
                if (hasEnemy) return false;

                pResult = EmptyResult;
                return true;
            }
        }

        internal static bool TryGetNegativeResult(
            Kingdom pKingdom,
            int pKey,
            out EnemyFinderData pResult)
        {
            pResult = null;
            if (pKingdom == null) return false;

            lock (Sync)
            {
                if (!NegativeKeys.TryGetValue(pKingdom,
                        out HashSet<int> keys) ||
                    !keys.Contains(pKey))
                    return false;

                pResult = EmptyResult;
                return true;
            }
        }

        internal static void AddNegativeResult(
            Kingdom pKingdom,
            int pKey)
        {
            if (pKingdom == null) return;

            lock (Sync)
            {
                if (!NegativeKeys.TryGetValue(pKingdom,
                        out HashSet<int> keys))
                {
                    keys = new HashSet<int>();
                    NegativeKeys.Add(pKingdom, keys);
                }

                keys.Add(pKey);
            }
        }

        internal static void ClearNegativeKeys(Kingdom pKingdom)
        {
            if (pKingdom == null) return;
            lock (Sync) NegativeKeys.Remove(pKingdom);
        }

        internal static void Clear()
        {
            lock (Sync)
            {
                Cache.Clear();
                NegativeKeys.Clear();
            }
        }

        private static bool FindPopulatedEnemy(Kingdom pMainKingdom)
        {
            bool peacefulMonsters = WorldLawLibrary
                .world_law_peaceful_monsters?.isEnabled() == true;
            if (pMainKingdom.asset != null &&
                pMainKingdom.asset.mobs && peacefulMonsters)
                return false;

            if (HasPopulatedEnemyIn(pMainKingdom, World.world.kingdoms,
                    peacefulMonsters))
                return true;
            return HasPopulatedEnemyIn(pMainKingdom,
                World.world.kingdoms_wild, peacefulMonsters);
        }

        private static bool HasPopulatedEnemyIn(
            Kingdom pMainKingdom,
            IEnumerable<Kingdom> pCandidates,
            bool pPeacefulMonsters)
        {
            if (pCandidates == null) return false;
            foreach (Kingdom candidate in pCandidates)
            {
                if (candidate == null || ReferenceEquals(candidate,
                        pMainKingdom))
                    continue;
                if (candidate.units == null || candidate.buildings == null ||
                    candidate.units.Count == 0 &&
                    candidate.buildings.Count == 0)
                    continue;
                if (pPeacefulMonsters && candidate.asset != null &&
                    candidate.asset.mobs)
                    continue;
                if (pMainKingdom.isEnemy(candidate)) return true;
            }
            return false;
        }
    }
}
