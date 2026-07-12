namespace AncientWarfare3.core.lineage
{
    public static class OfficialShiRules
    {
        public static bool ShouldGrantOfficialShi(bool hasValidShi)
        {
            return !hasValidShi;
        }

        public static bool ShouldReuseParentVisibleClan(bool hasValidShi,
            bool parentHasSameShi, bool parentHasVisibleClan)
        {
            return hasValidShi && parentHasSameShi && parentHasVisibleClan;
        }

        public static bool ShouldSyncDescendant(long parentLineageId, long parentShiId,
            long childLineageId, long childShiId)
        {
            if (parentLineageId < 0 || parentShiId < 0) return false;
            if (childLineageId >= 0 && childLineageId != parentLineageId) return false;
            return childShiId < 0 || childShiId == parentShiId;
        }
    }
}
