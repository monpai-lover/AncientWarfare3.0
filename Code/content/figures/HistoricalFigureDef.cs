namespace AncientWarfare3.content.figures
{
    /// <summary>
    ///     一个历史人物的静态只读定义(移植自 AW2 SpecialFigure 的 5 人名单)。
    ///
    ///     名字拆解:全名 = 氏/姓 + 名。AW3 命名规则(LineageService.ApplyDisplayName 合流前贵族男)
    ///     拼 display = (clan?:family) + given,所以这里 Given 只放"名"(发/政/邦/丕/炎),
    ///     Family/Clan 放姓/氏,降临注入后由 ApplyDisplayName 自动拼回全名(姬发/嬴政/…)。
    ///
    ///     RequiresIntegration:用户定调"按历史从刘邦开始需姓氏合流后才出现"——
    ///     周/秦是先秦(姓氏未合一)无门槛;汉(刘邦)起姓氏合流,需世上已有夏人国完成合流。
    /// </summary>
    public sealed class HistoricalFigureDef
    {
        public readonly int Order;                 // 严格生成顺序 0..4
        public readonly string Key;                // 全名(姬发),仅作标识/日志
        public readonly string FamilyName;         // 姓
        public readonly string ClanName;           // 氏
        public readonly string GivenName;          // 名(单名)
        public readonly string KingdomName;        // 预留国名(成为 king 时套用)
        public readonly bool RequiresIntegration;  // 是否需"世上已有夏人国姓氏合流"才出现
        public readonly float Chance;              // 命中概率(掷骰用私有 System.Random)

        private HistoricalFigureDef(int pOrder, string pKey, string pFamily, string pClan, string pGiven,
            string pKingdom, bool pReqIntegration, float pChance)
        {
            Order = pOrder;
            Key = pKey;
            FamilyName = pFamily;
            ClanName = pClan;
            GivenName = pGiven;
            KingdomName = pKingdom;
            RequiresIntegration = pReqIntegration;
            Chance = pChance;
        }

        /// <summary>严格顺序名单:姬发→嬴政→刘邦→曹丕→司马炎(AW2 原样姓氏/国名)。</summary>
        public static readonly HistoricalFigureDef[] All =
        {
            //                 Order Key      姓     氏     名    国名  需合流  概率
            new HistoricalFigureDef(0, "姬发",   "姬",   "姬",   "发", "周", false, 0.80f),
            new HistoricalFigureDef(1, "嬴政",   "嬴",   "赵",   "政", "秦", false, 0.005f),
            new HistoricalFigureDef(2, "刘邦",   "刘",   "刘",   "邦", "汉", true,  0.005f),
            new HistoricalFigureDef(3, "曹丕",   "曹",   "曹",   "丕", "魏", true,  0.005f),
            new HistoricalFigureDef(4, "司马炎", "司马", "司马", "炎", "晋", true,  0.005f),
        };

        public static HistoricalFigureDef Get(int pOrder)
        {
            if (pOrder < 0 || pOrder >= All.Length) return null;
            return All[pOrder];
        }

        public const int Count = 5;
    }
}
