namespace AncientWarfare3.core.lineage
{
    /// <summary>国家头衔等级(对齐 AW2 KingdomTitle):伯/侯/公/王/帝;天命王朝的帝级显示"朝"。</summary>
    internal enum KingdomTitle
    {
        Baron = 0,   // 伯国
        Marquis = 1, // 侯国
        Duke = 2,    // 公国
        King = 3,    // 王国
        Emperor = 4  // 帝国(天命=朝)
    }

    /// <summary>
    ///     国家头衔系统(参考 AW2 AW_Kingdom 的 Title/GetTitleString/GetSingleCharacterTitle/GetCitiesBonus)。
    ///     存 kingdom.data 的 aw_title(int)。
    ///
    ///     **升级触发先留空**:默认 Baron(伯国),等 AW3 政策/天命系统迁移后再接入 PromoteTitle。
    ///     当前只提供 读取 / 单字 / 国号后缀 / 城市上限加成,供年号与 UI 使用。
    /// </summary>
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
            pKingdom?.data?.set(LineageKeys.KINGDOM_TITLE, (int)pTitle);
        }

        /// <summary>升级头衔(留给政策/天命系统调用);上限 Emperor。</summary>
        public static void PromoteTitle(Kingdom pKingdom)
        {
            var t = GetTitle(pKingdom);
            if (t < KingdomTitle.Emperor) SetTitle(pKingdom, t + 1);
        }

        /// <summary>头衔单字:伯/侯/公/王/帝。</summary>
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

        /// <summary>国号后缀:伯国/侯国/公国/王国/帝国。</summary>
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

        /// <summary>城市数量上限加成(伯0/侯2/公4/王8/帝16,同 AW2)。供以后接入 getMaxCities。</summary>
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
