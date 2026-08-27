using System;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelStateNameRules
    {
        internal static string ResolvePreferredName(
            string pFamilyStateName, string pFallbackRoot)
        {
            string family = (pFamilyStateName ?? string.Empty).Trim();
            if (StateNameRules.IsValid(family)) return family;
            return PeasantRebelOutlawNameRules.NormalizeRoot(
                pFallbackRoot ?? string.Empty);
        }

        internal static bool IsFamilyStateName(string pCandidate,
            string pFamilyStateName)
        {
            string candidate = (pCandidate ?? string.Empty).Trim();
            string family = (pFamilyStateName ?? string.Empty).Trim();
            return StateNameRules.IsValid(family) &&
                   string.Equals(candidate, family,
                       StringComparison.Ordinal);
        }
    }
}
