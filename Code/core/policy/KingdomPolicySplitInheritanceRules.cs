using System;

namespace AncientWarfare3.core.policy
{
    public static class KingdomPolicySplitInheritanceRules
    {
        public const int XiaInstitutionsLevel = 4;
        public const int MaximumXiaizationLevel = 5;

        public static bool ShouldCaptureSplitSource(bool pRebellion,
            bool pFellApart, bool pIsIdentityRestoration,
            bool pFounderValid, bool pSourceValid, bool pSourceAlive)
        {
            return (pRebellion || pFellApart) &&
                   !pIsIdentityRestoration && pFounderValid &&
                   pSourceValid && pSourceAlive;
        }

        public static bool ShouldInheritFromSplit(bool pHasCapturedSource,
            bool pNewKingdomValid, bool pSourceValid, bool pSourceAlive,
            bool pChildHasPolicyProfile)
        {
            return pHasCapturedSource && pNewKingdomValid &&
                   pSourceValid && pSourceAlive &&
                   pChildHasPolicyProfile;
        }

        public static bool ShouldMarkCultureIntegrated(bool pNativeXiaCulture,
            int pPersistedXiaizationLevel)
        {
            return pNativeXiaCulture ||
                   pPersistedXiaizationLevel >= XiaInstitutionsLevel;
        }

        public static bool ShouldMarkCultureFullyIntegrated(
            int pPersistedXiaizationLevel)
        {
            return pPersistedXiaizationLevel >= MaximumXiaizationLevel;
        }

        public static int NormalizeInheritedXiaizationLevel(int pSourceLevel)
        {
            if (pSourceLevel <= 0) return 0;
            return pSourceLevel >= MaximumXiaizationLevel
                ? MaximumXiaizationLevel
                : pSourceLevel;
        }

        public static string ResolveInheritedGovernmentState(
            string pChildProfileId, string pSourceGovernmentState)
        {
            if (!string.Equals(pChildProfileId, "western_general",
                    StringComparison.Ordinal)) return "default";
            string state = (pSourceGovernmentState ?? string.Empty).Trim();
            switch (state)
            {
                case "western_noble_council":
                case "western_elective":
                case "western_feudal":
                case "western_royal_direct":
                    return state;
                default:
                    return "default";
            }
        }

        public static int ResolveInheritedRoyalAuthority(
            string pChildProfileId, string pSourceProfileId,
            int pSourceAuthority, int pMaximumAuthority = 30)
        {
            if (!string.Equals(pChildProfileId, "western_general",
                    StringComparison.Ordinal) ||
                !string.Equals(pSourceProfileId, "western_general",
                    StringComparison.Ordinal) || pSourceAuthority <= 0 ||
                pMaximumAuthority <= 0) return 0;
            int bounded = Math.Min(pMaximumAuthority, pSourceAuthority);
            return bounded / 2;
        }
    }
}
