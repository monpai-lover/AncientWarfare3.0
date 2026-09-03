using System;

namespace AncientWarfare3.core.court
{
    /// <summary>
    ///     宗族在官场里够到的最高层级。数值大的压过数值小的，
    ///     取整族成员的最大值即为该族的门第依据。
    /// </summary>
    internal enum ShiOfficeReach
    {
        /// <summary>族内无人任官。</summary>
        None = 0,
        /// <summary>只到县一级等低级地方官。</summary>
        LocalOnly = 1,
        /// <summary>出过城主或中央/御史/封国一级的官。</summary>
        HighOffice = 2
    }

    /// <summary>
    ///     门第（贵族 / 世家 / 寒门 / 平民）的判定规则。
    ///
    ///     旧判据看的是**个人**的谱系状态与爵位：
    ///     <c>LINEAGE_STATUS == "noble"</c> 即贵族，而
    ///     <c>NobleIdentityService.IsNobleActor</c> 又把「有爵位的人」一并算进去。
    ///     爵位是随官职发的（县伯之类），于是当过官的基本都成了贵族 ——
    ///     科举名单上放眼望去全是贵族，与实际的社会结构完全不符。
    ///
    ///     新判据以**宗族**为单位，看这一族在官场里够到哪一层：
    ///     <list type="bullet">
    ///     <item><b>贵族</b>：本人的氏就是所在国当今王室的氏。改朝换代之后
    ///           旧王室自动降为世家，不再世袭这个身份。</item>
    ///     <item><b>世家</b>：族内有人做到城主或中央官这一层。</item>
    ///     <item><b>寒门</b>：有氏，但族内最高只到低级地方官（含全族无官）。</item>
    ///     <item><b>平民</b>：无氏。</item>
    ///     </list>
    ///
    ///     纯函数，不碰世界状态。
    /// </summary>
    internal static class SocialStandingRules
    {
        /// <summary>
        ///     某个官职层级是否算「高门」——— 城主与中央一级。
        ///
        ///     军职单独留在低层：领兵不等于门第，且武人多出自寒门，
        ///     把军职算成高门会让判定重新滑回「人人皆贵」。
        /// </summary>
        internal static bool IsHighOfficeLayer(string pLayer)
        {
            return string.Equals(pLayer, CourtOfficeLayer.Central,
                       StringComparison.Ordinal) ||
                   string.Equals(pLayer, CourtOfficeLayer.Censor,
                       StringComparison.Ordinal) ||
                   string.Equals(pLayer, CourtOfficeLayer.Feudatory,
                       StringComparison.Ordinal) ||
                   string.Equals(pLayer, CourtOfficeLayer.City,
                       StringComparison.Ordinal);
        }

        /// <summary>
        ///     单个成员的任官情况折算成本族的够到层级。
        /// </summary>
        internal static ShiOfficeReach ReachOf(bool pIsCityLeader,
            string pOfficeLayer)
        {
            if (pIsCityLeader || IsHighOfficeLayer(pOfficeLayer))
                return ShiOfficeReach.HighOffice;
            return string.IsNullOrEmpty(pOfficeLayer)
                ? ShiOfficeReach.None
                : ShiOfficeReach.LocalOnly;
        }

        internal static ShiOfficeReach Max(ShiOfficeReach pLeft,
            ShiOfficeReach pRight)
        {
            return pLeft >= pRight ? pLeft : pRight;
        }

        /// <summary>
        ///     最终门第。
        /// </summary>
        /// <param name="pHasShi">本人是否有氏。</param>
        /// <param name="pIsRoyalShi">
        ///     本人的氏是否就是所在国当今王室的氏。
        /// </param>
        /// <param name="pReach">全族够到的最高官职层级。</param>
        internal static string Resolve(bool pHasShi, bool pIsRoyalShi,
            ShiOfficeReach pReach)
        {
            // 无氏即平民,不看官职 —— 门第讲的是族,没有族就无从谈起。
            if (!pHasShi) return CivilServiceExamRules.CommonerOrigin;
            // 贵族只认当今王室这一支。
            if (pIsRoyalShi) return CivilServiceExamRules.NobleOrigin;
            if (pReach == ShiOfficeReach.HighOffice)
                return CivilServiceExamRules.GentryOrigin;
            // 有氏但没够到高位 —— 含全族无官,一律寒门。
            return CivilServiceExamRules.DeclinedNobleOrigin;
        }
    }
}
