using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    public enum BoundaryDisplayLayer
    {
        Countries = 0,
        Cities = 1
    }

    public enum BoundaryTier
    {
        None = 0,
        City = 1,
        VassalRealm = 2,
        SuzerainSystem = 3
    }

    public enum BoundaryWaterKind
    {
        Land = 0,
        InlandWater = 1,
        Ocean = 2,
        Lava = 3
    }

    public readonly struct BoundaryChunkKey : IEquatable<BoundaryChunkKey>
    {
        public BoundaryChunkKey(int pX, int pY)
        {
            X = pX;
            Y = pY;
        }

        public int X { get; }

        public int Y { get; }

        public bool Equals(BoundaryChunkKey pOther)
        {
            return X == pOther.X && Y == pOther.Y;
        }

        public override bool Equals(object pValue)
        {
            return pValue is BoundaryChunkKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked((X * 397) ^ Y);
        }

        public override string ToString()
        {
            return "(" + X + "," + Y + ")";
        }
    }

    public readonly struct BoundaryChunkBounds : IEquatable<BoundaryChunkBounds>
    {
        public BoundaryChunkBounds(
            int captureMinX,
            int captureMinY,
            int captureMaxXExclusive,
            int captureMaxYExclusive,
            int interiorMinX,
            int interiorMinY,
            int interiorMaxXExclusive,
            int interiorMaxYExclusive)
        {
            CaptureMinX = captureMinX;
            CaptureMinY = captureMinY;
            CaptureMaxXExclusive = captureMaxXExclusive;
            CaptureMaxYExclusive = captureMaxYExclusive;
            InteriorMinX = interiorMinX;
            InteriorMinY = interiorMinY;
            InteriorMaxXExclusive = interiorMaxXExclusive;
            InteriorMaxYExclusive = interiorMaxYExclusive;
        }

        public int CaptureMinX { get; }

        public int CaptureMinY { get; }

        public int CaptureMaxXExclusive { get; }

        public int CaptureMaxYExclusive { get; }

        public int InteriorMinX { get; }

        public int InteriorMinY { get; }

        public int InteriorMaxXExclusive { get; }

        public int InteriorMaxYExclusive { get; }

        public bool Equals(BoundaryChunkBounds pOther)
        {
            return CaptureMinX == pOther.CaptureMinX &&
                   CaptureMinY == pOther.CaptureMinY &&
                   CaptureMaxXExclusive == pOther.CaptureMaxXExclusive &&
                   CaptureMaxYExclusive == pOther.CaptureMaxYExclusive &&
                   InteriorMinX == pOther.InteriorMinX &&
                   InteriorMinY == pOther.InteriorMinY &&
                   InteriorMaxXExclusive == pOther.InteriorMaxXExclusive &&
                   InteriorMaxYExclusive == pOther.InteriorMaxYExclusive;
        }

        public override bool Equals(object pValue)
        {
            return pValue is BoundaryChunkBounds other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = CaptureMinX;
                hash = (hash * 397) ^ CaptureMinY;
                hash = (hash * 397) ^ CaptureMaxXExclusive;
                hash = (hash * 397) ^ CaptureMaxYExclusive;
                hash = (hash * 397) ^ InteriorMinX;
                hash = (hash * 397) ^ InteriorMinY;
                hash = (hash * 397) ^ InteriorMaxXExclusive;
                return (hash * 397) ^ InteriorMaxYExclusive;
            }
        }

        public override string ToString()
        {
            return "capture=(" + CaptureMinX + "," + CaptureMinY + ")-(" +
                   CaptureMaxXExclusive + "," + CaptureMaxYExclusive + ") " +
                   "interior=(" + InteriorMinX + "," + InteriorMinY + ")-(" +
                   InteriorMaxXExclusive + "," + InteriorMaxYExclusive + ")";
        }
    }

    public readonly struct BoundaryCellFacts
    {
        public BoundaryCellFacts(
            int x,
            int y,
            bool isValid,
            BoundaryWaterKind water,
            byte height,
            long systemId,
            long realmId,
            long cityId,
            uint rgba)
        {
            X = x;
            Y = y;
            IsValid = isValid;
            Water = water;
            Height = height;
            SystemId = systemId;
            RealmId = realmId;
            CityId = cityId;
            Rgba = rgba;
        }

        public int X { get; }

        public int Y { get; }

        public bool IsValid { get; }

        public BoundaryWaterKind Water { get; }

        public byte Height { get; }

        public long SystemId { get; }

        public long RealmId { get; }

        public long CityId { get; }

        public uint Rgba { get; }

        public bool IsLand
        {
            get { return IsValid && Water == BoundaryWaterKind.Land; }
        }
    }

    public sealed class BoundaryCellRaster
    {
        private readonly BoundaryCellFacts[] _cells;

        public BoundaryCellRaster(
            int pOriginX,
            int pOriginY,
            int pWidth,
            int pHeight,
            BoundaryCellFacts[] pCells)
        {
            if (pWidth < 0)
                throw new ArgumentOutOfRangeException(nameof(pWidth));
            if (pHeight < 0)
                throw new ArgumentOutOfRangeException(nameof(pHeight));
            if (pCells == null)
                throw new ArgumentNullException(nameof(pCells));
            if (pCells.Length != pWidth * pHeight)
                throw new ArgumentException(
                    "Cell count must match raster dimensions.", nameof(pCells));

            OriginX = pOriginX;
            OriginY = pOriginY;
            Width = pWidth;
            Height = pHeight;
            _cells = pCells;
        }

        public int OriginX { get; }

        public int OriginY { get; }

        public int Width { get; }

        public int Height { get; }

        public int MaxXExclusive
        {
            get { return OriginX + Width; }
        }

        public int MaxYExclusive
        {
            get { return OriginY + Height; }
        }

        public BoundaryCellFacts GetOrInvalid(int pX, int pY)
        {
            int localX = pX - OriginX;
            int localY = pY - OriginY;
            if (localX < 0 || localY < 0 ||
                localX >= Width || localY >= Height)
            {
                return new BoundaryCellFacts(
                    pX, pY, false, BoundaryWaterKind.Land, 0,
                    -1, -1, -1, 0);
            }
            return _cells[localY * Width + localX];
        }
    }

    public readonly struct BoundaryGridPoint :
        IEquatable<BoundaryGridPoint>, IComparable<BoundaryGridPoint>
    {
        public BoundaryGridPoint(int pX, int pY)
        {
            X = pX;
            Y = pY;
        }

        public int X { get; }

        public int Y { get; }

        public int CompareTo(BoundaryGridPoint pOther)
        {
            int xComparison = X.CompareTo(pOther.X);
            return xComparison != 0 ? xComparison : Y.CompareTo(pOther.Y);
        }

        public bool Equals(BoundaryGridPoint pOther)
        {
            return X == pOther.X && Y == pOther.Y;
        }

        public override bool Equals(object pValue)
        {
            return pValue is BoundaryGridPoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked((X * 397) ^ Y);
        }

        public override string ToString()
        {
            return "(" + X + "," + Y + ")";
        }
    }

    public readonly struct BoundaryRawEdge
    {
        public BoundaryRawEdge(
            BoundaryGridPoint pStart,
            BoundaryGridPoint pEnd,
            BoundaryTier pTier,
            long leftOwnerId,
            long rightOwnerId)
        {
            Start = pStart;
            End = pEnd;
            Tier = pTier;
            LeftOwnerId = leftOwnerId;
            RightOwnerId = rightOwnerId;
        }

        public BoundaryGridPoint Start { get; }

        public BoundaryGridPoint End { get; }

        public BoundaryTier Tier { get; }

        public long LeftOwnerId { get; }

        public long RightOwnerId { get; }

        public BoundaryGridPoint Other(BoundaryGridPoint pPoint)
        {
            if (Start.Equals(pPoint))
                return End;
            if (End.Equals(pPoint))
                return Start;
            throw new ArgumentException("Point is not on the edge.", nameof(pPoint));
        }
    }

    public sealed class BoundaryChain
    {
        public BoundaryChain(
            IReadOnlyList<BoundaryGridPoint> pPoints,
            IReadOnlyList<BoundaryRawEdge> pEdges,
            bool pClosed)
        {
            Points = pPoints ?? throw new ArgumentNullException(nameof(pPoints));
            Edges = pEdges ?? throw new ArgumentNullException(nameof(pEdges));
            Closed = pClosed;
        }

        public IReadOnlyList<BoundaryGridPoint> Points { get; }

        public IReadOnlyList<BoundaryRawEdge> Edges { get; }

        public bool Closed { get; }

        public BoundaryTier Tier
        {
            get { return Edges.Count == 0 ? BoundaryTier.None : Edges[0].Tier; }
        }
    }

    public sealed class BoundaryTopologyDraft
    {
        public BoundaryTopologyDraft(
            IReadOnlyList<BoundaryRawEdge> pRawEdges,
            IReadOnlyList<BoundaryChain> pOpenChains,
            IReadOnlyList<BoundaryChain> pClosedChains,
            HashSet<BoundaryGridPoint> pProtectedVertices)
        {
            RawEdges = pRawEdges;
            OpenChains = pOpenChains;
            ClosedChains = pClosedChains;
            ProtectedVertices = pProtectedVertices;
        }

        public IReadOnlyList<BoundaryRawEdge> RawEdges { get; }

        public IReadOnlyList<BoundaryChain> OpenChains { get; }

        public IReadOnlyList<BoundaryChain> ClosedChains { get; }

        public HashSet<BoundaryGridPoint> ProtectedVertices { get; }
    }

    public readonly struct BoundaryFloatPoint : IEquatable<BoundaryFloatPoint>
    {
        public BoundaryFloatPoint(float pX, float pY)
        {
            X = pX;
            Y = pY;
        }

        public float X { get; }

        public float Y { get; }

        public bool Equals(BoundaryFloatPoint pOther)
        {
            return X.Equals(pOther.X) && Y.Equals(pOther.Y);
        }

        public override bool Equals(object pValue)
        {
            return pValue is BoundaryFloatPoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked((X.GetHashCode() * 397) ^ Y.GetHashCode());
        }
    }

    public readonly struct BoundaryGridEdgeKey : IEquatable<BoundaryGridEdgeKey>
    {
        public BoundaryGridEdgeKey(
            BoundaryGridPoint pFirst,
            BoundaryGridPoint pSecond)
        {
            if (pFirst.CompareTo(pSecond) <= 0)
            {
                First = pFirst;
                Second = pSecond;
            }
            else
            {
                First = pSecond;
                Second = pFirst;
            }
        }

        public BoundaryGridPoint First { get; }

        public BoundaryGridPoint Second { get; }

        public bool Equals(BoundaryGridEdgeKey pOther)
        {
            return First.Equals(pOther.First) && Second.Equals(pOther.Second);
        }

        public override bool Equals(object pValue)
        {
            return pValue is BoundaryGridEdgeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked((First.GetHashCode() * 397) ^ Second.GetHashCode());
        }
    }

    public sealed class BoundaryPoliticalRiverChain
    {
        public BoundaryPoliticalRiverChain(
            IReadOnlyList<BoundaryFloatPoint> pPoints,
            BoundaryTier pTier,
            long pLeftOwnerId,
            long pRightOwnerId)
        {
            Points = pPoints;
            Tier = pTier;
            LeftOwnerId = pLeftOwnerId;
            RightOwnerId = pRightOwnerId;
        }

        public IReadOnlyList<BoundaryFloatPoint> Points { get; }

        public BoundaryTier Tier { get; }

        public long LeftOwnerId { get; }

        public long RightOwnerId { get; }
    }

    public sealed class BoundaryRiverDraft
    {
        public BoundaryRiverDraft(
            IReadOnlyList<BoundaryPoliticalRiverChain> pPoliticalChains,
            HashSet<BoundaryGridEdgeKey> pShoreEdgesToSuppress)
        {
            PoliticalChains = pPoliticalChains;
            ShoreEdgesToSuppress = pShoreEdgesToSuppress;
        }

        public IReadOnlyList<BoundaryPoliticalRiverChain> PoliticalChains { get; }

        public HashSet<BoundaryGridEdgeKey> ShoreEdgesToSuppress { get; }
    }
}
