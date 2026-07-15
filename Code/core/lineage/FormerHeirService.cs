namespace AncientWarfare3.core.lineage
{
    internal static class FormerHeirService
    {
        public static void ArchiveAndClear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;

            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long heirId, -1L);
            Actor heir = null;
            if (heirId >= 0)
            {
                try { heir = World.world?.units?.get(heirId); }
                catch { }
            }
            bool heirAlive = heir?.data != null && heir.isAlive() && !heir.isRekt();
            if (FormerHeirTitleRules.ShouldSnapshot(
                    kingdomDestroyed: true,
                    registeredHeir: heirId >= 0,
                    heirAlive: heirAlive))
            {
                string title = HeirTitleRules.BuildSocialTitle(pKingdom.name ?? "", pKingdom);
                heir.data.set(LineageKeys.FORMER_HEIR_KINGDOM_ID, pKingdom.id);
                heir.data.set(LineageKeys.FORMER_HEIR_KINGDOM_NAME, pKingdom.name ?? "");
                heir.data.set(LineageKeys.FORMER_HEIR_KINGDOM_COLOR,
                    HistoryColors.FromKingdom(pKingdom));
                heir.data.set(LineageKeys.FORMER_HEIR_TITLE,
                    FormerHeirTitleRules.BuildFormerTitle(title));
            }

            HeirService.ClearHeir(pKingdom);
            if (heirAlive)
                LineageService.ArchiveActor(heir, pAlive: true);
        }

        public static void ClearSnapshot(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.set(LineageKeys.FORMER_HEIR_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.FORMER_HEIR_KINGDOM_NAME, "");
            pActor.data.set(LineageKeys.FORMER_HEIR_KINGDOM_COLOR, "");
            pActor.data.set(LineageKeys.FORMER_HEIR_TITLE, "");
        }
    }
}
