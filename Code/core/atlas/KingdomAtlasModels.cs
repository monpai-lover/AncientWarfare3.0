using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.atlas
{
    internal enum KingdomAtlasNodeKind
    {
        City = 0,
        VassalStart = 1,
        VassalEnd = 2
    }

    internal readonly struct KingdomAtlasColor : IEquatable<KingdomAtlasColor>
    {
        public KingdomAtlasColor(byte pRed, byte pGreen, byte pBlue, byte pAlpha = 255)
        {
            Red = pRed; Green = pGreen; Blue = pBlue; Alpha = pAlpha;
        }
        public byte Red { get; }
        public byte Green { get; }
        public byte Blue { get; }
        public byte Alpha { get; }
        public bool Equals(KingdomAtlasColor pOther) => Red == pOther.Red &&
            Green == pOther.Green && Blue == pOther.Blue && Alpha == pOther.Alpha;
        public override bool Equals(object pObject) => pObject is KingdomAtlasColor other && Equals(other);
        public override int GetHashCode() => (Red << 24) | (Green << 16) | (Blue << 8) | Alpha;
        public static bool operator ==(KingdomAtlasColor pLeft, KingdomAtlasColor pRight) => pLeft.Equals(pRight);
        public static bool operator !=(KingdomAtlasColor pLeft, KingdomAtlasColor pRight) => !pLeft.Equals(pRight);
    }

    internal readonly struct KingdomAtlasZoneCell : IEquatable<KingdomAtlasZoneCell>
    {
        public KingdomAtlasZoneCell(long pCityId, int pX, int pY, bool pWater, byte pNeighborMask)
        {
            CityId = pCityId; X = pX; Y = pY; Water = pWater; NeighborMask = pNeighborMask;
        }
        public long CityId { get; }
        public int X { get; }
        public int Y { get; }
        public bool Water { get; }
        public byte NeighborMask { get; }
        public bool Equals(KingdomAtlasZoneCell pOther) => CityId == pOther.CityId && X == pOther.X && Y == pOther.Y && Water == pOther.Water && NeighborMask == pOther.NeighborMask;
        public override bool Equals(object pObject) => pObject is KingdomAtlasZoneCell other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = CityId.GetHashCode();
                hash = hash * 397 ^ X;
                hash = hash * 397 ^ Y;
                hash = hash * 397 ^ (Water ? 1 : 0);
                return hash * 397 ^ NeighborMask;
            }
        }
    }

    internal sealed class KingdomAtlasHistoryEvent
    {
        public long EventId { get; set; }
        public double WorldTime { get; set; }
        public int Year { get; set; }
        public string YearText { get; set; } = "";
        public long CityId { get; set; }
        public string CityName { get; set; } = "";
        public string EventType { get; set; } = "";
        public long OldKingdomId { get; set; } = -1L;
        public string OldKingdomName { get; set; } = "";
        public string OldKingdomColor { get; set; } = "";
        public long NewKingdomId { get; set; } = -1L;
        public string NewKingdomName { get; set; } = "";
        public string NewKingdomColor { get; set; } = "";
    }

    internal sealed class KingdomAtlasZoneSnapshot
    {
        public long SnapshotId { get; set; }
        public long CityId { get; set; }
        public double WorldTime { get; set; }
        public string EventType { get; set; } = "";
        public long KingdomId { get; set; } = -1L;
        public string KingdomName { get; set; } = "";
        public string KingdomColor { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public bool Water { get; set; }
        public byte NeighborMask { get; set; }
    }

    internal sealed class KingdomAtlasChronicleRow
    {
        public long EventId { get; set; }
        public double WorldTime { get; set; }
        public int Year { get; set; }
        public string YearText { get; set; } = "";
        public string Content { get; set; } = "";
        public string ContentRich { get; set; } = "";
        public string EventType { get; set; } = "";
    }

    internal sealed class KingdomAtlasKingdomSnapshot
    {
        public long KingdomId { get; set; } = -1L;
        public string Name { get; set; } = "";
        public string Color { get; set; } = "";
    }

    internal sealed class KingdomAtlasVassalRelationSnapshot
    {
        public long RelationId { get; set; } = -1L;
        public long VassalId { get; set; } = -1L;
        public string VassalName { get; set; } = "";
        public string VassalColor { get; set; } = "";
        public long SuzerainId { get; set; } = -1L;
        public string SuzerainName { get; set; } = "";
        public string SuzerainColor { get; set; } = "";
        public int ContractTier { get; set; }
        public double StartTime { get; set; }
        public double EndTime { get; set; } = -1d;
    }

    internal sealed class KingdomAtlasNodeDescriptor
    {
        public KingdomAtlasNodeKind NodeKind { get; set; }
        public long SourceId { get; set; } = -1L;
        public string StableKey { get; set; } = "";
        public double WorldTime { get; set; }
        public long CityReplayEventId { get; set; } = long.MaxValue;
        public KingdomAtlasHistoryEvent CityEvent { get; set; }
        public KingdomAtlasVassalRelationSnapshot Relation { get; set; }
    }

    internal sealed class KingdomAtlasNode
    {
        public long KingdomId { get; set; } = -1L;
        public KingdomAtlasNodeKind NodeKind { get; set; }
        public long SourceId { get; set; } = -1L;
        public string StableKey { get; set; } = "";
        public KingdomAtlasVassalRelationSnapshot Relation { get; set; }
        public KingdomAtlasHistoryEvent Event { get; set; }
        public IReadOnlyList<KingdomAtlasHistoryEvent> Events { get; set; } = Array.Empty<KingdomAtlasHistoryEvent>();
        public IReadOnlyDictionary<long, long> CityOwners { get; set; } = new Dictionary<long, long>();
        public IReadOnlyList<KingdomAtlasZoneCell> VisibleZones { get; set; } = Array.Empty<KingdomAtlasZoneCell>();
        public int TerrainWorldWidth { get; set; } = 1;
        public int TerrainWorldHeight { get; set; } = 1;
        public IReadOnlyList<KingdomAtlasChronicleRow> OldChronicle { get; set; } = Array.Empty<KingdomAtlasChronicleRow>();
        public IReadOnlyList<KingdomAtlasChronicleRow> NewChronicle { get; set; } = Array.Empty<KingdomAtlasChronicleRow>();
        public string OldChronicleYearText { get; set; } = "";
        public string NewChronicleYearText { get; set; } = "";
        public IReadOnlyList<KingdomAtlasVassalRelationSnapshot> VassalRelations { get; set; } = Array.Empty<KingdomAtlasVassalRelationSnapshot>();
        public IReadOnlyDictionary<long, KingdomAtlasKingdomSnapshot> Kingdoms { get; set; } = new Dictionary<long, KingdomAtlasKingdomSnapshot>();
        public IReadOnlyDictionary<long, KingdomAtlasColor> DisplayColors { get; set; } = new Dictionary<long, KingdomAtlasColor>();
    }

    internal sealed class KingdomAtlasRaster
    {
        public KingdomAtlasRaster(int pWidth, int pHeight, byte[] pRgba)
        {
            if (pWidth < 1 || pHeight < 1) throw new ArgumentOutOfRangeException(nameof(pWidth));
            if (pRgba == null || pRgba.Length != pWidth * pHeight * 4) throw new ArgumentException("RGBA buffer dimensions mismatch.", nameof(pRgba));
            Width = pWidth; Height = pHeight; Rgba = pRgba;
        }
        public int Width { get; }
        public int Height { get; }
        public byte[] Rgba { get; }
    }

    internal sealed class KingdomAtlasRenderFrame
    {
        public KingdomAtlasRenderFrame(KingdomAtlasRaster pRaster,
            long pEventId, int pYear, string pTitle)
        {
            Raster = pRaster ?? throw new ArgumentNullException(nameof(pRaster));
            EventId = pEventId;
            Year = pYear;
            Title = pTitle ?? "";
        }

        public KingdomAtlasRaster Raster { get; }
        public long EventId { get; }
        public int Year { get; }
        public string Title { get; }
    }

    internal sealed class KingdomAtlasLabel
    {
        public long KingdomId { get; set; }
        public string Text { get; set; } = "";
        public KingdomAtlasColor Color { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public float Angle { get; set; }
        public float Size { get; set; }
    }

    internal readonly struct KingdomAtlasGenerationKey : IEquatable<KingdomAtlasGenerationKey>
    {
        public KingdomAtlasGenerationKey(long pEventId, int pResolution, string pGeometryVersion)
        {
            EventId = pEventId; Resolution = pResolution; GeometryVersion = pGeometryVersion ?? "";
        }
        public long EventId { get; }
        public int Resolution { get; }
        public string GeometryVersion { get; }
        public bool Equals(KingdomAtlasGenerationKey pOther) => EventId == pOther.EventId && Resolution == pOther.Resolution && string.Equals(GeometryVersion, pOther.GeometryVersion, StringComparison.Ordinal);
        public override bool Equals(object pObject) => pObject is KingdomAtlasGenerationKey other && Equals(other);
        public override int GetHashCode() => (EventId.GetHashCode() * 397 ^ Resolution) * 397 ^ GeometryVersion.GetHashCode();
    }

    internal readonly struct KingdomAtlasProgress
    {
        public KingdomAtlasProgress(int pCompleted, int pTotal, string pStage, long pEventId)
        {
            Completed = Math.Max(0, pCompleted); Total = Math.Max(0, pTotal); Stage = pStage ?? ""; EventId = pEventId;
        }
        public int Completed { get; }
        public int Total { get; }
        public string Stage { get; }
        public long EventId { get; }
        public int Percent => KingdomAtlasRules.Percent(Completed, Total);
    }
}
