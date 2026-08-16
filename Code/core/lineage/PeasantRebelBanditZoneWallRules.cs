using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    public sealed class BanditZoneWallPlan
    {
        public BanditZoneWallPlan(
            IReadOnlyList<CultiwayWallPoint> pClosedWallPoints,
            IReadOnlyList<CultiwayWallPoint> pWallPoints,
            IReadOnlyList<CultiwayWallPoint> pGateCenters)
        {
            ClosedWallPoints = pClosedWallPoints ??
                Array.Empty<CultiwayWallPoint>();
            WallPoints = pWallPoints ?? Array.Empty<CultiwayWallPoint>();
            GateCenters = pGateCenters ??
                Array.Empty<CultiwayWallPoint>();
        }

        public IReadOnlyList<CultiwayWallPoint> ClosedWallPoints { get; }
        public IReadOnlyList<CultiwayWallPoint> WallPoints { get; }
        public IReadOnlyList<CultiwayWallPoint> GateCenters { get; }
    }

    public static class PeasantRebelBanditZoneWallRules
    {
        private const int RoadSearchRadius = 6;

        private static readonly CultiwayWallPoint[] CardinalDirections =
        {
            new CultiwayWallPoint(0, 1),
            new CultiwayWallPoint(1, 0),
            new CultiwayWallPoint(0, -1),
            new CultiwayWallPoint(-1, 0)
        };

        public static BanditZoneWallPlan Build(int pMapWidth,
            int pMapHeight, CultiwayWallPoint pCenter,
            IEnumerable<CultiwayWallPoint> pTerritory,
            IEnumerable<CultiwayWallPoint> pPassable,
            IEnumerable<CultiwayWallPoint> pRoads)
        {
            if (pMapWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(pMapWidth));
            if (pMapHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(pMapHeight));
            if (!InMap(pCenter, pMapWidth, pMapHeight))
                throw new ArgumentOutOfRangeException(nameof(pCenter));

            var territory = new HashSet<CultiwayWallPoint>(
                pTerritory ?? Array.Empty<CultiwayWallPoint>());
            if (territory.Count == 0 || !territory.Contains(pCenter))
                throw new ArgumentException(
                    "territory must contain its center", nameof(pTerritory));
            if (territory.Any(point =>
                    !InMap(point, pMapWidth, pMapHeight)))
                throw new ArgumentOutOfRangeException(nameof(pTerritory));

            var passable = new HashSet<CultiwayWallPoint>(
                pPassable ?? Array.Empty<CultiwayWallPoint>());
            var roads = new HashSet<CultiwayWallPoint>(
                pRoads ?? Array.Empty<CultiwayWallPoint>());
            HashSet<CultiwayWallPoint> closed = territory.Where(point =>
                    passable.Contains(point) && IsOuterBoundary(point,
                        territory))
                .ToHashSet();
            if (closed.Count == 0)
                throw new InvalidOperationException(
                    "zone territory has no placeable perimeter");

            GetBounds(territory, out int minX, out int maxX,
                out int minY, out int maxY, out int centerX,
                out int centerY);
            var opened = new HashSet<CultiwayWallPoint>(closed);
            var gateCenters = new List<CultiwayWallPoint>(4)
            {
                CarveCardinalGate(opened, closed, roads, 0, 1,
                    maxY, centerX),
                CarveCardinalGate(opened, closed, roads, 1, 0,
                    maxX, centerY),
                CarveCardinalGate(opened, closed, roads, 0, -1,
                    minY, centerX),
                CarveCardinalGate(opened, closed, roads, -1, 0,
                    minX, centerY)
            };

            return new BanditZoneWallPlan(Order(closed), Order(opened),
                gateCenters);
        }

        public static IReadOnlyList<CultiwayWallPoint>
            RankInwardTowerCandidates(CultiwayWallPoint pGate,
                CultiwayWallPoint pInwardDirection,
                int pMaxInwardSteps)
        {
            if (pMaxInwardSteps < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(pMaxInwardSteps));
            if (Math.Abs(pInwardDirection.X) +
                Math.Abs(pInwardDirection.Y) != 1)
                throw new ArgumentOutOfRangeException(
                    nameof(pInwardDirection));

            int firstStep = pInwardDirection.Y > 0 ? -1 : 0;
            var result = new CultiwayWallPoint[pMaxInwardSteps + 1];
            for (int index = 0; index <= pMaxInwardSteps; index++)
            {
                int step = firstStep + index;
                result[index] = new CultiwayWallPoint(
                    pGate.X + pInwardDirection.X * step,
                    pGate.Y + pInwardDirection.Y * step);
            }
            return result;
        }

        public static bool IsOneTileBoundaryStraddleAllowed(
            IEnumerable<CultiwayWallPoint> pFootprint,
            ISet<CultiwayWallPoint> pTerritory)
        {
            if (pFootprint == null || pTerritory == null ||
                pTerritory.Count == 0) return false;
            CultiwayWallPoint[] footprint = pFootprint.Distinct().ToArray();
            if (footprint.Length == 0 ||
                !footprint.Any(pTerritory.Contains)) return false;
            foreach (CultiwayWallPoint point in footprint)
            {
                if (pTerritory.Contains(point)) continue;
                bool adjacent = CardinalDirections.Any(direction =>
                    pTerritory.Contains(new CultiwayWallPoint(
                        point.X + direction.X,
                        point.Y + direction.Y)));
                if (!adjacent) return false;
            }
            return true;
        }

        private static bool IsOuterBoundary(CultiwayWallPoint pPoint,
            HashSet<CultiwayWallPoint> pTerritory)
        {
            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                CultiwayWallPoint direction = CardinalDirections[i];
                if (!pTerritory.Contains(new CultiwayWallPoint(
                        pPoint.X + direction.X,
                        pPoint.Y + direction.Y))) return true;
            }
            return false;
        }

        private static CultiwayWallPoint CarveCardinalGate(
            HashSet<CultiwayWallPoint> pOpened,
            HashSet<CultiwayWallPoint> pClosed,
            HashSet<CultiwayWallPoint> pRoads,
            int pDirectionX, int pDirectionY, int pSide,
            int pLateralCenter)
        {
            bool verticalSide = pDirectionY != 0;
            List<CultiwayWallPoint> candidates = pClosed.Where(point =>
                    (verticalSide ? point.Y : point.X) == pSide &&
                    HasThreeTileRun(point, pClosed, verticalSide))
                .OrderByDescending(point => HasRoadNearby(point, pRoads))
                .ThenBy(point => Math.Abs(
                    (verticalSide ? point.X : point.Y) - pLateralCenter))
                .ThenBy(point => point.X)
                .ThenBy(point => point.Y).ToList();
            if (candidates.Count == 0)
                throw new InvalidOperationException(
                    "four cardinal gates unavailable");
            CultiwayWallPoint selected = candidates[0];
            MarkThreeTilePassage(pOpened, selected);
            return selected;
        }

        private static bool HasThreeTileRun(CultiwayWallPoint pPoint,
            HashSet<CultiwayWallPoint> pWalls, bool pHorizontalRun)
        {
            if (pHorizontalRun)
            {
                return pWalls.Contains(new CultiwayWallPoint(
                           pPoint.X - 1, pPoint.Y)) &&
                       pWalls.Contains(new CultiwayWallPoint(
                           pPoint.X + 1, pPoint.Y));
            }
            return pWalls.Contains(new CultiwayWallPoint(
                       pPoint.X, pPoint.Y - 1)) &&
                   pWalls.Contains(new CultiwayWallPoint(
                       pPoint.X, pPoint.Y + 1));
        }

        private static bool HasRoadNearby(CultiwayWallPoint pPoint,
            HashSet<CultiwayWallPoint> pRoads)
        {
            int radiusSquared = RoadSearchRadius * RoadSearchRadius;
            return pRoads.Any(road => DistanceSquared(pPoint, road) <=
                radiusSquared);
        }

        private static void MarkThreeTilePassage(
            HashSet<CultiwayWallPoint> pWalls,
            CultiwayWallPoint pGate)
        {
            pWalls.RemoveWhere(point =>
                Math.Abs(point.X - pGate.X) <= 1 &&
                Math.Abs(point.Y - pGate.Y) <= 1);
        }

        private static void GetBounds(
            HashSet<CultiwayWallPoint> pTerritory,
            out int pMinX, out int pMaxX, out int pMinY, out int pMaxY,
            out int pCenterX, out int pCenterY)
        {
            pMinX = pTerritory.Min(point => point.X);
            pMaxX = pTerritory.Max(point => point.X);
            pMinY = pTerritory.Min(point => point.Y);
            pMaxY = pTerritory.Max(point => point.Y);
            pCenterX = (pMinX + pMaxX + 1) / 2;
            pCenterY = (pMinY + pMaxY + 1) / 2;
        }

        private static CultiwayWallPoint[] Order(
            IEnumerable<CultiwayWallPoint> pPoints)
        {
            return pPoints.OrderBy(point => point.X)
                .ThenBy(point => point.Y).ToArray();
        }

        private static int DistanceSquared(CultiwayWallPoint pLeft,
            CultiwayWallPoint pRight)
        {
            int dx = pLeft.X - pRight.X;
            int dy = pLeft.Y - pRight.Y;
            return dx * dx + dy * dy;
        }

        private static bool InMap(CultiwayWallPoint pPoint,
            int pWidth, int pHeight)
        {
            return pPoint.X >= 0 && pPoint.X < pWidth &&
                   pPoint.Y >= 0 && pPoint.Y < pHeight;
        }
    }
}
