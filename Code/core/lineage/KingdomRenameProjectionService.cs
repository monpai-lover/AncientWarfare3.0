using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class KingdomRenameProjectionService
    {
        public static void Refresh(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            KingdomArchiveWriter.Upsert(pKingdom);
            RulerAppellationService.RefreshLivingProjection(pKingdom);

            try { World.world?.nameplate_manager?.clearCaches(); }
            catch { }
            HierarchicalVassalMapModeService.MarkKingdomDirty(pKingdom);
        }
    }
}
