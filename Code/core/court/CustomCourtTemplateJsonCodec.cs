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
            normalized.Offices = (normalized.Offices ??
                new List<CustomCourtOffice>()).Where(item => item != null)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToList();
            normalized.Edges = (normalized.Edges ??
                new List<CustomCourtEdge>()).Where(item => item != null)
                .OrderBy(item => item.Kind)
                .ThenBy(item => item.FromOfficeId, StringComparer.Ordinal)
                .ThenBy(item => item.ToOfficeId, StringComparer.Ordinal)
                .ToList();
            foreach (CustomCourtOffice office in normalized.Offices)
            {
                office.Name = office.Name ?? new CustomCourtLocalizedText();
                office.Layout = office.Layout ?? new CustomCourtOfficeLayout();
                office.Requirements = office.Requirements ??
                    new CustomCourtOfficeRequirement();
                office.Effects = (office.Effects ??
                    new List<CustomCourtOfficeEffect>()).OrderBy(item => item.Id)
                    .ThenBy(item => item.Mode).ThenBy(item => item.Scope)
                    .ThenBy(item => item.Value).ToList();
            }
            normalized.Name = normalized.Name ?? new CustomCourtLocalizedText();
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
    }
}
