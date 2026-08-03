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
            KingdomRenameProjectionRefreshContract.Apply(
                () => MandateService.RefreshKingdomNameProjection(pKingdom),
                RulerAppellationService.InvalidateFamilyTreeProjectionCaches,
                () => FamilyTreeProjectionRevision.Advance(
                    FamilyTreeProjectionChange.DynastyOrStateName));

            try { World.world?.nameplate_manager?.clearCaches(); }
            catch { }
            HierarchicalVassalMapModeService.MarkKingdomDirty(pKingdom);
        }
    }
}
