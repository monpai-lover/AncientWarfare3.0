namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// 身份词的判定：贵族 / 世家 / 寒门 / 平民 / 奴隶。
    ///
    /// 以前这几档笼统显示成「贵族」，把三种完全不同的处境混成了一个词。
    /// 现在按两个正交信号分：
    ///
    /// <list type="bullet">
    ///   <item><b>是不是皇亲国戚</b> —— 本人的氏，是不是某个**现存**王国当朝的
    ///         统治之氏。只有这一档配得上「贵族」。国灭或改朝换代之后这个信号
    ///         自然消失，不需要另外去销毁什么；</item>
    ///   <item><b>有没有氏</b> —— 有氏说明这一支进过谱系体系、立过门户。
    ///         没有氏就是从来没有门第可言的普通平民。</item>
    /// </list>
    ///
    /// 于是：
    /// <list type="bullet">
    ///   <item><b>贵族</b> —— 有氏，且该氏是现存王国的统治之氏（皇亲国戚）；</item>
    ///   <item><b>世家</b> —— 有氏、仍持爵位或仍在士族之列，但与当朝统治家族无关；</item>
    ///   <item><b>寒门</b> —— 平民化的旧世家：有氏，但已跌回平民身份。
    ///         门第还在族谱上，人已经不在士族之列了；</item>
    ///   <item><b>平民</b> —— 无氏，也就无所谓衰落；</item>
    ///   <item><b>奴隶</b> —— 独立一档，不参与门第判定。</item>
    /// </list>
    ///
    /// 判定只看传进来的三个值，不查 DB 也不碰 locale —— 返回的是 locale **键**，
    /// 由调用方去取词。这样这套分档能在 Rules.Tests 里直接对拍。
    /// </summary>
    public static class GentryIdentityRules
    {
        /// <summary>皇亲国戚：现存王国当朝统治之氏。</summary>
        public const string KeyNoble = "aw_identity_noble";

        /// <summary>士族门第，但与当朝统治家族无关。</summary>
        public const string KeyGentry = "aw_identity_gentry";

        /// <summary>平民化的旧世家 —— 门第尚在，身份已落。</summary>
        public const string KeyDeclined = "aw_identity_declined";

        /// <summary>从无门第可言。</summary>
        public const string KeyCommon = "aw_identity_common";

        /// <summary>奴籍。</summary>
        public const string KeySlave = "aw_identity_slave";

        /// <summary>没有氏时 SHI_ID 取这个值。</summary>
        public const long NoShi = -1L;

        /// <summary>有没有氏 —— 门第判定的前提。</summary>
        public static bool HasShi(long pShiId)
        {
            return pShiId > NoShi;
        }

        /// <summary>
        /// 定身份，返回 locale 键。
        ///
        /// <paramref name="pRulingShi"/> 由调用方按「该氏是否为某现存王国当朝
        /// 统治之氏」算好传进来 —— 这里不知道王国，也不该知道。
        ///
        /// 奴籍优先于一切门第：卖身为奴的世家子弟，显示的是奴隶。
        /// </summary>
        public static string Classify(string pStatus, long pShiId,
            bool pRulingShi)
        {
            if (pStatus == LineageStatus.SLAVE) return KeySlave;

            bool hasShi = HasShi(pShiId);
            // 皇亲国戚必须同时有氏 —— 统治之氏本身就是一个氏，
            // 没有氏的人不可能属于它。
            if (pRulingShi && hasShi) return KeyNoble;

            if (pStatus == LineageStatus.NOBLE)
                return hasShi ? KeyGentry : KeyCommon;

            if (pStatus == LineageStatus.COMMON)
                // 有氏而身份已落 = 寒门；从来没有氏 = 平民。
                return hasShi ? KeyDeclined : KeyCommon;

            return "";
        }

        /// <summary>
        /// 这一档要不要显示「距贵族 N 代」那类与当朝亲缘相关的补充行。
        /// 只有真的和当朝统治家族有关系才有意义 —— 世家/寒门跟当朝没关系，
        /// 显示代数只会让人误以为他们是宗亲。
        /// </summary>
        public static bool ShowsRoyalKinship(string pIdentityKey)
        {
            return pIdentityKey == KeyNoble;
        }

        /// <summary>
        /// 已故者的身份词。人死了，当朝亲缘还算不算？算 —— 皇亲国戚的身份
        /// 不因死亡消失，只因**政权更替**消失，而政权更替已经体现在
        /// <paramref name="pRulingShi"/> 里了（该氏不再当朝，信号自然为假）。
        /// 所以死者和生者走同一条判定，这个方法只是把意图写明。
        /// </summary>
        public static string ClassifyDeceased(string pStatus, long pShiId,
            bool pRulingShi)
        {
            return Classify(pStatus, pShiId, pRulingShi);
        }
    }
}
