using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    internal sealed class DeJureAdministrationStore
    {
        public int SchemaVersion { get; set; } = 1;
        public long NextRegionId { get; set; } = 1L;
        public long StoreRevision { get; set; }
        public List<DeJureRegion> Regions { get; set; } =
            new List<DeJureRegion>();
        public List<DeJureRegionChange> ChangeHistory { get; set; } =
            new List<DeJureRegionChange>();
        public List<string> OrphanedRecords { get; set; } =
            new List<string>();
    }

    internal sealed class DeJureRegion
    {
        public long RegionId { get; set; } = -1L;
        public string RegionName { get; set; } = string.Empty;
        public long SeatCityId { get; set; } = -1L;
        public List<long> MemberCityIds { get; set; } = new List<long>();
        public int CreatedYear { get; set; } = -1;
        public string CreatedByKind { get; set; } = string.Empty;
        public long CreatedByKingdomId { get; set; } = -1L;
        public int Version { get; set; } = 1;
        public bool Active { get; set; } = true;
    }

    internal sealed class DeJureRegionChange
    {
        public long ChangeId { get; set; } = -1L;
        public long RegionId { get; set; } = -1L;
        public long CityId { get; set; } = -1L;
        public long FromRegionId { get; set; } = -1L;
        public long ToRegionId { get; set; } = -1L;
        public string Reason { get; set; } = string.Empty;
        public int Year { get; set; } = -1;
        public long ActorId { get; set; } = -1L;
        public int Version { get; set; } = 1;
    }

    internal sealed class DeJureRegionMergeCandidate
    {
        public long PrimaryRegionId { get; set; }
        public long SecondaryRegionId { get; set; }
        public long PrimaryCityId { get; set; }
        public long SecondaryCityId { get; set; }
        public string PrimaryName { get; set; } = string.Empty;
        public string SecondaryName { get; set; } = string.Empty;
    }
}
