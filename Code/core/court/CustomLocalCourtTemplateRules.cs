using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public static class CustomLocalCourtTemplateRules
    {
        public const int CurrentSchemaVersion = 3;
        public const int MaximumTemplates = 16;
        public const string LegacyDefaultTemplateId = "local_default";

        public static CustomCourtTemplate UpgradeLegacy(
            CustomCourtTemplate pTemplate)
        {
            if (pTemplate == null) return null;
            pTemplate.Offices = pTemplate.Offices ??
                                new List<CustomCourtOffice>();
            pTemplate.Edges = pTemplate.Edges ??
                              new List<CustomCourtEdge>();
            pTemplate.LocalTemplates = pTemplate.LocalTemplates ??
                                       new List<CustomLocalCourtTemplate>();
            pTemplate.ArchivedCrossLayerEdges =
                pTemplate.ArchivedCrossLayerEdges ??
                new List<CustomCourtEdge>();
            if (pTemplate.SchemaVersion < 2)
            {
                var localIds = new HashSet<string>(pTemplate.Offices
                    .Where(office => office != null &&
                                     office.Layer == CourtOfficeLayer.City)
                    .Select(office => office.Id), StringComparer.Ordinal);
                if (localIds.Count > 0)
                {
                    var local = new CustomLocalCourtTemplate
                    {
                        Id = LegacyDefaultTemplateId,
                        Name = new CustomCourtLocalizedText
                        {
                            Chinese = "地方官署",
                            English = "Local Government"
                        },
                        DefaultKind =
                            CustomLocalCourtDefaultKind.CivilDefault,
                        Offices = pTemplate.Offices.Where(office =>
                            office != null && localIds.Contains(office.Id))
                            .ToList(),
                        Edges = pTemplate.Edges.Where(edge => edge != null &&
                            localIds.Contains(edge.FromOfficeId) &&
                            localIds.Contains(edge.ToOfficeId)).ToList()
                    };
                    pTemplate.LocalTemplates.Add(local);
                }

                foreach (CustomCourtEdge edge in pTemplate.Edges)
                {
                    if (edge == null) continue;
                    bool fromLocal = localIds.Contains(edge.FromOfficeId);
                    bool toLocal = localIds.Contains(edge.ToOfficeId);
                    if (fromLocal != toLocal)
                        pTemplate.ArchivedCrossLayerEdges.Add(edge);
                }
                pTemplate.Offices = pTemplate.Offices.Where(office =>
                    office != null && !localIds.Contains(office.Id)).ToList();
                pTemplate.Edges = pTemplate.Edges.Where(edge => edge != null &&
                    !localIds.Contains(edge.FromOfficeId) &&
                    !localIds.Contains(edge.ToOfficeId)).ToList();
                pTemplate.SchemaVersion = 2;
            }

            if (pTemplate.SchemaVersion < 3)
            {
                UpgradeGeneratedDefault(pTemplate,
                    CustomLocalGovernmentPresetRules.CivilTemplateId,
                    CustomLocalCourtDefaultKind.CivilDefault,
                    CustomLocalGovernmentPresetRules.CreateCivil);
                UpgradeGeneratedDefault(pTemplate,
                    CustomLocalGovernmentPresetRules.MilitaryTemplateId,
                    CustomLocalCourtDefaultKind.MilitaryDefault,
                    CustomLocalGovernmentPresetRules.CreateMilitary);
                pTemplate.SchemaVersion = CurrentSchemaVersion;
            }
            return pTemplate;
        }

        private static void UpgradeGeneratedDefault(
            CustomCourtTemplate pTemplate, string pId,
            CustomLocalCourtDefaultKind pKind,
            Func<string, CustomLocalCourtTemplate> pFactory)
        {
            CustomLocalCourtTemplate current = pTemplate.LocalTemplates
                .FirstOrDefault(item => item != null &&
                    string.Equals(item.Id, pId, StringComparison.Ordinal));
            if (current == null)
            {
                pTemplate.LocalTemplates.Add(pFactory(pId));
                return;
            }
            if (!IsLegacyGeneratedSingleGovernor(current, pId, pKind))
                return;
            int index = pTemplate.LocalTemplates.IndexOf(current);
            pTemplate.LocalTemplates[index] = pFactory(pId);
        }

        public static bool IsLegacyGeneratedSingleGovernor(
            CustomLocalCourtTemplate pTemplate, string pId,
            CustomLocalCourtDefaultKind pKind)
        {
            if (pTemplate == null || pTemplate.Id != pId ||
                pTemplate.DefaultKind != pKind ||
                pTemplate.Offices == null || pTemplate.Offices.Count != 1 ||
                pTemplate.Edges == null || pTemplate.Edges.Count != 0)
                return false;
            string expectedName = pId ==
                CustomLocalGovernmentPresetRules.CivilTemplateId
                ? "民州"
                : "军府";
            if (pTemplate.Name == null ||
                pTemplate.Name.Chinese != expectedName ||
                pTemplate.Name.English != expectedName)
                return false;
            CustomCourtOffice office = pTemplate.Offices[0];
            if (office == null || office.Id != pId + "_governor" ||
                office.Layer != CourtOfficeLayer.City || office.Grade != 10 ||
                office.Slots != 1 || office.MilitaryCapable ||
                !string.IsNullOrEmpty(office.PreferredSchoolId) ||
                office.Layout == null || office.Layout.X != 0f ||
                office.Layout.Y != 0f || office.Layout.Lane != 0 ||
                office.Effects == null || office.Effects.Count != 0)
                return false;
            if (office.Requirements == null ||
                office.Requirements.MinimumRank != 0 ||
                !string.IsNullOrEmpty(office.Requirements.RequiredSchoolId) ||
                !string.IsNullOrEmpty(office.Requirements.RequiredTraitId) ||
                !string.IsNullOrEmpty(office.Requirements.RequiredOfficeId))
                return false;
            string name = office.Name?.Chinese ?? string.Empty;
            string english = office.Name?.English ?? string.Empty;
            return IsGeneratedGovernorName(name) &&
                   IsGeneratedGovernorName(english);
        }

        private static bool IsGeneratedGovernorName(string pName)
        {
            return pName == "Governor" || pName == "地方长官" ||
                   pName == "地方長官";
        }

        public static string ResolveTemplateId(
            IReadOnlyList<CustomLocalCourtTemplate> pTemplates,
            string persistedTemplateId, bool manualOverride,
            bool militaryCity)
        {
            List<CustomLocalCourtTemplate> valid = ValidTemplates(pTemplates);
            if (valid.Count == 0) return string.Empty;
            if (manualOverride && valid.Any(template =>
                    template.Id == persistedTemplateId))
                return persistedTemplateId;
            CustomLocalCourtDefaultKind preferred = militaryCity
                ? CustomLocalCourtDefaultKind.MilitaryDefault
                : CustomLocalCourtDefaultKind.CivilDefault;
            CustomLocalCourtTemplate selected = valid.FirstOrDefault(
                template => template.DefaultKind == preferred);
            return (selected ?? valid[0]).Id;
        }

        public static string CityTypeName(CustomLocalCourtTemplate pTemplate,
            bool useEnglish)
        {
            if (pTemplate?.Name == null) return string.Empty;
            string primary = useEnglish
                ? pTemplate.Name.English
                : pTemplate.Name.Chinese;
            string secondary = useEnglish
                ? pTemplate.Name.Chinese
                : pTemplate.Name.English;
            if (!string.IsNullOrWhiteSpace(primary)) return primary;
            if (!string.IsNullOrWhiteSpace(secondary)) return secondary;
            return pTemplate.Id ?? string.Empty;
        }

        public static bool CanDeleteTemplate(string pTemplateId,
            string replacementTemplateId, int inUseCityCount)
        {
            if (string.IsNullOrWhiteSpace(pTemplateId)) return false;
            if (inUseCityCount <= 0) return true;
            return !string.IsNullOrWhiteSpace(replacementTemplateId) &&
                   !string.Equals(pTemplateId, replacementTemplateId,
                       StringComparison.Ordinal);
        }

        private static List<CustomLocalCourtTemplate> ValidTemplates(
            IReadOnlyList<CustomLocalCourtTemplate> pTemplates)
        {
            return (pTemplates ?? Array.Empty<CustomLocalCourtTemplate>())
                .Where(template => template != null &&
                                   !string.IsNullOrWhiteSpace(template.Id))
                .OrderBy(template => template.Id, StringComparer.Ordinal)
                .Take(MaximumTemplates).ToList();
        }
    }
}
