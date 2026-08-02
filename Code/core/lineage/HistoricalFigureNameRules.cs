using System;

namespace AncientWarfare3.core.lineage
{
    public static class HistoricalFigureNameRules
    {
        public static bool ShouldProtect(bool hasHistoricalFigureTrait,
            string familyName, string givenName)
        {
            return hasHistoricalFigureTrait &&
                   !string.IsNullOrWhiteSpace(familyName) &&
                   !string.IsNullOrWhiteSpace(givenName);
        }

        public static string ResolveDisplayName(string familyName,
            string givenName, string currentName)
        {
            string family = (familyName ?? string.Empty).Trim();
            string given = (givenName ?? string.Empty).Trim();
            if (family.Length > 0 && given.Length > 0) return family + given;
            if (family.Length > 0) return family;
            if (given.Length > 0) return given;
            return (currentName ?? string.Empty).Trim();
        }
    }
}
