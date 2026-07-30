namespace AncientWarfare3.core.lineage
{
    /// <summary>国家爵位等级：伯/侯/公/王/帝；天命王朝帝级显示为“朝”。</summary>
    internal enum KingdomTitle
    {
        Baron = 0,
        Marquis = 1,
        Duke = 2,
        King = 3,
        Emperor = 4
    }

    internal static class KingdomTitleService
    {
        public static KingdomTitle GetTitle(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return KingdomTitle.Baron;
            pKingdom.data.get(LineageKeys.KINGDOM_TITLE, out int t, (int)KingdomTitle.Baron);
            return (KingdomTitle)t;
        }

        public static void SetTitle(Kingdom pKingdom, KingdomTitle pTitle)
        {
            if (pKingdom?.data == null) return;
            KingdomTitle previous = GetTitle(pKingdom);
            pKingdom.data.set(LineageKeys.KINGDOM_TITLE, (int)pTitle);
            RulerAppellationService.RefreshLivingProjection(pKingdom);
            if (previous != pTitle)
            {
                Actor heir = HeirService.PeekStoredHeirForMinimap(pKingdom);
                if (heir?.data != null)
                    LineageService.ArchiveActor(heir, pAlive: true);
                FamilyTreeProjectionRevision.Advance(
                    FamilyTreeProjectionChange.RankOrMandate);
            }
            if (previous >= KingdomTitle.Emperor || pTitle != KingdomTitle.Emperor ||
                pKingdom.king?.data == null) return;

            pKingdom.king.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            if (shiId < 0) return;
            RetrospectiveTitleService.TryAwardFirstImperialAncestors(
                pKingdom, pKingdom.king, shiId,
                DynastyRecordWriter.GetCurrentDynastyId(pKingdom.id));
            YearNameService.TryStartImperialProclamationEra(
                pKingdom, pKingdom.king);
        }

        public static void PromoteTitle(Kingdom pKingdom)
        {
            var t = GetTitle(pKingdom);
            if (t < KingdomTitle.Emperor) SetTitle(pKingdom, t + 1);
        }

        public static string GetTitleChar(KingdomTitle pTitle)
        {
            return pTitle switch
            {
                KingdomTitle.Baron => "伯",
                KingdomTitle.Marquis => "侯",
                KingdomTitle.Duke => "公",
                KingdomTitle.King => "王",
                KingdomTitle.Emperor => "帝",
                _ => ""
            };
        }

        public static string GetTitleString(KingdomTitle pTitle)
        {
            return pTitle switch
            {
                KingdomTitle.Baron => "伯国",
                KingdomTitle.Marquis => "侯国",
                KingdomTitle.Duke => "公国",
                KingdomTitle.King => "王国",
                KingdomTitle.Emperor => "帝国",
                _ => "未知"
            };
        }

        public static int GetCitiesBonus(KingdomTitle pTitle)
        {
            return pTitle switch
            {
                KingdomTitle.Baron => 0,
                KingdomTitle.Marquis => 2,
                KingdomTitle.Duke => 4,
                KingdomTitle.King => 8,
                KingdomTitle.Emperor => 16,
                _ => 0
            };
        }

        public static bool IsEmperor(Kingdom pKingdom)
        {
            return GetTitle(pKingdom) == KingdomTitle.Emperor;
        }
    }
}
