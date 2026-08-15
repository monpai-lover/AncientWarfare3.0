using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    public sealed class CultiwayFrontierWallGeometryInput
    {
        public CultiwayFrontierWallGeometryInput(
            IEnumerable<CultiwayWallPoint> pCityLand,
            IEnumerable<CultiwayWallPoint> pPassable,
            IEnumerable<CultiwayWallPoint> pFrontierSeeds,
            IEnumerable<CultiwayWallPoint> pRoads,
            int pWallWidth)
        {
            if (pWallWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(pWallWidth));

            CityLand = new HashSet<CultiwayWallPoint>(
                pCityLand ?? Array.Empty<CultiwayWallPoint>());
            Passable = new HashSet<CultiwayWallPoint>(
                pPassable ?? Array.Empty<CultiwayWallPoint>());
            FrontierSeeds = new HashSet<CultiwayWallPoint>(
                pFrontierSeeds ?? Array.Empty<CultiwayWallPoint>());
            Roads = new HashSet<CultiwayWallPoint>(
                pRoads ?? Array.Empty<CultiwayWallPoint>());
            WallWidth = pWallWidth;
        }

        public HashSet<CultiwayWallPoint> CityLand { get; }
        public HashSet<CultiwayWallPoint> Passable { get; }
        public HashSet<CultiwayWallPoint> FrontierSeeds { get; }
        public HashSet<CultiwayWallPoint> Roads { get; }
        public int WallWidth { get; }
    }

    public static class CultiwayStyleFrontierWallGeometryRules
    {
        private const int PassageHalfWidth = 1;

        private static readonly CultiwayWallPoint[] CardinalDirections =
        {
            new CultiwayWallPoint(-1, 0),
            new CultiwayWallPoint(1, 0),
            new CultiwayWallPoint(0, -1),
            new CultiwayWallPoint(0, 1)
        };

        public static IReadOnlyList<CultiwayWallPoint> Compute(
            CultiwayFrontierWallGeometryInput pInput)
        {
            if (pInput == null)
                throw new ArgumentNullException(nameof(pInput));

            HashSet<CultiwayWallPoint> available = pInput.CityLand
                .Where(pInput.Passable.Contains).ToHashSet();
            HashSet<CultiwayWallPoint> layer = pInput.FrontierSeeds
                .Where(available.Contains).ToHashSet();
            var walls = new HashSet<CultiwayWallPoint>();

            for (int depth = 0;
                 depth < pInput.WallWidth && layer.Count > 0;
                 depth++)
            {
                walls.UnionWith(layer);
                layer = layer.SelectMany(CardinalNeighbours)
                    .Where(point => available.Contains(point) &&
                                    !walls.Contains(point))
                    .ToHashSet();
            }

            SealDiagonalGaps(walls, available);
            CarveRoadPassages(walls, pInput.Roads);
            return walls.OrderBy(point => point.X)
                .ThenBy(point => point.Y).ToArray();
        }

        private static IEnumerable<CultiwayWallPoint> CardinalNeighbours(
            CultiwayWallPoint pPoint)
        {
            foreach (CultiwayWallPoint direction in CardinalDirections)
                yield return Offset(pPoint, direction.X, direction.Y);
        }

        private static void SealDiagonalGaps(
            HashSet<CultiwayWallPoint> pWalls,
            HashSet<CultiwayWallPoint> pAvailable)
        {
            var additions = new HashSet<CultiwayWallPoint>();
            foreach (CultiwayWallPoint point in pWalls.ToArray())
            {
                TrySeal(point, 1, 1, pWalls, pAvailable, additions);
                TrySeal(point, 1, -1, pWalls, pAvailable, additions);
            }
            pWalls.UnionWith(additions);
        }

        private static void TrySeal(CultiwayWallPoint pPoint,
            int pDx, int pDy,
            HashSet<CultiwayWallPoint> pWalls,
            HashSet<CultiwayWallPoint> pAvailable,
            HashSet<CultiwayWallPoint> pAdditions)
        {
            CultiwayWallPoint diagonal = Offset(pPoint, pDx, pDy);
            if (!pWalls.Contains(diagonal)) return;

            CultiwayWallPoint horizontal = Offset(pPoint, pDx, 0);
            CultiwayWallPoint vertical = Offset(pPoint, 0, pDy);
            if (pWalls.Contains(horizontal) || pWalls.Contains(vertical) ||
                pAdditions.Contains(horizontal) ||
                pAdditions.Contains(vertical)) return;

            bool hasHorizontal = pAvailable.Contains(horizontal);
            bool hasVertical = pAvailable.Contains(vertical);
            if (!hasHorizontal && !hasVertical) return;
            if (!hasHorizontal) pAdditions.Add(vertical);
            else if (!hasVertical) pAdditions.Add(horizontal);
            else pAdditions.Add(Compare(horizontal, vertical) <= 0
                ? horizontal : vertical);
        }

        private static void CarveRoadPassages(
            HashSet<CultiwayWallPoint> pWalls,
            HashSet<CultiwayWallPoint> pRoads)
        {
            CultiwayWallPoint[] crossings = pRoads
                .Where(pWalls.Contains).ToArray();
            if (crossings.Length == 0) return;

            pWalls.RemoveWhere(point => crossings.Any(crossing =>
                Math.Abs(point.X - crossing.X) <= PassageHalfWidth &&
                Math.Abs(point.Y - crossing.Y) <= PassageHalfWidth));
        }

        private static int Compare(CultiwayWallPoint pLeft,
            CultiwayWallPoint pRight)
        {
            int x = pLeft.X.CompareTo(pRight.X);
            return x != 0 ? x : pLeft.Y.CompareTo(pRight.Y);
        }

        private static CultiwayWallPoint Offset(
            CultiwayWallPoint pPoint, int pDx, int pDy)
        {
            return new CultiwayWallPoint(
                pPoint.X + pDx, pPoint.Y + pDy);
        }
    }
}
