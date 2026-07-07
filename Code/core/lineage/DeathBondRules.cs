namespace AncientWarfare3.core.lineage
{
    public static class DeathBondRules
    {
        public static bool ShouldRecordBondDeathForParentsAndLover(bool pDeadIsTraceable)
        {
            return pDeadIsTraceable;
        }

        public static bool ShouldUseWorldScanForChildren(bool pCanUseActorChildrenList, bool pDeadIsImportant)
        {
            if (pCanUseActorChildrenList) return false;
            return pDeadIsImportant;
        }
    }
}
