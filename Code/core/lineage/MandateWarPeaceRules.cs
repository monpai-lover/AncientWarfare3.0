namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     天命战争的和局判据。纯函数，便于测试。
    ///
    ///     天命之争不是一场可以「打赢就收」的边境战：攻下都城不等于取代
    ///     一个王朝。原版/本模组的战功结算会在都城易主那一刻把战争分数顶到
    ///     决定性区间、或判定战争目标达成，于是战争当场结束 —— 都城刚破，
    ///     旧朝的法理圈还没来得及移交、残余宗室还没来得及分裂，一切就都停了。
    ///
    ///     所以这里挡掉的只有**「战功/战争目标自动收局」**这一路。军事上被
    ///     彻底消灭、或双方厌战到底，仍然照常结束 —— 天命之争该由刀兵了断，
    ///     不该由记分板了断。
    ///
    ///     同类先例：<see cref="ZhuluWarRules"/>（逐鹿之战）与叛乱战争都用
    ///     同一套「不走寻常和局」的写法。
    /// </summary>
    internal static class MandateWarPeaceRules
    {
        internal const string SettlementBlockedReason =
            "mandate_war_no_ordinary_settlement";

        internal static bool BlocksOrdinarySettlement(string pWarType,
            bool pActive)
        {
            return pActive &&
                   (pWarType == MandateService.WAR_TIANMING ||
                    pWarType == MandateService.WAR_TIANMING_REBEL);
        }
    }
}
