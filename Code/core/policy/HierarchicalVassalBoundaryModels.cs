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

    internal enum BoundarySafeBoundsStatus
    {
        Invalid = 0,
        Empty = 1,
        Valid = 2
    }

    internal static class BoundarySafeBoundsGate
    {
        private const long MaximumDimension = 262144L;
        private const long MaximumArea = 262144L;

        public static BoundarySafeBoundsStatus Evaluate(
            BoundaryChunkBounds pBounds)
        {
            long minimumX = pBounds.InteriorMinX;
            long minimumY = pBounds.InteriorMinY;
            long maximumX = pBounds.InteriorMaxXExclusive;
            long maximumY = pBounds.InteriorMaxYExclusive;
            if (maximumX < minimumX || maximumY < minimumY)
                return BoundarySafeBoundsStatus.Invalid;

            try
            {
                long width = checked(maximumX - minimumX);
                long height = checked(maximumY - minimumY);
                if (width > MaximumDimension || height > MaximumDimension)
                    return BoundarySafeBoundsStatus.Invalid;
                if (width == 0L || height == 0L)
                    return BoundarySafeBoundsStatus.Empty;
                return checked(width * height) <= MaximumArea
                    ? BoundarySafeBoundsStatus.Valid
                    : BoundarySafeBoundsStatus.Invalid;
            }
            catch (OverflowException)
            {
                return BoundarySafeBoundsStatus.Invalid;
            }
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
            long cellCount = (long)pWidth * pHeight;
            if (cellCount > int.MaxValue)
                throw new ArgumentOutOfRangeException(
                    nameof(pWidth), "Raster dimensions exceed array capacity.");
            if ((long)pOriginX + pWidth > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(pOriginX));
            if ((long)pOriginY + pHeight > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(pOriginY));
            if (pCells.Length != (int)cellCount)
                throw new ArgumentException(
                    "Cell count must match raster dimensions.", nameof(pCells));

            OriginX = pOriginX;
            OriginY = pOriginY;
            Width = pWidth;
            Height = pHeight;
            _cells = new BoundaryCellFacts[pCells.Length];
            Array.Copy(pCells, _cells, pCells.Length);
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

    public readonly struct BoundaryFloat3 : IEquatable<BoundaryFloat3>
    {
        public BoundaryFloat3(float pX, float pY, float pZ)
        {
            X = pX;
            Y = pY;
            Z = pZ;
        }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        public bool Equals(BoundaryFloat3 pOther)
        {
            return X.Equals(pOther.X) && Y.Equals(pOther.Y) &&
                   Z.Equals(pOther.Z);
        }

        public override bool Equals(object pValue)
        {
            return pValue is BoundaryFloat3 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                return (hash * 397) ^ Z.GetHashCode();
            }
        }
    }

    public sealed class BoundaryHeightDraft
    {
        public const int MaximumDimension = 36;
        public const int MaximumHalo = 2;

        private readonly byte[] _samples;
        private readonly IReadOnlyList<byte> _readOnlySamples;

        public BoundaryHeightDraft(
            byte[] pSamples,
            int pWidth,
            int pHeight,
            int pCaptureWorldOriginX,
            int pCaptureWorldOriginY,
            int pHalo,
            long pTerrainRevision)
        {
            if (pSamples == null)
                throw new ArgumentNullException(nameof(pSamples));
            if (pWidth <= 0 || pWidth > MaximumDimension)
                throw new ArgumentOutOfRangeException(nameof(pWidth));
            if (pHeight <= 0 || pHeight > MaximumDimension)
                throw new ArgumentOutOfRangeException(nameof(pHeight));
            if (pHalo < 0 || pHalo > MaximumHalo ||
                pHalo * 2 >= pWidth || pHalo * 2 >= pHeight)
                throw new ArgumentOutOfRangeException(nameof(pHalo));
            if (pTerrainRevision < 0L)
                throw new ArgumentOutOfRangeException(nameof(pTerrainRevision));

            long chunkWorldOriginX = (long)pCaptureWorldOriginX + pHalo;
            long chunkWorldOriginY = (long)pCaptureWorldOriginY + pHalo;
            if (chunkWorldOriginX < int.MinValue ||
                chunkWorldOriginX > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pCaptureWorldOriginX));
            }
            if (chunkWorldOriginY < int.MinValue ||
                chunkWorldOriginY > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pCaptureWorldOriginY));
            }

            int expectedLength = checked(pWidth * pHeight);
            if (pSamples.Length != expectedLength)
                throw new ArgumentException(
                    "Sample count must match height draft dimensions.",
                    nameof(pSamples));

            _samples = new byte[pSamples.Length];
            Array.Copy(pSamples, _samples, pSamples.Length);
            _readOnlySamples = Array.AsReadOnly(_samples);
            Width = pWidth;
            Height = pHeight;
            CaptureWorldOriginX = pCaptureWorldOriginX;
            CaptureWorldOriginY = pCaptureWorldOriginY;
            ChunkWorldOriginX = (int)chunkWorldOriginX;
            ChunkWorldOriginY = (int)chunkWorldOriginY;
            Halo = pHalo;
            TerrainRevision = pTerrainRevision;
        }

        public int Width { get; }

        public int Height { get; }

        public int CaptureWorldOriginX { get; }

        public int CaptureWorldOriginY { get; }

        public int ChunkWorldOriginX { get; }

        public int ChunkWorldOriginY { get; }

        public int Halo { get; }

        public long TerrainRevision { get; }

        public IReadOnlyList<byte> Samples
        {
            get { return _readOnlySamples; }
        }

        public int Index(int pX, int pY)
        {
            if (pX < 0 || pX >= Width)
                throw new ArgumentOutOfRangeException(nameof(pX));
            if (pY < 0 || pY >= Height)
                throw new ArgumentOutOfRangeException(nameof(pY));
            return pY * Width + pX;
        }

        public int IndexForWorldCell(int pWorldX, int pWorldY)
        {
            GetLocalWorldCell(pWorldX, pWorldY, out int localX, out int localY);
            return Index(localX, localY);
        }

        public BoundaryFloatPoint UvForWorldCell(int pWorldX, int pWorldY)
        {
            GetLocalWorldCell(pWorldX, pWorldY, out int localX, out int localY);
            return new BoundaryFloatPoint(
                (localX + 0.5f) / Width,
                (localY + 0.5f) / Height);
        }

        internal byte SampleAtUnchecked(int pX, int pY)
        {
            return _samples[pY * Width + pX];
        }

        private void GetLocalWorldCell(
            int pWorldX,
            int pWorldY,
            out int pLocalX,
            out int pLocalY)
        {
            long localX = (long)pWorldX - CaptureWorldOriginX;
            long localY = (long)pWorldY - CaptureWorldOriginY;
            if (localX < 0L || localX >= Width)
                throw new ArgumentOutOfRangeException(nameof(pWorldX));
            if (localY < 0L || localY >= Height)
                throw new ArgumentOutOfRangeException(nameof(pWorldY));
            pLocalX = (int)localX;
            pLocalY = (int)localY;
        }
    }

    public sealed class BoundaryChunkDraftSet
    {
        private readonly BoundaryHeightDraft _heightDraft;

        public BoundaryChunkDraftSet(BoundaryHeightDraft pHeightDraft)
        {
            _heightDraft = pHeightDraft ??
                           throw new ArgumentNullException(nameof(pHeightDraft));
        }

        public BoundaryHeightDraft CountryHeightDraft
        {
            get { return _heightDraft; }
        }

        public BoundaryHeightDraft CityHeightDraft
        {
            get { return _heightDraft; }
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

    public readonly struct BoundaryCurveOptions
    {
        public BoundaryCurveOptions(
            BoundaryTier pTier,
            long leftOwnerId,
            long rightOwnerId,
            float maximumDeviation,
            bool closed,
            bool allowRiverWater)
        {
            if (float.IsNaN(maximumDeviation) ||
                float.IsInfinity(maximumDeviation) || maximumDeviation < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDeviation));
            }
            Tier = pTier;
            LeftOwnerId = leftOwnerId;
            RightOwnerId = rightOwnerId;
            MaximumDeviation = maximumDeviation;
            Closed = closed;
            AllowRiverWater = allowRiverWater;
        }

        public BoundaryTier Tier { get; }

        public long LeftOwnerId { get; }

        public long RightOwnerId { get; }

        public float MaximumDeviation { get; }

        public bool Closed { get; }

        public bool AllowRiverWater { get; }
    }

    public sealed class BoundaryCurveDraft
    {
        public BoundaryCurveDraft(
            IReadOnlyList<BoundaryFloatPoint> pPoints,
            bool pClosed,
            bool pUsedRawFallback,
            float pTangentScale)
            : this(pPoints, pClosed, pUsedRawFallback, pTangentScale,
                default(BoundaryFloatPoint), default(BoundaryFloatPoint))
        {
        }

        public BoundaryCurveDraft(
            IReadOnlyList<BoundaryFloatPoint> pPoints,
            bool pClosed,
            bool pUsedRawFallback,
            float pTangentScale,
            BoundaryFloatPoint pStartTangent,
            BoundaryFloatPoint pEndTangent)
        {
            Points = pPoints;
            Closed = pClosed;
            UsedRawFallback = pUsedRawFallback;
            TangentScale = pTangentScale;
            StartTangent = pStartTangent;
            EndTangent = pEndTangent;
            IsValid = HasTwoDistinctFinitePoints(pPoints);
        }

        public IReadOnlyList<BoundaryFloatPoint> Points { get; }

        public bool Closed { get; }

        public bool UsedRawFallback { get; }

        public float TangentScale { get; }

        public BoundaryFloatPoint StartTangent { get; }

        public BoundaryFloatPoint EndTangent { get; }

        public bool IsValid { get; }

        private static bool HasTwoDistinctFinitePoints(
            IReadOnlyList<BoundaryFloatPoint> pPoints)
        {
            if (pPoints == null || pPoints.Count < 2)
                return false;
            BoundaryFloatPoint first = pPoints[0];
            if (float.IsNaN(first.X) || float.IsInfinity(first.X) ||
                float.IsNaN(first.Y) || float.IsInfinity(first.Y))
            {
                return false;
            }
            bool hasDistinctPoint = false;
            for (int i = 1; i < pPoints.Count; i++)
            {
                BoundaryFloatPoint point = pPoints[i];
                if (float.IsNaN(point.X) || float.IsInfinity(point.X) ||
                    float.IsNaN(point.Y) || float.IsInfinity(point.Y))
                {
                    return false;
                }
                if (!point.Equals(first))
                    hasDistinctPoint = true;
            }
            return hasDistinctPoint;
        }
    }
}
