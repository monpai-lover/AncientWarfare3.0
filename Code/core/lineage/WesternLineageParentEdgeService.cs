namespace AncientWarfare3.core.lineage
{
    internal static class WesternLineageParentEdgeService
    {
        internal static LineageBirthArchiveResult RecordBirth(Actor pBaby,
            Actor pParent1,
            Actor pParent2, bool pUseLightweightEdges)
        {
            if (!pUseLightweightEdges)
                return new LineageBirthArchiveResult(
                    LineageBirthArchiveStatus.NotEligible,
                    pBaby?.data?.id ?? -1L,
                    pParent1?.data?.id ?? -1L,
                    pParent2?.data?.id ?? -1L,
                    "lightweight birth path does not own this child");
            return LineageBirthArchiveService.TryRecord(pBaby, pParent1,
                pParent2);
        }
    }
}
