namespace AncientWarfare3.core.lineage
{
    public static class VassalAnnexGuardReconciliationRules
    {
        public static bool ShouldReconcile(bool pCityTransferCommitted,
            bool pRelationClosed, bool pAbsorbed)
        {
            return pCityTransferCommitted && pRelationClosed && pAbsorbed;
        }
    }
}
