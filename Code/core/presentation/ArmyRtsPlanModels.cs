using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.presentation
{
    public readonly struct ArmyRtsPlanColor : IEquatable<ArmyRtsPlanColor>
    {
        public ArmyRtsPlanColor(byte pRed, byte pGreen, byte pBlue,
            byte pAlpha = 255)
        {
            Red = pRed;
            Green = pGreen;
            Blue = pBlue;
            Alpha = pAlpha;
        }

        public byte Red { get; }
        public byte Green { get; }
        public byte Blue { get; }
        public byte Alpha { get; }

        public bool Equals(ArmyRtsPlanColor pOther)
        {
            return Red == pOther.Red && Green == pOther.Green &&
                   Blue == pOther.Blue && Alpha == pOther.Alpha;
        }

        public override bool Equals(object pObject)
        {
            return pObject is ArmyRtsPlanColor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Red;
                hash = hash * 397 ^ Green;
                hash = hash * 397 ^ Blue;
                return hash * 397 ^ Alpha;
            }
        }

        public static bool operator ==(ArmyRtsPlanColor pLeft,
            ArmyRtsPlanColor pRight)
        {
            return pLeft.Equals(pRight);
        }

        public static bool operator !=(ArmyRtsPlanColor pLeft,
            ArmyRtsPlanColor pRight)
        {
            return !pLeft.Equals(pRight);
        }
    }

    public readonly struct ArmyRtsPlanPoint : IEquatable<ArmyRtsPlanPoint>
    {
        public ArmyRtsPlanPoint(int pX, int pY)
        {
            X = pX;
            Y = pY;
        }

        public int X { get; }
        public int Y { get; }

        public bool Equals(ArmyRtsPlanPoint pOther)
        {
            return X == pOther.X && Y == pOther.Y;
        }

        public override bool Equals(object pObject)
        {
            return pObject is ArmyRtsPlanPoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked { return X * 397 ^ Y; }
        }

        public static bool operator ==(ArmyRtsPlanPoint pLeft,
            ArmyRtsPlanPoint pRight)
        {
            return pLeft.Equals(pRight);
        }

        public static bool operator !=(ArmyRtsPlanPoint pLeft,
            ArmyRtsPlanPoint pRight)
        {
            return !pLeft.Equals(pRight);
        }
    }

    public enum ArmyRtsPlanOperation
    {
        Rally = 0,
        Attack = 1,
        Defense = 2,
        Hold = 3,
        Retreat = 4,
        Replenish = 5
    }

    public enum ArmyRtsPlanArrowStyle
    {
        March = 0,
        Attack = 1,
        Recovery = 2,
        Redeploy = 3,
        Transport = 4
    }

    public sealed class ArmyRtsPlanKingdom
    {
        public ArmyRtsPlanKingdom(long pKingdomId, string pName,
            ArmyRtsPlanColor pColor, bool pAttacker)
        {
            KingdomId = pKingdomId;
            Name = pName ?? string.Empty;
            Color = pColor;
            Attacker = pAttacker;
        }

        public long KingdomId { get; }
        public string Name { get; }
        public ArmyRtsPlanColor Color { get; }
        public bool Attacker { get; }
    }

    public sealed class ArmyRtsPlanZone
    {
        public ArmyRtsPlanZone(int pX, int pY, int pWidth, int pHeight,
            long pCityId, long pKingdomId, ArmyRtsPlanColor pColor,
            bool pWater, bool pParticipant)
        {
            X = pX;
            Y = pY;
            Width = Math.Max(1, pWidth);
            Height = Math.Max(1, pHeight);
            CityId = pCityId;
            KingdomId = pKingdomId;
            Color = pColor;
            Water = pWater;
            Participant = pParticipant;
        }

        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public long CityId { get; }
        public long KingdomId { get; }
        public ArmyRtsPlanColor Color { get; }
        public bool Water { get; }
        public bool Participant { get; }
    }

    public sealed class ArmyRtsPlanCity
    {
        public ArmyRtsPlanCity(long pCityId, long pOwnerKingdomId,
            long pControllerKingdomId, ArmyRtsPlanPoint pPosition,
            bool pFriendlyOccupied)
        {
            CityId = pCityId;
            OwnerKingdomId = pOwnerKingdomId;
            ControllerKingdomId = pControllerKingdomId;
            Position = pPosition;
            FriendlyOccupied = pFriendlyOccupied;
        }

        public long CityId { get; }
        public long OwnerKingdomId { get; }
        public long ControllerKingdomId { get; }
        public ArmyRtsPlanPoint Position { get; }
        public bool FriendlyOccupied { get; }
    }

    public sealed class ArmyRtsPlanArmy
    {
        public ArmyRtsPlanArmy(long pArmyId, long pKingdomId,
            ArmyRtsPlanPoint pCaptain, long pTargetCityId,
            ArmyRtsPlanPoint pTarget, ArmyRtsPlanPoint? pRouteAnchor,
            long pFrontId, ArmyRtsPlanOperation pOperation,
            bool pFriendlyRecovery, bool pTransportActive,
            bool pPlayerOrder, bool pStalled,
            ArmyRtsProposalKind pProposalKind =
                ArmyRtsProposalKind.Attack,
            ArmyRtsRole pRole = ArmyRtsRole.Assault,
            ArmyRtsPosture pPosture = ArmyRtsPosture.Automatic,
            IReadOnlyList<ArmyRtsPlanPoint> pActualPath = null)
        {
            ArmyId = pArmyId;
            KingdomId = pKingdomId;
            Captain = pCaptain;
            TargetCityId = pTargetCityId;
            Target = pTarget;
            RouteAnchor = pRouteAnchor;
            FrontId = pFrontId;
            Operation = pOperation;
            FriendlyRecovery = pFriendlyRecovery;
            TransportActive = pTransportActive;
            PlayerOrder = pPlayerOrder;
            Stalled = pStalled;
            ProposalKind = pProposalKind;
            Role = pRole;
            Posture = pPosture;
            ActualPath = pActualPath == null
                ? Array.Empty<ArmyRtsPlanPoint>()
                : new List<ArmyRtsPlanPoint>(pActualPath).ToArray();
        }

        public long ArmyId { get; }
        public long KingdomId { get; }
        public ArmyRtsPlanPoint Captain { get; }
        public long TargetCityId { get; }
        public ArmyRtsPlanPoint Target { get; }
        public ArmyRtsPlanPoint? RouteAnchor { get; }
        public long FrontId { get; }
        public ArmyRtsPlanOperation Operation { get; }
        public bool FriendlyRecovery { get; }
        public bool TransportActive { get; }
        public bool PlayerOrder { get; }
        public bool Stalled { get; }
        public ArmyRtsProposalKind ProposalKind { get; }
        public ArmyRtsRole Role { get; }
        public ArmyRtsPosture Posture { get; }
        public IReadOnlyList<ArmyRtsPlanPoint> ActualPath { get; }
    }

    public sealed class ArmyRtsPlanFront
    {
        public ArmyRtsPlanFront(long pFrontId, long pKingdomId,
            ArmyRtsPlanPoint pStart, ArmyRtsPlanPoint pEnd)
        {
            FrontId = pFrontId;
            KingdomId = pKingdomId;
            Start = pStart;
            End = pEnd;
        }

        public long FrontId { get; }
        public long KingdomId { get; }
        public ArmyRtsPlanPoint Start { get; }
        public ArmyRtsPlanPoint End { get; }
    }

    public sealed class ArmyRtsPlanSnapshot
    {
        public ArmyRtsPlanSnapshot(long pWarId, int pWorldYear,
            int pWorldWidth, int pWorldHeight, string pReason,
            IEnumerable<ArmyRtsPlanKingdom> pKingdoms,
            IEnumerable<ArmyRtsPlanZone> pZones,
            IEnumerable<ArmyRtsPlanCity> pCities,
            IEnumerable<ArmyRtsPlanArmy> pArmies,
            IEnumerable<ArmyRtsPlanFront> pFronts,
            ArmyRtsPlanTerrain pTerrain = null)
        {
            WarId = pWarId;
            WorldYear = pWorldYear;
            WorldWidth = Math.Max(1, pWorldWidth);
            WorldHeight = Math.Max(1, pWorldHeight);
            Reason = pReason ?? string.Empty;
            Kingdoms = Copy(pKingdoms);
            Zones = Copy(pZones);
            Cities = Copy(pCities);
            Armies = Copy(pArmies);
            Fronts = Copy(pFronts);
            Terrain = pTerrain;
        }

        public long WarId { get; }
        public int WorldYear { get; }
        public int WorldWidth { get; }
        public int WorldHeight { get; }
        public string Reason { get; }
        public IReadOnlyList<ArmyRtsPlanKingdom> Kingdoms { get; }
        public IReadOnlyList<ArmyRtsPlanZone> Zones { get; }
        public IReadOnlyList<ArmyRtsPlanCity> Cities { get; }
        public IReadOnlyList<ArmyRtsPlanArmy> Armies { get; }
        public IReadOnlyList<ArmyRtsPlanFront> Fronts { get; }
        public ArmyRtsPlanTerrain Terrain { get; }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> pItems)
        {
            if (pItems == null) return Array.Empty<T>();
            return new List<T>(pItems).ToArray();
        }
    }

    public sealed class ArmyRtsPlanArtifact
    {
        public ArmyRtsPlanArtifact(ArmyRtsPlanSnapshot pSnapshot,
            int pRevision, long pWorldGeneration = 0L,
            ulong pFingerprint = 0UL)
        {
            Snapshot = pSnapshot ?? throw new ArgumentNullException(
                nameof(pSnapshot));
            Revision = Math.Max(0, pRevision);
            WorldGeneration = Math.Max(0L, pWorldGeneration);
            Fingerprint = pFingerprint;
        }

        public ArmyRtsPlanSnapshot Snapshot { get; }
        public int Revision { get; }
        public long WorldGeneration { get; }
        public ulong Fingerprint { get; }
    }

    public readonly struct ArmyRtsPlanFrameSummary
    {
        public ArmyRtsPlanFrameSummary(int pWorldWidth, int pWorldHeight,
            int pKingdomCount, int pCityCount, int pArmyCount,
            int pFrontCount, int pProposalKindMask, int pRoleMask,
            int pPostureMask)
        {
            WorldWidth = Math.Max(1, pWorldWidth);
            WorldHeight = Math.Max(1, pWorldHeight);
            KingdomCount = Math.Max(0, pKingdomCount);
            CityCount = Math.Max(0, pCityCount);
            ArmyCount = Math.Max(0, pArmyCount);
            FrontCount = Math.Max(0, pFrontCount);
            ProposalKindMask = pProposalKindMask;
            RoleMask = pRoleMask;
            PostureMask = pPostureMask;
        }

        public int WorldWidth { get; }
        public int WorldHeight { get; }
        public int KingdomCount { get; }
        public int CityCount { get; }
        public int ArmyCount { get; }
        public int FrontCount { get; }
        public int ProposalKindMask { get; }
        public int RoleMask { get; }
        public int PostureMask { get; }
    }

    public sealed class ArmyRtsPlanTerrain
    {
        public ArmyRtsPlanTerrain(int pWidth, int pHeight, byte[] pPixels)
        {
            Width = Math.Max(1, pWidth);
            Height = Math.Max(1, pHeight);
            if (pPixels == null || pPixels.Length != Width * Height)
                throw new ArgumentException(
                    "Terrain pixels must match its dimensions.",
                    nameof(pPixels));
            Pixels = pPixels;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }
    }

    public sealed class ArmyRtsPlanIndexedRaster
    {
        public ArmyRtsPlanIndexedRaster(int pWidth, int pHeight,
            byte[] pPixels)
        {
            Width = Math.Max(1, pWidth);
            Height = Math.Max(1, pHeight);
            if (pPixels == null || pPixels.Length != Width * Height)
                throw new ArgumentException(
                    "Indexed pixels must match raster dimensions.",
                    nameof(pPixels));
            Pixels = pPixels;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }
    }

    public sealed class ArmyRtsPlanGifFrame
    {
        public ArmyRtsPlanGifFrame(ulong pFingerprint, int pRevision,
            int pWorldYear, string pReason,
            ArmyRtsPlanFrameSummary pSummary,
            ArmyRtsPlanIndexedRaster pRaster)
        {
            Fingerprint = pFingerprint;
            Revision = Math.Max(0, pRevision);
            WorldYear = Math.Max(0, pWorldYear);
            Reason = pReason ?? string.Empty;
            Summary = pSummary;
            Raster = pRaster ?? throw new ArgumentNullException(
                nameof(pRaster));
        }

        public ulong Fingerprint { get; }
        public int Revision { get; }
        public int WorldYear { get; }
        public string Reason { get; }
        public ArmyRtsPlanFrameSummary Summary { get; }
        public ArmyRtsPlanIndexedRaster Raster { get; }
    }
}
