using System;

namespace AncientWarfare3.core.court
{
    /// <summary>
    /// 官职名到底有没有翻出来。
    ///
    /// <c>CourtInstitutionService.OfficeName</c> 查不到译名时会退回裸 office id
    /// （<c>AW_L10n.Text(key, office)</c> 的 fallback 就是 <c>office</c> 本身）。
    /// 活人界面上这只是当帧显示难看,下一帧 locale 到位就好了;**归档不一样** ——
    /// 死者的 social_title 是当年写进 DB 的定型字符串,不再重算。裸 id 一旦写进去
    /// 就永久留在存档里,玩家看到的就是「顺昌 太守 · governor」。
    ///
    /// 所以归档路径需要一个判据:这段名字是真译名,还是 fallback 漏出来的 id。
    /// 抽出来单独放是为了能在 Rules.Tests 里直接测 —— 它不碰 Unity、不碰 DB。
    /// </summary>
    public static class CourtOfficeDisplayRules
    {
        public static bool IsJurisdictionalLayer(string pLayer)
        {
            return string.Equals(pLayer, CourtOfficeLayer.City,
                       StringComparison.Ordinal) ||
                   string.Equals(pLayer, CourtOfficeLayer.County,
                       StringComparison.Ordinal) ||
                   string.Equals(pLayer, CourtOfficeLayer.Regional,
                       StringComparison.Ordinal) ||
                   string.Equals(pLayer, CourtOfficeLayer.Feudatory,
                       StringComparison.Ordinal);
        }

        public static string ComposeJurisdictionalTitle(string pLayer,
            string pJurisdiction, string pOfficeName)
        {
            string office = (pOfficeName ?? string.Empty).Trim();
            if (office.Length == 0 || !IsJurisdictionalLayer(pLayer))
                return office;

            string jurisdiction = (pJurisdiction ?? string.Empty).Trim();
            if (jurisdiction.Length == 0 || office.StartsWith(jurisdiction,
                    StringComparison.Ordinal)) return office;
            return jurisdiction + " " + office;
        }

        /// <summary>
        /// <paramref name="pDisplayName"/> 是不是根本没翻出来。
        ///
        /// 判据只有一条:显示名和 office id 逐字符相同。这是 fallback 漏出来的
        /// 唯一形态 —— <c>AW_L10n.Text</c> 拿不到 key 时原样返回传入的默认值。
        ///
        /// 注意<b>不能</b>顺手加「含 ASCII 字母就算没翻」这类启发式:西式官职
        /// (<c>west_*</c>)的译名本身就是拉丁字母,那样会把正常译名判成漏译。
        /// </summary>
        public static bool IsUntranslated(string pDisplayName, string pOfficeId)
        {
            if (string.IsNullOrWhiteSpace(pDisplayName)) return true;
            if (string.IsNullOrWhiteSpace(pOfficeId)) return false;
            // OfficeName 内部先过 DisplayOfficeId,所以要和映射后的 id 比。
            // 直接拿原始 office id 比会漏掉 city_leader:* → governor 这一类:
            // 那正是本次真实遇到的漏译。
            string display = CourtInstitutionRules.DisplayOfficeId(pOfficeId);
            return string.Equals(pDisplayName.Trim(), display,
                       StringComparison.Ordinal) ||
                   string.Equals(pDisplayName.Trim(), pOfficeId.Trim(),
                       StringComparison.Ordinal);
        }
    }
}
