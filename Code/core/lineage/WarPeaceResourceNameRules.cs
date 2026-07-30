using System;
using System.Text;

namespace AncientWarfare3.core.lineage
{
    public static class WarPeaceResourceNameRules
    {
        private const string LocalePrefix = "aw_war_peace_resource_";

        public static string Resolve(string pResourceId,
            string pOriginalTranslation, string pFallbackTranslation)
        {
            string resourceId = (pResourceId ?? string.Empty).Trim();
            string original = (pOriginalTranslation ?? string.Empty).Trim();
            if (IsTranslatedName(original, resourceId)) return original;

            string fallback = (pFallbackTranslation ?? string.Empty).Trim();
            if (IsTranslatedName(fallback, resourceId)) return fallback;
            return HumanizeId(resourceId);
        }

        public static string FallbackLocaleKey(string pResourceId)
        {
            string resourceId = NormalizeId(pResourceId);
            return resourceId.Length == 0
                ? LocalePrefix + "generic"
                : LocalePrefix + resourceId;
        }

        public static string BuiltInEnglishFallback(string pResourceId)
        {
            switch (NormalizeId(pResourceId))
            {
                case "gold": return "Gold";
                case "mushrooms": return "Mushrooms";
                case "herbs": return "Herbs";
                case "meat": return "Meat";
                default: return HumanizeId(pResourceId);
            }
        }

        private static string HumanizeId(string pResourceId)
        {
            string value = (pResourceId ?? string.Empty).Trim();
            if (value.Length == 0) return string.Empty;
            var result = new StringBuilder(value.Length);
            bool uppercaseNext = true;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '_' || c == '-')
                {
                    if (result.Length > 0 && result[result.Length - 1] != ' ')
                        result.Append(' ');
                    uppercaseNext = true;
                    continue;
                }
                result.Append(uppercaseNext ? char.ToUpperInvariant(c) : c);
                uppercaseNext = false;
            }
            return result.ToString().Trim();
        }

        private static bool IsTranslatedName(string pValue,
            string pResourceId)
        {
            if (string.IsNullOrWhiteSpace(pValue)) return false;
            if (string.Equals(pValue, pResourceId,
                    StringComparison.OrdinalIgnoreCase)) return false;
            return !string.Equals(pValue, "name",
                       StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(pValue, "???",
                       StringComparison.Ordinal);
        }

        private static string NormalizeId(string pResourceId)
        {
            string value = (pResourceId ?? string.Empty).Trim()
                .ToLowerInvariant();
            if (value.Length == 0) return string.Empty;
            var result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                result.Append(char.IsLetterOrDigit(c) || c == '_'
                    ? c
                    : '_');
            }
            return result.ToString();
        }
    }
}
