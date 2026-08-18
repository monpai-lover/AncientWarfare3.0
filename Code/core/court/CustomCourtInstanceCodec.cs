using System;
using Newtonsoft.Json;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtInstanceCodec
    {
        private static readonly JsonSerializerSettings Settings =
            new JsonSerializerSettings
            {
                Culture = System.Globalization.CultureInfo.InvariantCulture,
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Include
            };

        public static string Export(CustomCourtInstance instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));
            return JsonConvert.SerializeObject(instance, Settings);
        }

        public static bool TryImport(string json,
            out CustomCourtInstance instance)
        {
            instance = null;
            if (string.IsNullOrWhiteSpace(json))
                return false;
            try
            {
                instance = JsonConvert.DeserializeObject<CustomCourtInstance>(
                    json, Settings);
                if (instance?.ResolvedSnapshot != null)
                    instance.ResolvedSnapshot =
                        CustomLocalCourtTemplateRules.UpgradeLegacy(
                            instance.ResolvedSnapshot);
                return instance != null &&
                    instance.SchemaVersion == 1 &&
                    CustomCourtInstanceRules.IsValidKingdomId(
                        instance.KingdomId) &&
                    CustomCourtTemplateRules.IsValidTemplateId(
                        instance.TemplateId) &&
                    instance.ResolvedSnapshot != null &&
                    CustomCourtTemplateRules.Validate(
                        instance.ResolvedSnapshot) ==
                        CustomCourtTemplateValidationError.None;
            }
            catch (JsonException)
            {
                instance = null;
                return false;
            }
        }
    }
}
