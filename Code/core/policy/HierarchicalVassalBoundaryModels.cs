using System;

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
}
