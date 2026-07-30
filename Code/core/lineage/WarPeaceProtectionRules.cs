using System;

namespace AncientWarfare3.core.lineage
{
    public static class WarPeaceProtectionRules
    {
        public static bool IsProtected(string pWarType,
            bool pMandateConquest, bool pHasProtectedGoal,
            bool pAuthoritativeRebellion = false)
        {
            if (pMandateConquest || pHasProtectedGoal ||
                pAuthoritativeRebellion) return true;
            string warType = pWarType ?? string.Empty;
            return warType.Equals("tianming", StringComparison.Ordinal) ||
                   warType.Equals("tianmingrebel",
                       StringComparison.Ordinal) ||
                   warType.Equals("independence_war",
                       StringComparison.Ordinal) ||
                   warType.Equals("fief_independence_war",
                       StringComparison.Ordinal) ||
                   warType.Equals("general_rebellion_war",
                       StringComparison.Ordinal) ||
                   warType.Equals("coup_restoration_war",
                       StringComparison.Ordinal);
        }
    }
}
