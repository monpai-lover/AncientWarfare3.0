using System;

namespace AncientWarfare3.core.lineage
{
    public static class WarRuntimeDisplayRules
    {
        public static string ResolveName(string pLiveWarName,
            string pLocalizedWarName, string pGenericWarName)
        {
            if (IsDisplayName(pLiveWarName)) return pLiveWarName.Trim();
            if (IsDisplayName(pLocalizedWarName))
                return pLocalizedWarName.Trim();
            return IsDisplayName(pGenericWarName)
                ? pGenericWarName.Trim()
                : "War";
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
