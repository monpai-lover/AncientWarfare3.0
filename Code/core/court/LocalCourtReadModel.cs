using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal sealed class LocalCourtReadModel
    {
        public long KingdomId { get; set; } = -1L;
        public long CityId { get; set; } = -1L;
        public string CityName { get; set; } = string.Empty;
        public string TemplateId { get; set; } = string.Empty;
        public string TemplateName { get; set; } = string.Empty;
        public string CityTypeName { get; set; } = string.Empty;
        public long RegionSeatCityId { get; set; } = -1L;
        public string RegionName { get; set; } = string.Empty;
        public string RegionTitle { get; set; } = string.Empty;
        public string RegionalGovernorTitle { get; set; } = string.Empty;
        public string LocalLevelTitle { get; set; } = string.Empty;
        public int RegionMemberCount { get; set; }
        public long RegionalGovernorActorId { get; set; } = -1L;
        public CourtPyramidNodeModel RegionalSuperiorNode { get; set; }
        public CourtPyramidNodeModel LeaderNode { get; set; }
        public int ActiveSeats { get; set; }
        public int TotalSeats { get; set; }
        public float Efficiency { get; set; }
        public string LocalSchoolId { get; set; } = string.Empty;
        public CorruptionCountrySnapshot CountryCorruption { get; set; }
        public CorruptionCitySnapshot CityCorruption { get; set; }
        public List<CourtPyramidNodeModel> Nodes { get; set; } =
            new List<CourtPyramidNodeModel>();
        public List<CustomCourtEdge> Edges { get; set; } =
            new List<CustomCourtEdge>();
    }
}
