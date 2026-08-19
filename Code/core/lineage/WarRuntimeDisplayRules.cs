using System;

namespace AncientWarfare3.core.lineage
{
    public static class WarRuntimeDisplayRules
    {
        public static string ResolveName(string pLiveWarName,
            string pLocalizedWarName, string pGenericWarName)
        {
            if (IsDisplayName(pLocalizedWarName))
                return pLocalizedWarName.Trim();
            if (IsDisplayName(pLiveWarName) &&
                !LooksLikeGeneratedNativeWarName(pLiveWarName))
                return pLiveWarName.Trim();
            return IsDisplayName(pGenericWarName)
                ? pGenericWarName.Trim()
                : "War";
        }

        private static bool LooksLikeGeneratedNativeWarName(string pValue)
        {
            if (string.IsNullOrWhiteSpace(pValue)) return false;
            string value = pValue.Trim();
            return value.StartsWith("Great War of ",
                       StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("War of ",
                       StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("Battle of ",
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsDisplayName(string pValue)
        {
            if (string.IsNullOrWhiteSpace(pValue)) return false;
            string value = pValue.Trim();
            if (string.Equals(value, "name",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "unknown",
                    StringComparison.OrdinalIgnoreCase) ||
                value == "?") return false;
            if (value.StartsWith("war_name_", StringComparison.Ordinal) ||
                value.StartsWith("war_type_", StringComparison.Ordinal))
                return false;
            return value.IndexOf('$') < 0 && value.IndexOf('{') < 0 &&
                   value.IndexOf('}') < 0;
        }

        public static bool HasNamedWinner(long pSpeakerKingdomId)
        {
            return pSpeakerKingdomId >= 0L;
        }
    }
}
