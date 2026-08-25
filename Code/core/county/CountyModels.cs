using System.Collections.Generic;

namespace AncientWarfare3.core.county
{
    internal sealed class CountyAdministrationSnapshot
    {
        public int SchemaVersion { get; set; } = 1;
        public long NextCountyId { get; set; } = 1L;
        public long Revision { get; set; }
        public List<CountyRecord> Counties { get; set; } = new List<CountyRecord>();
    }

    internal sealed class CountyRecord
    {
        public long CountyId { get; set; } = -1L;
        public long CityId { get; set; } = -1L;
        public long RegionId { get; set; } = -1L;
        public int Ordinal { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool ManualName { get; set; }
        public List<long> ZoneIds { get; set; } = new List<long>();
        public long LeaderActorId { get; set; } = -1L;
        public bool Active { get; set; } = true;
        public int CreatedYear { get; set; } = -1;
        public int LastRepairedYear { get; set; } = -1;
        public long Revision { get; set; }
    }
}
