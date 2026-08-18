using System.Collections.Generic;

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
        public CourtPyramidNodeModel LeaderNode { get; set; }
        public int ActiveSeats { get; set; }
        public int TotalSeats { get; set; }
        public float Efficiency { get; set; }
        public string LocalSchoolId { get; set; } = string.Empty;
        public List<CourtPyramidNodeModel> Nodes { get; set; } =
            new List<CourtPyramidNodeModel>();
        public List<CustomCourtEdge> Edges { get; set; } =
            new List<CustomCourtEdge>();
    }
}
