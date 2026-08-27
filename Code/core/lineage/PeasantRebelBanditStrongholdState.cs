using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal enum BanditStrongholdPhase
    {
        None,
        Creating,
        Active,
        Released,
        Falling,
        Completed
    }

    internal enum BanditRaidStage
    {
        None,
        Outbound,
        Looted,
        Returning,
        Cooldown
    }

    internal sealed class BanditStrongholdPoint
    {
        public int X = 0;
        public int Y = 0;
        public string OriginalTopTypeId = "";

        public string Key => X + ":" + Y;
    }

    internal sealed class BanditStrongholdTower
    {
        public long TowerBuildingId = -1L;
        public int X = 0;
        public int Y = 0;
        public string AssetId = "";
    }

    internal sealed class BanditRaidMissionState
    {
        public BanditRaidStage Stage = BanditRaidStage.None;
        public List<long> MemberActorIds = new List<long>();
        public long LeaderActorId = -1L;
        public long TargetCityId = -1L;
        public int TargetX = 0;
        public int TargetY = 0;
        public int CarriedFood = 0;
        public Dictionary<string, int> CarriedFoodByResourceId =
            new Dictionary<string, int>();
        public Dictionary<long, Dictionary<string, int>>
            CarriedFoodByActorId =
                new Dictionary<long, Dictionary<string, int>>();
        public int CooldownUntilYear = 0;
        public int LastRouteDistance = 0;
    }

    internal sealed class PeasantRebelBanditStrongholdState
    {
        public const int CurrentSchemaVersion = 7;

        public int SchemaVersion = CurrentSchemaVersion;
        public BanditStrongholdPhase Phase = BanditStrongholdPhase.None;
        public BanditStrongholdKind StrongholdKind =
            BanditStrongholdKind.Land;
        public BanditIslandMigrationState Migration =
            new BanditIslandMigrationState();
        public long StrongholdCityId = -1L;
        public long LeaderActorId = -1L;
        public long MotherCityId = -1L;
        public long OriginKingdomId = -1L;
        public List<string> FixedZoneKeys = new List<string>();
        public List<BanditStrongholdPoint> WallPoints =
            new List<BanditStrongholdPoint>();
        public List<BanditStrongholdTower> Towers =
            new List<BanditStrongholdTower>();
        public long LastHostileKillerKingdomId = -1L;
        public long SuppressorKingdomId = -1L;
        public long PressureTargetCityId = -1L;
        public int Pressure = 0;
        public int LastPressureYear = int.MinValue;
        public BanditRaidMissionState Raid = new BanditRaidMissionState();
        public Dictionary<long, int> SuppressionExpiryByKingdomId =
            new Dictionary<long, int>();
        public List<long> InheritedStrongholdCityIds = new List<long>();
        public string RouteSubtype = "";
        public long GuiyiOccupierKingdomId = -1L;
        public long GuiyiOriginalKingdomId = -1L;
        public long GuiyiOriginalCityId = -1L;
        public long GuiyiRestorationClaimId = -1L;
        public int GuiyiCreatedYear = -1;
        public string GuiyiStage = "";
    }

    internal sealed class BanditIslandMigrationState
    {
        public BanditMigrationStage Stage = BanditMigrationStage.None;
        public long OldStrongholdCityId = -1L;
        public long TargetIslandId = -1L;
        public int TargetLandingTileId = -1;
        public int StartedYear = -1;
        public int ThreatCycles = 0;
        public long LeaderActorId = -1L;
        public List<long> MemberActorIds = new List<long>();
        public long TransportRequestId = -1L;
        public long TransportBoatId = -1L;
        public int FailureCount = 0;
    }
}
