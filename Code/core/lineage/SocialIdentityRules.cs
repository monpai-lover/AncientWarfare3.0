using System;

namespace AncientWarfare3.core.lineage
{
    internal enum SocialIdentityClass
    {
        None,
        Noble,
        ScholarOfficial
    }

    internal static class SocialIdentityRules
    {
        internal static SocialIdentityClass Resolve(bool isOfficial,
            bool isActing, bool isCurrentRuler, bool isRegisteredHeir,
            bool isRoyalRelative, bool hasFormalTitle)
        {
            if (isActing) return SocialIdentityClass.None;
            if (isCurrentRuler || isRegisteredHeir || isRoyalRelative ||
                hasFormalTitle) return SocialIdentityClass.Noble;
            return isOfficial ? SocialIdentityClass.ScholarOfficial :
                SocialIdentityClass.None;
        }

        internal static bool ShouldHaveNobleTrait(SocialIdentityClass pClass)
        {
            return pClass == SocialIdentityClass.Noble;
        }

        internal static bool ShouldHaveScholarTrait(SocialIdentityClass pClass)
        {
            return pClass == SocialIdentityClass.ScholarOfficial;
        }
    }
}
