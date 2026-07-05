namespace AncientWarfare3.core.lineage
{
    public static class MapModeFocusRules
    {
        public static long ResolveFocusId(long pCurrentFocusId, long pSelectedKingdomId)
        {
            if (pCurrentFocusId >= 0) return pCurrentFocusId;
            return pSelectedKingdomId >= 0 ? pSelectedKingdomId : -1L;
        }
    }
}
