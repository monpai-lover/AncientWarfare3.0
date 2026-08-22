using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    internal sealed class RegionalGovernmentCityFact
    {
        public long KingdomId { get; set; } = -1L;
        public long CityId { get; set; } = -1L;
        public string CityName { get; set; } = string.Empty;
        public float Development { get; set; }
        public int Population { get; set; }
        public IReadOnlyList<long> NeighborCityIds { get; set; } =
            Array.Empty<long>();
    }

    internal sealed class RegionalGovernmentFact
    {
        public long KingdomId { get; set; } = -1L;
        public long SeatCityId { get; set; } = -1L;
        public string SeatCityName { get; set; } = string.Empty;
        public List<long> MemberCityIds { get; set; } = new List<long>();
    }

    internal sealed class RegionalGovernmentReadModel
    {
        public long KingdomId { get; set; } = -1L;
        // SeatCityId remains the persisted/legal seat used by maps and history.
        public long SeatCityId { get; set; } = -1L;
        public long LegalSeatCityId { get; set; } = -1L;
        public long EffectiveSeatCityId { get; set; } = -1L;
        public string RegionName { get; set; } = string.Empty;
        public string RegionTitle { get; set; } = string.Empty;
        public string GovernorTitle { get; set; } = string.Empty;
        public string LocalLevelTitle { get; set; } = string.Empty;
        public long GovernorActorId { get; set; } = -1L;
        public long RegionId { get; set; } = -1L;
        public int TotalMemberCount { get; set; }
        public int ControlledMemberCount { get; set; }
        public int ForeignMemberCount { get; set; }
        public bool IsSeatControlled { get; set; }
        public bool HasForeignDeJureMembers { get; set; }
        public Dictionary<long, int> ControllerMemberCounts { get; set; } =
            new Dictionary<long, int>();
        public int MemberCount => MemberCityIds?.Count ?? 0;
        public List<long> MemberCityIds { get; set; } = new List<long>();
        public List<long> LocalGovernmentCityIds { get; set; } =
            new List<long>();
    }
}
