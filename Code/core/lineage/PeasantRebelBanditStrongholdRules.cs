using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    public sealed class BanditZoneFact
    {
        private BanditZoneFact(string key, int x, int y,
            IEnumerable<string> neighbourKeys)
        {
            Key = (key ?? "").Trim();
            X = x;
            Y = y;
            NeighbourKeys = new List<string>(
                neighbourKeys ?? Array.Empty<string>());
        }

        public string Key { get; }
        public IReadOnlyList<string> NeighbourKeys { get; }

        public int X { get; }
        public int Y { get; }

        public static BanditZoneFact At(string key, int x, int y,
            IEnumerable<string> neighbourKeys)
        {
            return new BanditZoneFact(key, x, y, neighbourKeys);
        }
    }

    public static class PeasantRebelBanditStrongholdRules
    {
        private const int StrongholdZoneCount = 4;

        private sealed class RankedCandidate
        {
            internal IReadOnlyList<string> Keys;
            internal bool IsTwoByTwo;
            internal long BoundingArea;
            internal long DistanceFromCenter;
            internal string Canonical;
        }

        public static IReadOnlyList<IReadOnlyList<string>>
            RankFourZoneCandidates(IReadOnlyList<BanditZoneFact> zones,
                string centerKey)
        {
            if (zones == null || zones.Count < StrongholdZoneCount ||
                string.IsNullOrWhiteSpace(centerKey))
                return Array.Empty<IReadOnlyList<string>>();

            var byKey = new Dictionary<string, BanditZoneFact>(
                StringComparer.Ordinal);
            for (int i = 0; i < zones.Count; i++)
            {
                BanditZoneFact zone = zones[i];
                if (zone == null || zone.Key.Length == 0) continue;
                byKey[zone.Key] = zone;
            }
            if (!byKey.TryGetValue(centerKey, out BanditZoneFact center))
                return Array.Empty<IReadOnlyList<string>>();

            var ranked = new List<RankedCandidate>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            ExpandCandidate(new HashSet<string>(StringComparer.Ordinal)
                { centerKey }, byKey, center, visited, ranked);
            return ranked.OrderBy(candidate => candidate.IsTwoByTwo ? 0 : 1)
                .ThenBy(candidate => candidate.BoundingArea)
                .ThenBy(candidate => candidate.DistanceFromCenter)
                .ThenBy(candidate => candidate.Canonical,
                    StringComparer.Ordinal)
                .Select(candidate => candidate.Keys).ToArray();
        }

        private static void ExpandCandidate(HashSet<string> selected,
            IReadOnlyDictionary<string, BanditZoneFact> byKey,
            BanditZoneFact center, HashSet<string> visited,
            List<RankedCandidate> ranked)
        {
            string canonical = CanonicalKey(selected);
            if (!visited.Add(canonical)) return;
            if (selected.Count == StrongholdZoneCount)
            {
                BanditZoneFact[] facts = selected.Select(key => byKey[key])
                    .ToArray();
                ranked.Add(new RankedCandidate
                {
                    Keys = selected.OrderBy(key => key,
                        StringComparer.Ordinal).ToArray(),
                    IsTwoByTwo = IsTwoByTwo(facts),
                    BoundingArea = BoundingArea(facts),
                    DistanceFromCenter = facts.Sum(fact =>
                        (long)Math.Abs(fact.X - center.X) +
                        Math.Abs(fact.Y - center.Y)),
                    Canonical = canonical
                });
                return;
            }

            string[] frontier = selected.SelectMany(key =>
                    byKey[key].NeighbourKeys)
                .Where(key => !selected.Contains(key) &&
                              byKey.ContainsKey(key))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < frontier.Length; i++)
            {
                selected.Add(frontier[i]);
                ExpandCandidate(selected, byKey, center, visited, ranked);
                selected.Remove(frontier[i]);
            }
        }

        private static bool IsTwoByTwo(IReadOnlyList<BanditZoneFact> facts)
        {
            if (facts == null || facts.Count != StrongholdZoneCount)
                return false;
            int[] xs = facts.Select(fact => fact.X).Distinct().ToArray();
            int[] ys = facts.Select(fact => fact.Y).Distinct().ToArray();
            if (xs.Length != 2 || ys.Length != 2) return false;
            return xs.All(x => ys.All(y => facts.Any(fact =>
                fact.X == x && fact.Y == y)));
        }

        private static long BoundingArea(IReadOnlyList<BanditZoneFact> facts)
        {
            long width = (long)facts.Max(fact => fact.X) -
                         facts.Min(fact => fact.X) + 1L;
            long height = (long)facts.Max(fact => fact.Y) -
                          facts.Min(fact => fact.Y) + 1L;
            return width * height;
        }

        private static string CanonicalKey(IEnumerable<string> keys)
        {
            return string.Join("|", keys.OrderBy(key => key,
                StringComparer.Ordinal));
        }

        public static bool IsViableSplit(int interiorCount,
            int exteriorCount)
        {
            return interiorCount == StrongholdZoneCount &&
                   exteriorCount > 0;
        }

        public static bool ShouldRestoreWall(string currentTopTypeId)
        {
            return string.Equals(currentTopTypeId, "wall_wild",
                StringComparison.Ordinal);
        }

        public static bool CanAcquireZone(bool bandit, string zoneKey,
            ISet<string> fixedZoneKeys)
        {
            if (!bandit) return true;
            return !string.IsNullOrWhiteSpace(zoneKey) &&
                   fixedZoneKeys != null && fixedZoneKeys.Contains(zoneKey);
        }

        public static string ComposeStrongholdName(string root)
        {
            return PeasantRebelOutlawNameRules.NormalizeRoot(root) +
                   "\u5be8";
        }

        public static string ComposeCeremonialTitle(string root, bool heir)
        {
            return ComposeCeremonialTitle(root,
                heir ? "\u5c11\u5f53\u5bb6" : "\u5927\u5f53\u5bb6");
        }

        public static string ComposeCeremonialTitle(string root,
            string roleText)
        {
            return ComposeStrongholdName(root) + (roleText ?? "").Trim();
        }
    }
}
