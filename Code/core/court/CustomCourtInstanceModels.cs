using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public sealed class CustomCourtOfficeOverride
    {
        public string OfficeId { get; set; } = string.Empty;
        public bool HasName { get; set; }
        public CustomCourtLocalizedText Name { get; set; } =
            new CustomCourtLocalizedText();
        public bool HasGrade { get; set; }
        public int Grade { get; set; }
        public bool HasSlots { get; set; }
        public int Slots { get; set; }
    }

    public sealed class CustomCourtLegacyOffice
    {
        public string OfficeId { get; set; } = string.Empty;
        public string FormerName { get; set; } = string.Empty;
        public int RetiredRevision { get; set; }
    }

    public sealed class CustomCourtInstance
    {
        public int SchemaVersion { get; set; } = 1;
        public string KingdomId { get; set; } = string.Empty;
        public string TemplateId { get; set; } = string.Empty;
        public int TemplateRevision { get; set; }
        public string TemplateHash { get; set; } = string.Empty;
        public CustomCourtTemplate ResolvedSnapshot { get; set; }
        public List<CustomCourtOfficeOverride> Overrides { get; set; } =
            new List<CustomCourtOfficeOverride>();
        public List<CustomCourtLegacyOffice> LegacyOffices { get; set; } =
            new List<CustomCourtLegacyOffice>();
    }
}
