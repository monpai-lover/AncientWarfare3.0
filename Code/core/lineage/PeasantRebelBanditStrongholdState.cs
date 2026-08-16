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
        public int CooldownUntilYear = 0;
        public int LastRouteDistance = 0;
    }

    internal sealed class PeasantRebelBanditStrongholdState
    {
        public const int CurrentSchemaVersion = 3;

        public int SchemaVersion = CurrentSchemaVersion;
        public BanditStrongholdPhase Phase = BanditStrongholdPhase.None;
        public long StrongholdCityId = -1L;
        public long MotherCityId = -1L;
        public long OriginKingdomId = -1L;
        public List<string> FixedZoneKeys = new List<string>();
        public List<BanditStrongholdPoint> WallPoints =
            new List<BanditStrongholdPoint>();
        public BanditRaidMissionState Raid = new BanditRaidMissionState();
        public Dictionary<long, int> SuppressionExpiryByKingdomId =
            new Dictionary<long, int>();
    }
}
