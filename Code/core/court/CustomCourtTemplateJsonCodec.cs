using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtTemplateJsonCodec
    {
        private static readonly JsonSerializerSettings Settings =
            new JsonSerializerSettings
            {
                Culture = System.Globalization.CultureInfo.InvariantCulture,
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Include,
                DefaultValueHandling = DefaultValueHandling.Include
            };

        public static CustomCourtTemplate Normalize(
            CustomCourtTemplate template)
        {
            if (template == null)
                throw new ArgumentNullException(nameof(template));

            CustomCourtTemplate normalized = JsonConvert.DeserializeObject<
                CustomCourtTemplate>(JsonConvert.SerializeObject(template,
                    Settings));
            normalized = CustomLocalCourtTemplateRules.UpgradeLegacy(
                normalized);
            normalized.Offices = (normalized.Offices ??
                new List<CustomCourtOffice>()).Where(item => item != null)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToList();
            normalized.Edges = (normalized.Edges ??
                new List<CustomCourtEdge>()).Where(item => item != null)
                .OrderBy(item => item.Kind)
                .ThenBy(item => item.FromOfficeId, StringComparer.Ordinal)
                .ThenBy(item => item.ToOfficeId, StringComparer.Ordinal)
                .ToList();
            NormalizeOffices(normalized.Offices);
            normalized.LocalTemplates = (normalized.LocalTemplates ??
                new List<CustomLocalCourtTemplate>()).Where(item => item != null)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToList();
            foreach (CustomLocalCourtTemplate local in
                     normalized.LocalTemplates)
            {
                local.Name = local.Name ?? new CustomCourtLocalizedText();
                local.Offices = (local.Offices ??
                    new List<CustomCourtOffice>()).Where(item => item != null)
                    .OrderBy(item => item.Id, StringComparer.Ordinal).ToList();
                local.Edges = SortEdges(local.Edges);
                NormalizeOffices(local.Offices);
            }
            normalized.ArchivedCrossLayerEdges = SortEdges(
                normalized.ArchivedCrossLayerEdges);
            normalized.Name = normalized.Name ?? new CustomCourtLocalizedText();
            if (normalized.Offices.Count > 0)
                EnsureRegionalLayer(normalized);
            if (normalized.RegionalGovernmentLayer != null)
                NormalizeRegionalLayer(normalized.RegionalGovernmentLayer);
            return normalized;
        }

        public static string Export(CustomCourtTemplate template)
        {
            return JsonConvert.SerializeObject(Normalize(template), Settings);
        }

        public static bool TryImport(string json,
            out CustomCourtTemplate template,
            out CustomCourtTemplateValidationError error)
        {
            template = null;
            error = CustomCourtTemplateValidationError.InvalidTemplateId;
            if (string.IsNullOrWhiteSpace(json))
                return false;
            try
            {
                template = JsonConvert.DeserializeObject<CustomCourtTemplate>(
                    json, Settings);
                template = CustomLocalCourtTemplateRules.UpgradeLegacy(
                    template);
                if (template?.Offices != null && template.Offices.Count > 0)
                    EnsureRegionalLayer(template);
                error = CustomCourtTemplateRules.Validate(template);
                if (error != CustomCourtTemplateValidationError.None)
                {
                    template = null;
                    return false;
                }
                template = Normalize(template);
                return true;
            }
            catch (JsonException)
            {
                template = null;
                return false;
            }
        }

        public static string Hash(CustomCourtTemplate template)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(Export(template));
            using (SHA256 sha256 = SHA256.Create())
            {
                return string.Concat(sha256.ComputeHash(bytes).Select(
                    value => value.ToString("x2")));
            }
        }

        private static List<CustomCourtEdge> SortEdges(
            IEnumerable<CustomCourtEdge> pEdges)
        {
            return (pEdges ?? Array.Empty<CustomCourtEdge>())
                .Where(item => item != null).OrderBy(item => item.Kind)
                .ThenBy(item => item.FromOfficeId, StringComparer.Ordinal)
                .ThenBy(item => item.ToOfficeId, StringComparer.Ordinal)
                .ToList();
        }

        private static void NormalizeOffices(
            IEnumerable<CustomCourtOffice> pOffices)
        {
            if (pOffices == null) return;
            foreach (CustomCourtOffice office in pOffices)
            {
                office.Name = office.Name ?? new CustomCourtLocalizedText();
                office.Layout = office.Layout ?? new CustomCourtOfficeLayout();
                office.Requirements = office.Requirements ??
                    new CustomCourtOfficeRequirement();
                office.Effects = (office.Effects ??
                    new List<CustomCourtOfficeEffect>()).Where(item =>
                        item != null).OrderBy(item => item.Id)
                    .ThenBy(item => item.Mode).ThenBy(item => item.Scope)
                    .ThenBy(item => item.Value).ToList();
            }
        }

        internal static void EnsureRegionalLayer(CustomCourtTemplate pTemplate)
        {
            if (pTemplate == null) return;
            pTemplate.RegionalGovernmentLayer =
                pTemplate.RegionalGovernmentLayer ??
                new CustomCourtRegionalGovernmentLayer();
            NormalizeRegionalLayer(pTemplate.RegionalGovernmentLayer);
        }

        private static void NormalizeRegionalLayer(
            CustomCourtRegionalGovernmentLayer pLayer)
        {
            if (pLayer == null) return;
            pLayer.Id = "regional_government_layer";
            pLayer.RegionTitle = pLayer.RegionTitle ??
                new CustomCourtLocalizedText();
            pLayer.GovernorTitle = pLayer.GovernorTitle ??
                new CustomCourtLocalizedText();
            if (string.IsNullOrWhiteSpace(pLayer.RegionTitle.Chinese))
                pLayer.RegionTitle.Chinese = "郡";
            if (string.IsNullOrWhiteSpace(pLayer.RegionTitle.English))
                pLayer.RegionTitle.English = "Commandery";
            if (string.IsNullOrWhiteSpace(pLayer.GovernorTitle.Chinese))
                pLayer.GovernorTitle.Chinese = "郡守";
            if (string.IsNullOrWhiteSpace(pLayer.GovernorTitle.English))
                pLayer.GovernorTitle.English = "Regional Governor";
            pLayer.ManagementOfficeIds = (pLayer.ManagementOfficeIds ??
                    new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal).OrderBy(id => id,
                    StringComparer.Ordinal).ToList();
        }
    }
}
