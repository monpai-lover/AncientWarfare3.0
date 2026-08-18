using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    public enum BanditStrongholdFallAction
    {
        None,
        RecordSuppressorOnly,
        QueueFall
    }

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
            internal bool IsCentered;
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

            var byCoordinate = new Dictionary<(int X, int Y),
                BanditZoneFact>();
            foreach (BanditZoneFact zone in byKey.Values)
                byCoordinate[(zone.X, zone.Y)] = zone;

            var ranked = new List<RankedCandidate>();
            for (int offsetY = 0; offsetY < 2; offsetY++)
            for (int offsetX = 0; offsetX < 2; offsetX++)
            {
                int originX = center.X - offsetX;
                int originY = center.Y - offsetY;
                var facts = new List<BanditZoneFact>(StrongholdZoneCount);
                bool complete = true;
                for (int y = originY; y < originY + 2 && complete; y++)
                for (int x = originX; x < originX + 2; x++)
                {
                    if (!byCoordinate.TryGetValue((x, y),
                            out BanditZoneFact fact))
                    {
                        complete = false;
                        break;
                    }
                    facts.Add(fact);
                }
                if (!complete || facts.Count != StrongholdZoneCount)
                    continue;
                string[] keys = facts.Select(fact => fact.Key)
                    .OrderBy(key => key, StringComparer.Ordinal).ToArray();
                ranked.Add(new RankedCandidate
                {
                    Keys = keys,
                    IsCentered = offsetX == 1 && offsetY == 1,
                    DistanceFromCenter = facts.Sum(fact =>
                        (long)Math.Abs(fact.X - center.X) +
                        Math.Abs(fact.Y - center.Y)),
                    Canonical = CanonicalKey(keys)
                });
            }

            return ranked.OrderBy(candidate => candidate.IsCentered ? 0 : 1)
                .ThenBy(candidate => candidate.DistanceFromCenter)
                .ThenBy(candidate => candidate.Canonical,
                    StringComparer.Ordinal)
                .Select(candidate => candidate.Keys).ToArray();
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
                   exteriorCount >= 0;
        }

        public static bool CanUseWallCandidate(bool wallPlanExists,
            int gateTowerCount)
        {
            _ = gateTowerCount;
            return wallPlanExists;
        }

        public static bool CanDisposeStrongholdCity(bool cityExists,
            bool cityIsRekt, int zoneCount, bool expectedOwnerMatches)
        {
            return cityExists && !cityIsRekt &&
                   (expectedOwnerMatches || zoneCount == 0);
        }

        public static bool ShouldRestoreWall(string currentTopTypeId)
        {
            return string.Equals(currentTopTypeId, "wall_wild",
                   StringComparison.Ordinal);
        }

        public static long ResolveSuppressorKingdomId(
            long lastHostileKillerKingdomId, long originKingdomId,
            bool originAtWar)
        {
            if (lastHostileKillerKingdomId > 0)
                return lastHostileKillerKingdomId;
            return originAtWar && originKingdomId > 0
                ? originKingdomId
                : -1L;
        }

        public static bool CanAttributeHostileKiller(
            bool hostileAttacker, bool deathClearsAttacker)
        {
            return hostileAttacker && !deathClearsAttacker;
        }

        public static BanditStrongholdFallAction ResolveFallAction(
            int population, long hostileKillerKingdomId,
            bool captureFinished)
        {
            if (captureFinished) return BanditStrongholdFallAction.QueueFall;
            if (population > 0) return BanditStrongholdFallAction.None;
            return hostileKillerKingdomId > 0
                ? BanditStrongholdFallAction.RecordSuppressorOnly
                : BanditStrongholdFallAction.QueueFall;
        }

        public static bool CanRelocateOrdinaryResident(bool adult,
            bool civilianProfession, bool king, bool cityLeader,
            bool heir)
        {
            return adult && civilianProfession && !king && !cityLeader &&
                   !heir;
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
