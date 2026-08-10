namespace AncientWarfare3.core.lineage
{
    public enum NativeSiniticIdentityMigrationAction
    {
        Skip,
        Reuse,
        Repair
    }

    public static class NativeSiniticIdentityMigrationRules
    {
        public static NativeSiniticIdentityMigrationAction Decide(
            bool targetProfile,
            bool protectedName,
            bool completeIdentity,
            bool legacyWesternBranch,
            bool branchFamilyMismatch)
        {
            if (!targetProfile || protectedName)
                return NativeSiniticIdentityMigrationAction.Skip;

            return completeIdentity && !legacyWesternBranch && !branchFamilyMismatch
                ? NativeSiniticIdentityMigrationAction.Reuse
                : NativeSiniticIdentityMigrationAction.Repair;
        }
    }
}
