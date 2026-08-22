namespace AncientWarfare3.core.lineage
{
    internal static class DeJureWarEligibilityRules
    {
        internal static bool HasCommonRegionMembers(int pSourceMemberCount,
            int pTargetMemberCount)
        {
            return pSourceMemberCount > 0 && pTargetMemberCount > 0;
        }
    }
}
