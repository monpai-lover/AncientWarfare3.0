using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    public readonly struct CultiwayWallPoint : IEquatable<CultiwayWallPoint>
    {
        public CultiwayWallPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public bool Equals(CultiwayWallPoint other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object value)
        {
            return value is CultiwayWallPoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked(X * 397 ^ Y);
        }

        public override string ToString()
        {
            return "(" + X + "," + Y + ")";
        }
    }

    public readonly struct CultiwayWallBounds
    {
        public CultiwayWallBounds(int cx, int cy, int hx, int hy)
        {
            CenterX = cx;
            CenterY = cy;
            HalfWidth = hx;
            HalfHeight = hy;
        }

        public int CenterX { get; }
        public int CenterY { get; }
        public int HalfWidth { get; }
        public int HalfHeight { get; }
    }

    public sealed class CultiwayWallGeometryInput
    {
        public CultiwayWallGeometryInput(int mapWidth, int mapHeight,
            CultiwayWallPoint center, CultiwayWallBounds bounds,
            IEnumerable<CultiwayWallPoint> cityLand,
            IEnumerable<CultiwayWallPoint> passable,
            IEnumerable<CultiwayWallPoint> roads,
            IEnumerable<CultiwayWallPoint> docks,
            int wallWidth, bool gates)
        {
            if (mapWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(mapWidth));
            if (mapHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(mapHeight));
            if (wallWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(wallWidth));

            MapWidth = mapWidth;
            MapHeight = mapHeight;
            Center = center;
            Bounds = bounds;
            CityLand = new HashSet<CultiwayWallPoint>(
                cityLand ?? Array.Empty<CultiwayWallPoint>());
            Passable = new HashSet<CultiwayWallPoint>(
                passable ?? Array.Empty<CultiwayWallPoint>());
            Roads = new HashSet<CultiwayWallPoint>(
                roads ?? Array.Empty<CultiwayWallPoint>());
            Docks = new HashSet<CultiwayWallPoint>(
                docks ?? Array.Empty<CultiwayWallPoint>());
            WallWidth = wallWidth;
            Gates = gates;
        }

        public int MapWidth { get; }
        public int MapHeight { get; }
        public CultiwayWallPoint Center { get; }
        public CultiwayWallBounds Bounds { get; }
        public HashSet<CultiwayWallPoint> CityLand { get; }
        public HashSet<CultiwayWallPoint> Passable { get; }
        public HashSet<CultiwayWallPoint> Roads { get; }
        public HashSet<CultiwayWallPoint> Docks { get; }
        public int WallWidth { get; }
        public bool Gates { get; }
    }

    public static class CultiwayStyleWallGeometryRules
    {
        private const int ExitHalf = 1;
        private const int RoadSearchRadius = 6;
        private const int DockPassageDistance = 8;

        private static readonly CultiwayWallPoint[] CardinalDirections =
        {
            new CultiwayWallPoint(-1, 0),
            new CultiwayWallPoint(1, 0),
            new CultiwayWallPoint(0, -1),
            new CultiwayWallPoint(0, 1)
        };

        public static IReadOnlyList<CultiwayWallPoint> Compute(
            CultiwayWallGeometryInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            HashSet<CultiwayWallPoint> coreLand = GetCoreLand(input);
            HashSet<CultiwayWallPoint> remaining = IntersectBounds(
                coreLand, input);
            if (remaining.Count == 0)
                return Array.Empty<CultiwayWallPoint>();

            GetClippedBounds(input, out int minX, out int maxX,
                out int minY, out int maxY);
            HashSet<CultiwayWallPoint> exterior = FloodExterior(
                remaining, minX, maxX, minY, maxY);
            var walls = new HashSet<CultiwayWallPoint>();
            var outerLayer = new HashSet<CultiwayWallPoint>();

            for (int layer = 0;
                 layer < input.WallWidth && remaining.Count > 0;
                 layer++)
            {
                HashSet<CultiwayWallPoint> boundary = remaining
                    .Where(point => IsOuterBoundary(point, exterior,
                        minX, maxX, minY, maxY))
                    .ToHashSet();
                if (boundary.Count == 0) break;

                HashSet<CultiwayWallPoint> sealedBoundary = SealDiagonalGaps(
                    boundary, coreLand, input.Passable, input.Bounds,
                    minX, maxX, minY, maxY);
                if (layer == 0) outerLayer.UnionWith(sealedBoundary);
                walls.UnionWith(sealedBoundary);

                foreach (CultiwayWallPoint point in boundary)
                {
                    remaining.Remove(point);
                    exterior.Add(point);
                }
                foreach (CultiwayWallPoint point in sealedBoundary)
                {
                    remaining.Remove(point);
                    exterior.Add(point);
                }
            }

            if (input.Gates)
            {
                CarveLandGates(walls, input);
                CarveDockPassages(walls, input);
            }
            EnsureCoreReachable(walls, outerLayer, coreLand, input);

            return walls.OrderBy(point => point.X)
                .ThenBy(point => point.Y).ToArray();
        }

        private static HashSet<CultiwayWallPoint> GetCoreLand(
            CultiwayWallGeometryInput input)
        {
            HashSet<CultiwayWallPoint> territory = input.CityLand
                .Where(point => InMap(point, input)).ToHashSet();
            var result = new HashSet<CultiwayWallPoint>();
            if (territory.Count == 0) return result;

            CultiwayWallPoint seed;
            if (territory.Contains(input.Center))
            {
                seed = input.Center;
            }
            else
            {
                seed = territory.OrderBy(point => DistanceSquared(
                        point, input.Center))
                    .ThenBy(point => point.X)
                    .ThenBy(point => point.Y).First();
            }

            var queue = new Queue<CultiwayWallPoint>();
            result.Add(seed);
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                CultiwayWallPoint current = queue.Dequeue();
                foreach (CultiwayWallPoint direction in CardinalDirections)
                {
                    CultiwayWallPoint neighbour = Offset(current,
                        direction.X, direction.Y);
                    if (!territory.Contains(neighbour) ||
                        !result.Add(neighbour)) continue;
                    queue.Enqueue(neighbour);
                }
            }
            return result;
        }

        private static HashSet<CultiwayWallPoint> IntersectBounds(
            HashSet<CultiwayWallPoint> coreLand,
            CultiwayWallGeometryInput input)
        {
            GetClippedBounds(input, out int minX, out int maxX,
                out int minY, out int maxY);
            return coreLand.Where(point => point.X >= minX &&
                point.X <= maxX && point.Y >= minY && point.Y <= maxY)
                .ToHashSet();
        }

        private static HashSet<CultiwayWallPoint> FloodExterior(
            HashSet<CultiwayWallPoint> land, int minX, int maxX,
            int minY, int maxY)
        {
            var exterior = new HashSet<CultiwayWallPoint>();
            var queue = new Queue<CultiwayWallPoint>();
            for (int x = minX; x <= maxX; x++)
            {
                AddExterior(new CultiwayWallPoint(x, minY), land, exterior,
                    queue, minX, maxX, minY, maxY);
                AddExterior(new CultiwayWallPoint(x, maxY), land, exterior,
                    queue, minX, maxX, minY, maxY);
            }
            for (int y = minY + 1; y < maxY; y++)
            {
                AddExterior(new CultiwayWallPoint(minX, y), land, exterior,
                    queue, minX, maxX, minY, maxY);
                AddExterior(new CultiwayWallPoint(maxX, y), land, exterior,
                    queue, minX, maxX, minY, maxY);
            }

            while (queue.Count > 0)
            {
                CultiwayWallPoint current = queue.Dequeue();
                foreach (CultiwayWallPoint direction in CardinalDirections)
                {
                    AddExterior(Offset(current, direction.X, direction.Y),
                        land, exterior, queue,
                        minX, maxX, minY, maxY);
                }
            }
            return exterior;
        }

        private static void AddExterior(CultiwayWallPoint point,
            HashSet<CultiwayWallPoint> land,
            HashSet<CultiwayWallPoint> exterior,
            Queue<CultiwayWallPoint> queue,
            int minX, int maxX, int minY, int maxY)
        {
            if (point.X < minX || point.X > maxX ||
                point.Y < minY || point.Y > maxY ||
                land.Contains(point) || !exterior.Add(point)) return;
            queue.Enqueue(point);
        }

        private static bool IsOuterBoundary(CultiwayWallPoint point,
            HashSet<CultiwayWallPoint> exterior,
            int minX, int maxX, int minY, int maxY)
        {
            if (point.X == minX || point.X == maxX ||
                point.Y == minY || point.Y == maxY) return true;
            return CardinalDirections.Any(direction => exterior.Contains(
                Offset(point, direction.X, direction.Y)));
        }

        private static HashSet<CultiwayWallPoint> SealDiagonalGaps(
            HashSet<CultiwayWallPoint> boundary,
            HashSet<CultiwayWallPoint> coreLand,
            HashSet<CultiwayWallPoint> passable,
            CultiwayWallBounds bounds,
            int minX, int maxX, int minY, int maxY)
        {
            var result = new HashSet<CultiwayWallPoint>(boundary);
            var additions = new List<CultiwayWallPoint>();
            foreach (CultiwayWallPoint point in boundary)
            {
                TrySealDiagonal(point, 1, 1, boundary, coreLand, passable,
                    bounds, additions, minX, maxX, minY, maxY);
                TrySealDiagonal(point, 1, -1, boundary, coreLand, passable,
                    bounds, additions, minX, maxX, minY, maxY);
            }
            result.UnionWith(additions);
            return result;
        }

        private static void TrySealDiagonal(CultiwayWallPoint point,
            int dx, int dy, HashSet<CultiwayWallPoint> walls,
            HashSet<CultiwayWallPoint> coreLand,
            HashSet<CultiwayWallPoint> passable,
            CultiwayWallBounds bounds, List<CultiwayWallPoint> additions,
            int minX, int maxX, int minY, int maxY)
        {
            CultiwayWallPoint diagonal = Offset(point, dx, dy);
            if (!walls.Contains(diagonal)) return;

            CultiwayWallPoint? horizontal = GetLandPoint(
                point.X + dx, point.Y, coreLand,
                minX, maxX, minY, maxY);
            CultiwayWallPoint? vertical = GetLandPoint(
                point.X, point.Y + dy, coreLand,
                minX, maxX, minY, maxY);
            if (horizontal.HasValue && walls.Contains(horizontal.Value) ||
                vertical.HasValue && walls.Contains(vertical.Value)) return;

            CultiwayWallPoint? bridge;
            if (!horizontal.HasValue) bridge = vertical;
            else if (!vertical.HasValue) bridge = horizontal;
            else
            {
                int horizontalDistance = Manhattan(horizontal.Value,
                    bounds.CenterX, bounds.CenterY);
                int verticalDistance = Manhattan(vertical.Value,
                    bounds.CenterX, bounds.CenterY);
                bridge = horizontalDistance >= verticalDistance
                    ? horizontal : vertical;
            }
            if (bridge.HasValue && passable.Contains(bridge.Value))
                additions.Add(bridge.Value);
        }

        private static CultiwayWallPoint? GetLandPoint(int x, int y,
            HashSet<CultiwayWallPoint> coreLand,
            int minX, int maxX, int minY, int maxY)
        {
            var point = new CultiwayWallPoint(x, y);
            if (x < minX || x > maxX || y < minY || y > maxY ||
                !coreLand.Contains(point)) return null;
            return point;
        }

        private static void CarveLandGates(HashSet<CultiwayWallPoint> walls,
            CultiwayWallGeometryInput input)
        {
            if (walls.Count == 0) return;
            var removed = new HashSet<CultiwayWallPoint>();
            bool north = CarveLandGate(walls, input, 0, 1, removed);
            bool east = CarveLandGate(walls, input, 1, 0, removed);
            bool south = CarveLandGate(walls, input, 0, -1, removed);
            bool west = CarveLandGate(walls, input, -1, 0, removed);
            if (!north && !east)
                CarveLandGate(walls, input, 1, 1, removed);
            if (!east && !south)
                CarveLandGate(walls, input, 1, -1, removed);
            if (!south && !west)
                CarveLandGate(walls, input, -1, -1, removed);
            if (!west && !north)
                CarveLandGate(walls, input, -1, 1, removed);
            walls.ExceptWith(removed);
        }

        private static bool CarveLandGate(HashSet<CultiwayWallPoint> walls,
            CultiwayWallGeometryInput input, int directionX, int directionY,
            HashSet<CultiwayWallPoint> removed)
        {
            CultiwayWallPoint? roadGate = SelectGate(walls, input,
                directionX, directionY, true);
            CultiwayWallPoint? gate = roadGate ?? SelectGate(walls, input,
                directionX, directionY, false);
            if (!gate.HasValue) return false;
            MarkPassage(walls, gate.Value, removed);
            return true;
        }

        private static CultiwayWallPoint? SelectGate(
            HashSet<CultiwayWallPoint> walls,
            CultiwayWallGeometryInput input,
            int directionX, int directionY, bool requireRoad)
        {
            CultiwayWallPoint? gate = null;
            int bestLateral = int.MaxValue;
            int bestProjection = int.MinValue;
            foreach (CultiwayWallPoint point in walls)
            {
                if (!IsInDirection(point, input.Bounds,
                        directionX, directionY,
                        out int lateral, out int projection) ||
                    !input.Passable.Contains(point) ||
                    requireRoad && !HasRoadNearby(point, input.Roads) ||
                    !HasPassableOutside(point, directionX, directionY,
                        input.Passable)) continue;
                if (lateral > bestLateral ||
                    lateral == bestLateral && projection <= bestProjection)
                    continue;
                bestLateral = lateral;
                bestProjection = projection;
                gate = point;
            }
            return gate;
        }

        private static bool IsInDirection(CultiwayWallPoint point,
            CultiwayWallBounds bounds, int directionX, int directionY,
            out int lateral, out int projection)
        {
            int dx = point.X - bounds.CenterX;
            int dy = point.Y - bounds.CenterY;
            projection = dx * directionX + dy * directionY;
            lateral = Math.Abs(dx * directionY - dy * directionX);
            return projection > 0 && lateral <= projection;
        }

        private static bool HasRoadNearby(CultiwayWallPoint point,
            HashSet<CultiwayWallPoint> roads)
        {
            int radiusSquared = RoadSearchRadius * RoadSearchRadius;
            return roads.Any(road => DistanceSquared(point, road) <=
                radiusSquared);
        }

        private static bool HasPassableOutside(CultiwayWallPoint point,
            int directionX, int directionY,
            HashSet<CultiwayWallPoint> passable)
        {
            if (directionX != 0 && directionY != 0)
            {
                return passable.Contains(Offset(point, directionX, 0)) ||
                    passable.Contains(Offset(point, 0, directionY));
            }
            return passable.Contains(Offset(point, directionX, directionY));
        }

        private static void MarkPassage(HashSet<CultiwayWallPoint> walls,
            CultiwayWallPoint passage,
            HashSet<CultiwayWallPoint> removed)
        {
            foreach (CultiwayWallPoint point in walls)
            {
                if (Math.Abs(point.X - passage.X) <= ExitHalf &&
                    Math.Abs(point.Y - passage.Y) <= ExitHalf)
                    removed.Add(point);
            }
        }

        private static void CarveDockPassages(
            HashSet<CultiwayWallPoint> walls,
            CultiwayWallGeometryInput input)
        {
            if (walls.Count == 0 || input.Docks.Count == 0) return;
            int maxDistanceSquared = DockPassageDistance *
                DockPassageDistance;
            var removed = new HashSet<CultiwayWallPoint>();
            foreach (CultiwayWallPoint dock in input.Docks)
            {
                CultiwayWallPoint? passage = walls
                    .Select(point => new
                    {
                        Point = point,
                        Distance = DistanceSquared(point, dock)
                    })
                    .Where(candidate => candidate.Distance <=
                        maxDistanceSquared)
                    .OrderBy(candidate => candidate.Distance)
                    .ThenBy(candidate => candidate.Point.X)
                    .ThenBy(candidate => candidate.Point.Y)
                    .Select(candidate => (CultiwayWallPoint?)candidate.Point)
                    .FirstOrDefault();
                if (passage.HasValue)
                    MarkPassage(walls, passage.Value, removed);
            }
            walls.ExceptWith(removed);
        }

        private static void EnsureCoreReachable(
            HashSet<CultiwayWallPoint> walls,
            HashSet<CultiwayWallPoint> outerLayer,
            HashSet<CultiwayWallPoint> coreLand,
            CultiwayWallGeometryInput input)
        {
            CultiwayWallPoint? target = FindCoreTarget(coreLand, input);
            if (!target.HasValue) return;

            List<CultiwayWallPoint> sources = outerLayer
                .Where(point => input.Passable.Contains(point) &&
                    HasExteriorLandNeighbour(point, coreLand, input))
                .OrderBy(point => point.X).ThenBy(point => point.Y).ToList();
            if (sources.Count == 0) return;

            var distances = new Dictionary<CultiwayWallPoint, int>();
            var previous = new Dictionary<CultiwayWallPoint,
                CultiwayWallPoint>();
            var queue = new LinkedList<CultiwayWallPoint>();
            foreach (CultiwayWallPoint source in sources)
            {
                int distance = walls.Contains(source) ? 1 : 0;
                if (distances.TryGetValue(source, out int current) &&
                    current <= distance) continue;
                distances[source] = distance;
                previous.Remove(source);
                if (distance == 0) queue.AddFirst(source);
                else queue.AddLast(source);
            }

            while (queue.Count > 0)
            {
                CultiwayWallPoint current = queue.First.Value;
                queue.RemoveFirst();
                if (current.Equals(target.Value)) break;
                foreach (CultiwayWallPoint direction in CardinalDirections)
                {
                    RelaxNeighbour(Offset(current, direction.X, direction.Y),
                        current, coreLand, walls, input.Passable,
                        distances, previous, queue);
                }
            }

            if (!distances.TryGetValue(target.Value, out int wallsCrossed) ||
                wallsCrossed == 0) return;

            var crossedWalls = new HashSet<CultiwayWallPoint>();
            CultiwayWallPoint path = target.Value;
            while (true)
            {
                if (walls.Contains(path)) crossedWalls.Add(path);
                if (!previous.TryGetValue(path, out path)) break;
            }
            foreach (CultiwayWallPoint crossed in crossedWalls)
            {
                walls.RemoveWhere(point =>
                    Math.Abs(point.X - crossed.X) <= ExitHalf &&
                    Math.Abs(point.Y - crossed.Y) <= ExitHalf);
            }
        }

        private static void RelaxNeighbour(CultiwayWallPoint neighbour,
            CultiwayWallPoint from,
            HashSet<CultiwayWallPoint> coreLand,
            HashSet<CultiwayWallPoint> walls,
            HashSet<CultiwayWallPoint> passable,
            Dictionary<CultiwayWallPoint, int> distances,
            Dictionary<CultiwayWallPoint, CultiwayWallPoint> previous,
            LinkedList<CultiwayWallPoint> queue)
        {
            if (!coreLand.Contains(neighbour) ||
                !passable.Contains(neighbour)) return;
            bool wall = walls.Contains(neighbour);
            int distance = distances[from] + (wall ? 1 : 0);
            if (distances.TryGetValue(neighbour, out int current) &&
                current <= distance) return;
            distances[neighbour] = distance;
            previous[neighbour] = from;
            if (wall) queue.AddLast(neighbour);
            else queue.AddFirst(neighbour);
        }

        private static CultiwayWallPoint? FindCoreTarget(
            HashSet<CultiwayWallPoint> coreLand,
            CultiwayWallGeometryInput input)
        {
            if (coreLand.Contains(input.Center) &&
                input.Passable.Contains(input.Center)) return input.Center;
            return coreLand.Where(input.Passable.Contains)
                .OrderBy(point => Manhattan(point,
                    input.Center.X, input.Center.Y))
                .ThenBy(point => point.X).ThenBy(point => point.Y)
                .Select(point => (CultiwayWallPoint?)point)
                .FirstOrDefault();
        }

        private static bool HasExteriorLandNeighbour(
            CultiwayWallPoint point,
            HashSet<CultiwayWallPoint> coreLand,
            CultiwayWallGeometryInput input)
        {
            return CardinalDirections.Any(direction => IsExteriorLand(
                Offset(point, direction.X, direction.Y), coreLand, input));
        }

        private static bool IsExteriorLand(CultiwayWallPoint point,
            HashSet<CultiwayWallPoint> coreLand,
            CultiwayWallGeometryInput input)
        {
            if (!InMap(point, input) || !input.Passable.Contains(point))
                return false;
            CultiwayWallBounds bounds = input.Bounds;
            bool outsideBounds = point.X < bounds.CenterX - bounds.HalfWidth ||
                point.X > bounds.CenterX + bounds.HalfWidth ||
                point.Y < bounds.CenterY - bounds.HalfHeight ||
                point.Y > bounds.CenterY + bounds.HalfHeight;
            return outsideBounds || !coreLand.Contains(point);
        }

        private static void GetClippedBounds(CultiwayWallGeometryInput input,
            out int minX, out int maxX, out int minY, out int maxY)
        {
            minX = Math.Max(0,
                input.Bounds.CenterX - Math.Max(0, input.Bounds.HalfWidth));
            maxX = Math.Min(input.MapWidth - 1,
                input.Bounds.CenterX + Math.Max(0, input.Bounds.HalfWidth));
            minY = Math.Max(0,
                input.Bounds.CenterY - Math.Max(0, input.Bounds.HalfHeight));
            maxY = Math.Min(input.MapHeight - 1,
                input.Bounds.CenterY + Math.Max(0, input.Bounds.HalfHeight));
        }

        private static bool InMap(CultiwayWallPoint point,
            CultiwayWallGeometryInput input)
        {
            return point.X >= 0 && point.X < input.MapWidth &&
                point.Y >= 0 && point.Y < input.MapHeight;
        }

        private static CultiwayWallPoint Offset(CultiwayWallPoint point,
            int dx, int dy)
        {
            return new CultiwayWallPoint(point.X + dx, point.Y + dy);
        }

        private static int DistanceSquared(CultiwayWallPoint left,
            CultiwayWallPoint right)
        {
            int dx = left.X - right.X;
            int dy = left.Y - right.Y;
            return dx * dx + dy * dy;
        }

        private static int Manhattan(CultiwayWallPoint point,
            int centerX, int centerY)
        {
            return Math.Abs(point.X - centerX) +
                Math.Abs(point.Y - centerY);
        }
    }
}
