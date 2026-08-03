using AncientWarfare3.core.court;
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
            RulerAppellationService.InvalidateFamilyTreeProjectionCaches();
            MandateService.RefreshKingdomNameProjection(pKingdom);
            CitySchoolSnapshotService.MarkKingdomDirty(pKingdom);
            CourtDirectionService.MarkDirty(pKingdom);

            try { World.world?.nameplate_manager?.clearCaches(); }
            catch { }
            SchoolMapModeService.DirtyMapIfActive();
            FeudatoryMapModeService.DirtyMapIfActive();
            VassalMapModeService.DirtyMapIfActive();
            MandateDynastyMapModeService.DirtyMapIfActive();
            MandateCoreMapModeService.DirtyMapIfActive();
            TechMapModeService.DirtyMapIfActive();
            DevelopmentMapModeService.DirtyMapIfActive();
            WarClaimMapModeService.DirtyMapIfActive();
            WarCoreMapModeService.DirtyMapIfActive();
            HierarchicalVassalMapModeService.MarkKingdomDirty(pKingdom);
            FamilyTreeProjectionRevision.Advance(
                FamilyTreeProjectionChange.DynastyOrStateName);
        }
    }
}
