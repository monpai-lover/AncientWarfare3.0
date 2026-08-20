using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    public static class MandateBorderTowerSpacingRules
    {
        public const int DefaultInterval = 10;
        public const int MaximumSafetyBudget = 64;
        public static IReadOnlyList<CultiwayWallPoint> SelectSpaced(IEnumerable<CultiwayWallPoint> pPoints, IEnumerable<CultiwayWallPoint> pExisting, IEnumerable<CultiwayWallPoint> pReserved, int pInterval = DefaultInterval)
        {
            int interval = Math.Max(1, pInterval);
            var existing = new HashSet<CultiwayWallPoint>(pExisting ?? Array.Empty<CultiwayWallPoint>());
            var reserved = new HashSet<CultiwayWallPoint>(pReserved ?? Array.Empty<CultiwayWallPoint>());
            var selected = new List<CultiwayWallPoint>();
            var lastByComponent = new Dictionary<int, CultiwayWallPoint>();
            int component = -1; CultiwayWallPoint previous = default; bool hasPrevious = false;
            foreach (CultiwayWallPoint point in (pPoints ?? Array.Empty<CultiwayWallPoint>()).Distinct().OrderBy(p => p.X).ThenBy(p => p.Y))
            {
                if (!hasPrevious || Manhattan(previous, point) > interval) component++;
                previous = point; hasPrevious = true;
                if (reserved.Contains(point)) continue;
                bool nearExisting = existing.Any(other => Manhattan(other, point) < interval);
                bool nearSelected = lastByComponent.TryGetValue(component, out CultiwayWallPoint last) && Manhattan(last, point) < interval;
                if (nearExisting || nearSelected) continue;
                selected.Add(point); lastByComponent[component] = point;
            }
            return selected;
        }
        public static int SafetyBudget(int pWallLength) => pWallLength <= 0 ? 0 : Math.Min(MaximumSafetyBudget, Math.Max(1, (pWallLength + DefaultInterval - 1) / DefaultInterval));
        private static int Manhattan(CultiwayWallPoint pLeft, CultiwayWallPoint pRight) => Math.Abs(pLeft.X - pRight.X) + Math.Abs(pLeft.Y - pRight.Y);
    }
}
