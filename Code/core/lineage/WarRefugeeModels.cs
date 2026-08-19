using System;

namespace AncientWarfare3.core.lineage
{
    public enum WarRefugeeJourneyState
    {
        Planned = 0,
        Traveling = 1,
        Arrived = 2,
        Returning = 3,
        Settled = 4,
        Cancelled = 5
    }

    public sealed class WarRefugeeJourneySnapshot
    {
        public long JourneyId { get; set; } = -1L;
        public long OriginKingdomId { get; set; } = -1L;
        public long OriginCityId { get; set; } = -1L;
        public long DestinationKingdomId { get; set; } = -1L;
        public long DestinationCityId { get; set; } = -1L;
        public WarRefugeeJourneyState State { get; set; }
        public int DepartureYear { get; set; }
        public int ArrivalYear { get; set; } = -1;
        public int ReservedCapacity { get; set; }
        public int SafeMonths { get; set; }
        public int LastAssimilationYear { get; set; } = -1;
        public int RouteRetries { get; set; }
        public int LastDistance { get; set; } = int.MaxValue;
        public int ConsecutiveDangerMonths { get; set; }
        public int NextRetryMonth { get; set; } = -1;
    }

    public sealed class WarRefugeeMemberSnapshot
    {
        public long JourneyId { get; set; } = -1L;
        public long ActorId { get; set; } = -1L;
        public bool IsLeader { get; set; }
        public bool Active { get; set; } = true;
        public string OriginCulture { get; set; } = "";
    }

    public sealed class WarRefugeeOriginSnapshot
    {
        public long ActorId { get; set; } = -1L;
        public long JourneyId { get; set; } = -1L;
        public long OriginKingdomId { get; set; } = -1L;
        public long OriginCityId { get; set; } = -1L;
        public string OriginCulture { get; set; } = "";
        public int SettledYear { get; set; }
    }

    public enum WarRefugeeRelation
    {
        Enemy = 0,
        Neutral = 1,
        ProtectedPartner = 2,
        Domestic = 3
    }

    public readonly struct WarRefugeeThreatFacts
    {
        public WarRefugeeThreatFacts(bool pNearbyArmy, bool pSiege,
            bool pCombatOrTransfer, bool pFamine, bool pActiveWar)
        {
            NearbyArmy = pNearbyArmy;
            Siege = pSiege;
            CombatOrTransfer = pCombatOrTransfer;
            Famine = pFamine;
            ActiveWar = pActiveWar;
        }

        public bool NearbyArmy { get; }
        public bool Siege { get; }
        public bool CombatOrTransfer { get; }
        public bool Famine { get; }
        public bool ActiveWar { get; }
    }

    public readonly struct WarRefugeeActorFacts
    {
        public WarRefugeeActorFacts(bool pAlive, bool pKing, bool pHeir,
            bool pCentralOfficial, bool pLocalOfficial, bool pGeneral,
            bool pWarrior, bool pRoyalGuard, bool pJourneyActive)
        {
            Alive = pAlive;
            King = pKing;
            Heir = pHeir;
            CentralOfficial = pCentralOfficial;
            LocalOfficial = pLocalOfficial;
            General = pGeneral;
            Warrior = pWarrior;
            RoyalGuard = pRoyalGuard;
            JourneyActive = pJourneyActive;
        }

        public bool Alive { get; }
        public bool King { get; }
        public bool Heir { get; }
        public bool CentralOfficial { get; }
        public bool LocalOfficial { get; }
        public bool General { get; }
        public bool Warrior { get; }
        public bool RoyalGuard { get; }
        public bool JourneyActive { get; }
    }

    public readonly struct WarRefugeeActiveMember
    {
        public WarRefugeeActiveMember(long pJourneyId, long pActorId,
            bool pLeader, bool pActive, string pCulture)
        {
            JourneyId = pJourneyId;
            ActorId = pActorId;
            IsLeader = pLeader;
            Active = pActive;
            OriginCulture = pCulture ?? "";
        }

        public long JourneyId { get; }
        public long ActorId { get; }
        public bool IsLeader { get; }
        public bool Active { get; }
        public string OriginCulture { get; }
    }

    public readonly struct WarRefugeeDestinationFacts
    {
        public WarRefugeeDestinationFacts(long pId, bool pAlive,
            bool pWarGoal, bool pHostileArmy, bool pCombat,
            int pFood, int pHousing, int pCapacity,
            WarRefugeeRelation pRelation, int pDistance, bool pFamine = false)
        {
            Id = pId;
            Alive = pAlive;
            WarGoal = pWarGoal;
            HostileArmy = pHostileArmy;
            Combat = pCombat;
            Food = pFood;
            Housing = pHousing;
            Capacity = pCapacity;
            Relation = pRelation;
            Distance = pDistance;
            Famine = pFamine;
        }

        public long Id { get; }
        public bool Alive { get; }
        public bool WarGoal { get; }
        public bool HostileArmy { get; }
        public bool Combat { get; }
        public int Food { get; }
        public int Housing { get; }
        public int Capacity { get; }
        public WarRefugeeRelation Relation { get; }
        public int Distance { get; }
        public bool Famine { get; }
    }

    public readonly struct WarRefugeeLeaderCandidate
    {
        public WarRefugeeLeaderCandidate(long pId, bool pAdult, bool pAlive)
        {
            Id = pId;
            Adult = pAdult;
            Alive = pAlive;
        }

        public long Id { get; }
        public bool Adult { get; }
        public bool Alive { get; }
    }

    public readonly struct WarRefugeeReturnFacts
    {
        public WarRefugeeReturnFacts(bool originSafe, int originProsperity,
            bool hostSafe, int hostProsperity, int relativesAtOrigin,
            int residenceYears, bool localMarriage, bool hostBornChildren,
            bool establishedLivelihood)
        {
            OriginSafe = originSafe;
            OriginProsperity = Math.Max(0, originProsperity);
            HostSafe = hostSafe;
            HostProsperity = Math.Max(0, hostProsperity);
            RelativesAtOrigin = Math.Max(0, relativesAtOrigin);
            ResidenceYears = Math.Max(0, residenceYears);
            LocalMarriage = localMarriage;
            HostBornChildren = hostBornChildren;
            EstablishedLivelihood = establishedLivelihood;
        }
        public bool OriginSafe { get; }
        public int OriginProsperity { get; }
        public bool HostSafe { get; }
        public int HostProsperity { get; }
        public int RelativesAtOrigin { get; }
        public int ResidenceYears { get; }
        public bool LocalMarriage { get; }
        public bool HostBornChildren { get; }
        public bool EstablishedLivelihood { get; }
    }
}
