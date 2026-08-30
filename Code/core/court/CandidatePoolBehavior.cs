namespace AncientWarfare3.core.court
{
    /// <summary>
    /// 候选池的**行为类**:决定一个人排在哪里的那几个量。
    ///
    /// 城官候选表原来按 (城, 官职, 通道) 缓存,于是一个王国有几十个
    /// (城 × 官职) 组合就要把候选池重扫几十遍。实测一次补缺 381 次建表、
    /// 遍历 58240 行 —— 而这几十张表里绝大多数**内容完全一样**。
    ///
    /// 因为排序只看三样:门第档次(有功名/世家/寒门)、评分、actor id。
    /// 档次和评分里除了籍贯加成,全都只取决于这个席位的
    /// <b>品级</b>、<b>是不是方镇主官</b>、<b>走不走空缺晋升</b> —— 也就是
    /// 这个结构。同品级的两个官职,候选顺序逐字节相同。
    ///
    /// 所以池子按行为类建一次,同类的席位直接共用。籍贯加成是唯一按城变的
    /// 项,由 <see cref="LocalOfficialCandidateRules.HometownBonus"/> 封顶,
    /// 在共享表的头部做一次有界重排即可 —— 见
    /// <see cref="CityShortlistRules.NeedsMoreForHometownBonus"/>。
    ///
    /// 资格闸(<c>CanReceiveFormalCivilAppointment</c>)还要吃 officeId,
    /// 但对城层来说它只在一处按 officeId 变:<c>IsAppointmentExempt</c> 里
    /// 「此人当前正担任的就是这个官职」。那种人必然已在
    /// <c>ReservedActorIds</c> 里,取人时会被跳过 —— 也就是说共享表只会
    /// **多收**这种人,不会漏收。多收由取人时的占用判定兜住,而漏收才会
    /// 让官位补不上。方向是安全的那一边。
    /// </summary>
    internal readonly struct CandidatePoolBehavior
    {
        internal readonly int OfficeGrade;
        internal readonly bool RegionalGovernor;
        internal readonly bool VacancyPromotion;
        internal readonly bool Strict;

        internal CandidatePoolBehavior(int pOfficeGrade,
            bool pRegionalGovernor, bool pVacancyPromotion, bool pStrict)
        {
            OfficeGrade = pOfficeGrade;
            RegionalGovernor = pRegionalGovernor;
            VacancyPromotion = pVacancyPromotion;
            Strict = pStrict;
        }

        /// <summary>
        /// 字典键。四个量都是小基数,拼成一个 int 即可 —— 品级只有几个固定值,
        /// 其余三个是布尔。<see cref="FromKey"/> 能完整还原,所以事件补入时
        /// 拿着键就能算出这一类的排序键。
        /// </summary>
        internal int Key()
        {
            return (OfficeGrade << 3) |
                   (RegionalGovernor ? 4 : 0) |
                   (VacancyPromotion ? 2 : 0) |
                   (Strict ? 1 : 0);
        }

        internal static CandidatePoolBehavior FromKey(int pKey)
        {
            return new CandidatePoolBehavior(pKey >> 3, (pKey & 4) != 0,
                (pKey & 2) != 0, (pKey & 1) != 0);
        }
    }
}
