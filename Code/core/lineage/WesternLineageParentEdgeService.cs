namespace AncientWarfare3.core.lineage
{
    internal static class WesternLineageParentEdgeService
    {
        internal static bool RecordBirth(Actor pBaby, Actor pParent1,
            Actor pParent2, bool pUseLightweightEdges)
        {
            if (!pUseLightweightEdges) return false;
            return LineageService.RecordLightweightParentEdges(pBaby,
                pParent1, pParent2);
        }
    }
}
