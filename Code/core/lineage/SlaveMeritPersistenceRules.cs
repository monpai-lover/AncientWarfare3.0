namespace AncientWarfare3.core.lineage
{
    public static class SlaveMeritPersistenceRules
    {
        public static bool ShouldPersist(int pOldMerit, int pNewMerit, int pPoints,
            int pMilestone, int pFreedomThreshold)
        {
            if (pNewMerit >= pFreedomThreshold) return false;
            if (pPoints >= 4) return true;

            int milestone = System.Math.Max(1, pMilestone);
            return pOldMerit / milestone != pNewMerit / milestone;
        }
    }
}
