using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class BanditZoneFact
    {
        public BanditZoneFact(string key, bool insideWall,
            IEnumerable<string> neighbourKeys)
        {
            Key = (key ?? "").Trim();
            InsideWall = insideWall;
            NeighbourKeys = new List<string>(
                neighbourKeys ?? Array.Empty<string>());
        }

        public string Key { get; }
        public bool InsideWall { get; }
        public IReadOnlyList<string> NeighbourKeys { get; }
    }

    public static class PeasantRebelBanditStrongholdRules
    {
        public static HashSet<string> SelectInteriorZoneKeys(
            IReadOnlyList<BanditZoneFact> zones, string centerKey)
        {
            var selected = new HashSet<string>(StringComparer.Ordinal);
            if (zones == null || zones.Count == 0 ||
                string.IsNullOrWhiteSpace(centerKey)) return selected;

            var byKey = new Dictionary<string, BanditZoneFact>(
                StringComparer.Ordinal);
            for (int i = 0; i < zones.Count; i++)
            {
                BanditZoneFact zone = zones[i];
                if (zone == null || zone.Key.Length == 0) continue;
                byKey[zone.Key] = zone;
            }
            if (!byKey.TryGetValue(centerKey, out BanditZoneFact center) ||
                !center.InsideWall) return selected;

            var pending = new Queue<string>();
            selected.Add(center.Key);
            pending.Enqueue(center.Key);
            while (pending.Count > 0)
            {
                BanditZoneFact current = byKey[pending.Dequeue()];
                for (int i = 0; i < current.NeighbourKeys.Count; i++)
                {
                    string neighbourKey = current.NeighbourKeys[i];
                    if (selected.Contains(neighbourKey) ||
                        !byKey.TryGetValue(neighbourKey,
                            out BanditZoneFact neighbour) ||
                        !neighbour.InsideWall) continue;
                    selected.Add(neighbourKey);
                    pending.Enqueue(neighbourKey);
                }
            }
            return selected;
        }

        public static bool IsViableSplit(int interiorCount,
            int exteriorCount)
        {
            return interiorCount > 0 && exteriorCount > 0;
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
            return ComposeStrongholdName(root) +
                   (heir ? "\u5c11\u5f53\u5bb6" : "\u5927\u5f53\u5bb6");
        }
    }
}
